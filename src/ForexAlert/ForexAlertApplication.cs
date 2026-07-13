using ForexAlert.Alerts;
using ForexAlert.Alerts.Rules;
using ForexAlert.Infrastructure.Ibkr;
using ForexAlert.MarketData;
using ForexAlert.Notifications;
using ForexAlert.Persistence;
using ForexAlert.Scheduling;
using ForexAlert.Simulation;
using ForexAlert.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ForexAlert;

public static class ForexAlertApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        CommandLineOptions commandLine;
        try
        {
            commandLine = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 2;
        }

        if (commandLine.ShowHelp)
        {
            await Console.Out.WriteLineAsync(CommandLineOptions.HelpText).ConfigureAwait(false);
            return 0;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddEnvironmentVariables("FOREXALERT_");

        Dictionary<string, string?> commandLineOverrides = [];
        if (commandLine.DryRun)
        {
            commandLineOverrides["Notifications:DryRun"] =
                bool.TrueString;
        }
        bool simulationMode = commandLine.SimulationFixture is not null;
        if (simulationMode)
        {
            commandLineOverrides["Notifications:DryRun"] =
                bool.TrueString;

            NotificationProvider configuredProvider =
                builder.Configuration.GetValue<NotificationProvider>(
                    "Notifications:Provider");

            // Preserve LocalFile so it can be tested safely using the
            // offline simulation. All other providers use Console.
            if (configuredProvider != NotificationProvider.LocalFile)
            {
                commandLineOverrides["Notifications:Provider"] =
                    nameof(NotificationProvider.Console);
            }
        }

        builder.Configuration.AddInMemoryCollection(commandLineOverrides);
        ConfigureServices(builder.Services, builder.Configuration, commandLine, simulationMode);

        try
        {
            using IHost host = builder.Build();
            ValidateOptions(host.Services);

            if (commandLine.ValidateConfiguration)
            {
                NotificationOptions notification = host.Services.GetRequiredService<IOptions<NotificationOptions>>().Value;
                string externalDelivery = notification.Provider == NotificationProvider.Twilio && !notification.DryRun
                    ? "enabled"
                    : "disabled";
                await Console.Out.WriteLineAsync(
                    $"Configuration is valid. Notification provider: {notification.Provider}; external delivery: {externalDelivery}.")
                    .ConfigureAwait(false);
                return 0;
            }

            if (commandLine.SimulationFixture is not null)
            {
                SimulationRunner runner = host.Services.GetRequiredService<SimulationRunner>();
                _ = await runner.RunAsync(commandLine.SimulationFixture, CancellationToken.None).ConfigureAwait(false);
                return 0;
            }

            await host.RunAsync().ConfigureAwait(false);
            return host.Services.GetRequiredService<ApplicationRunResult>().ExitCode;
        }
        catch (OptionsValidationException exception)
        {
            foreach (string failure in exception.Failures)
            {
                await Console.Error.WriteLineAsync($"Configuration error: {failure}").ConfigureAwait(false);
            }

            return 2;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"ForexAlert stopped: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        CommandLineOptions commandLine,
        bool simulationMode)
    {
        services.AddSingleton(commandLine);
        services.AddSingleton<ApplicationRunResult>();
        services.AddSingleton(TimeProvider.System);
        AddValidatedOptions(services, configuration);

        services.AddSingleton<ConfiguredCurrencyPairs>();
        services.AddSingleton<IPriceStateStore, PriceStateStore>();
        services.AddSingleton<IPriceHistoryStore, PriceHistoryStore>();
        services.AddSingleton<ICandleAggregator>(provider => new CandleAggregator(
            [TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)],
            provider.GetRequiredService<TimeProvider>().GetUtcNow()));
        services.AddSingleton<IMarketDataPipeline, MarketDataPipeline>();
        services.AddSingleton<IMarketSchedule, FxMarketSchedule>();

        services.AddSingleton<IAlertRule, DailyMovementRule>();
        services.AddSingleton<IAlertRule, HourlyMovementRule>();
        services.AddSingleton<IAlertRule, WeeklyMovementRule>();
        services.AddSingleton<IAlertRule, OneMinuteMovementRule>();
        services.AddSingleton<IAlertEngine, AlertEngine>();

        services.AddSingleton<ConsoleNotificationSender>();
        services.AddSingleton<DryRunNotificationSender>();
        services.AddSingleton<NullNotificationSender>();
        services.AddSingleton<LocalFileNotificationSender>();
        services.AddSingleton<TwilioNotificationSender>();
        services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri("https://api.twilio.com/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30),
        });
        services.AddSingleton<INotificationSender>(CreateNotificationSender);

        services.AddSingleton<IAlertStateStore>(simulationMode
            ? static _ => new InMemoryAlertStateStore()
            : static provider => new JsonAlertStateStore(provider.GetRequiredService<IOptions<PersistenceOptions>>()));
        services.AddSingleton<SimulationRunner>();

        if (!simulationMode && !commandLine.ValidateConfiguration)
        {
            services.AddSingleton<RequestIdRegistry>();
#if IBAPI_AVAILABLE
            services.AddSingleton<IIbkrTransport, IbkrSocketTransport>();
#else
            services.AddSingleton<IIbkrTransport, UnavailableIbkrTransport>();
#endif
            services.AddSingleton<IIbkrMarketDataClient, IbkrMarketDataClient>();
            services.AddHostedService<MarketMonitoringService>();
        }
    }

    private static INotificationSender CreateNotificationSender(IServiceProvider provider)
    {
        NotificationOptions options = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;
        if (options.Provider == NotificationProvider.Twilio && options.DryRun)
        {
            return provider.GetRequiredService<DryRunNotificationSender>();
        }

        return options.Provider switch
        {
            NotificationProvider.Console => provider.GetRequiredService<ConsoleNotificationSender>(),
            NotificationProvider.DryRun => provider.GetRequiredService<DryRunNotificationSender>(),
            NotificationProvider.Null => provider.GetRequiredService<NullNotificationSender>(),
            NotificationProvider.Twilio => provider.GetRequiredService<TwilioNotificationSender>(),
            NotificationProvider.LocalFile => provider.GetRequiredService<LocalFileNotificationSender>(),
            _ => throw new InvalidOperationException($"Unsupported notification provider '{options.Provider}'."),
        };
    }

    private static void AddValidatedOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IbkrOptions>().Bind(configuration.GetSection(ConfigurationSections.Ibkr)).ValidateOnStart();
        services.AddOptions<TwilioOptions>().Bind(configuration.GetSection(ConfigurationSections.Twilio)).ValidateOnStart();
        services.AddOptions<AlertOptions>().Bind(configuration.GetSection(ConfigurationSections.Alerts)).ValidateOnStart();
        services.AddOptions<MarketScheduleOptions>().Bind(configuration.GetSection(ConfigurationSections.MarketSchedule)).ValidateOnStart();
        services.AddOptions<CurrencyPairOptions>().Bind(configuration.GetSection(ConfigurationSections.CurrencyPairs)).ValidateOnStart();
        services.AddOptions<PersistenceOptions>().Bind(configuration.GetSection(ConfigurationSections.Persistence)).ValidateOnStart();
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(ConfigurationSections.Notifications)).ValidateOnStart();

        services.AddSingleton<IValidateOptions<IbkrOptions>, IbkrOptionsValidator>();
        services.AddSingleton<IValidateOptions<TwilioOptions>, TwilioOptionsValidator>();
        services.AddSingleton<IValidateOptions<AlertOptions>, AlertOptionsValidator>();
        services.AddSingleton<IValidateOptions<MarketScheduleOptions>, MarketScheduleOptionsValidator>();
        services.AddSingleton<IValidateOptions<CurrencyPairOptions>, CurrencyPairOptionsValidator>();
        services.AddSingleton<IValidateOptions<PersistenceOptions>, PersistenceOptionsValidator>();
        services.AddSingleton<IValidateOptions<PersistenceOptions>, PersistenceHistoryCapacityValidator>();
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.AddOptions<LocalFileOptions>()
    .Bind(configuration.GetSection(ConfigurationSections.LocalFile))
    .ValidateOnStart();
        services.AddSingleton<
        IValidateOptions<LocalFileOptions>,
        LocalFileOptionsValidator>();
    }

    private static void ValidateOptions(IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptions<IbkrOptions>>().Value;
        _ = services.GetRequiredService<IOptions<NotificationOptions>>().Value;
        _ = services.GetRequiredService<IOptions<TwilioOptions>>().Value;
        _ = services.GetRequiredService<IOptions<AlertOptions>>().Value;
        _ = services.GetRequiredService<IOptions<MarketScheduleOptions>>().Value;
        _ = services.GetRequiredService<IOptions<CurrencyPairOptions>>().Value;
        _ = services.GetRequiredService<IOptions<PersistenceOptions>>().Value;
        _ = services.GetRequiredService<ConfiguredCurrencyPairs>().Values;
        _ = services
    .GetRequiredService<IOptions<LocalFileOptions>>()
    .Value;
    }
}
