using ForexAlert.Alerts;
using ForexAlert.Infrastructure.Ibkr;
using ForexAlert.MarketData;
using ForexAlert.Notifications;
using ForexAlert.Persistence;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ForexAlert;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            await Console.Error.WriteLineAsync("ForexAlert is configured through appsettings.json and FOREXALERT_ environment variables; command-line options are not supported.");
            return 2;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.AddEnvironmentVariables("FOREXALERT_");
        builder.Services.AddOptions<ForexAlertOptions>().Bind(builder.Configuration.GetSection("ForexAlert"));
        builder.Services.AddOptions<IbkrOptions>().Bind(builder.Configuration.GetSection("Ibkr"));
        builder.Services.AddOptions<NotificationOptions>().Bind(builder.Configuration.GetSection("Notification"));
        builder.Services.AddOptions<TwilioOptions>().Bind(builder.Configuration.GetSection("Twilio"));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<MarketDataState>();
        builder.Services.AddSingleton<FxMarketSchedule>();
        builder.Services.AddSingleton<CooldownStore>();
        builder.Services.AddSingleton<AlertEvaluator>();
        builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri("https://api.twilio.com/"), Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton<LocalFileNotificationSender>();
        builder.Services.AddSingleton<TwilioNotificationSender>();
        builder.Services.AddSingleton<INotificationSender>(provider =>
            provider.GetRequiredService<IOptions<NotificationOptions>>().Value.Provider == NotificationProvider.Twilio
                ? provider.GetRequiredService<TwilioNotificationSender>()
                : provider.GetRequiredService<LocalFileNotificationSender>());
        builder.Services.AddSingleton<RequestIdRegistry>();
#if IBAPI_AVAILABLE
        builder.Services.AddSingleton<IIbkrTransport, IbkrSocketTransport>();
#else
        builder.Services.AddSingleton<IIbkrTransport, UnavailableIbkrTransport>();
#endif
        builder.Services.AddSingleton<IIbkrMarketDataClient, IbkrMarketDataClient>();
        builder.Services.AddHostedService<ForexMonitor>();

        using IHost host = builder.Build();
        IReadOnlyList<string> errors = OptionsValidation.Validate(
            host.Services.GetRequiredService<IOptions<ForexAlertOptions>>().Value,
            host.Services.GetRequiredService<IOptions<IbkrOptions>>().Value,
            host.Services.GetRequiredService<IOptions<NotificationOptions>>().Value,
            host.Services.GetRequiredService<IOptions<TwilioOptions>>().Value);
        if (errors.Count > 0)
        {
            foreach (string error in errors) await Console.Error.WriteLineAsync($"Configuration error: {error}");
            return 2;
        }

        try { await host.RunAsync().ConfigureAwait(false); return 0; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { await Console.Error.WriteLineAsync($"ForexAlert stopped: {exception.Message}"); return 1; }
    }
}
