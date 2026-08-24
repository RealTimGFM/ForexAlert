using Microsoft.Extensions.Configuration;

namespace ForexAlert.Tests.Configuration;

public sealed class OptionsValidationTests
{
    [Fact]
    public void Validate_AcceptsSafeLocalFileConfiguration()
    {
        IReadOnlyList<string> errors = OptionsValidation.Validate(
            CreateForexAlertOptions(),
            CreateIbkrOptions(),
            new NotificationOptions
            {
                Provider = NotificationProvider.LocalFile,
                LocalFilePath = "alerts.txt",
            },
            new TwilioOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                SuccessfulRecipientCacheDuration = TimeSpan.FromHours(24),
                SuccessfulRecipientCacheCapacity = 10_000,
            });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsMissingPairsInvalidThresholdAndUnconfiguredTwilio()
    {
        ForexAlertOptions app = CreateForexAlertOptions();
        app.CurrencyPairs = [];
        app.DailyThresholdPercent = 0;

        IReadOnlyList<string> errors = OptionsValidation.Validate(
            app,
            CreateIbkrOptions(),
            new NotificationOptions { Provider = NotificationProvider.Twilio },
            new TwilioOptions());

        Assert.Contains(errors, error => error.Contains("CurrencyPairs", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DailyThreshold", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Twilio credentials", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsMissingRequiredOperationalConfigurationKeys()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        IReadOnlyList<string> errors = OptionsValidation.Validate(
            configuration,
            new ForexAlertOptions(),
            new IbkrOptions(),
            new NotificationOptions(),
            new TwilioOptions());

        Assert.Contains("ForexAlert:OneMinuteEnabled is required.", errors);
        Assert.Contains("ForexAlert:DailyBaseline is required.", errors);
        Assert.Contains("ForexAlert:Cooldown is required.", errors);
        Assert.Contains("Ibkr:PaperTrading is required.", errors);
        Assert.Contains("Ibkr:ClientId is required.", errors);
        Assert.Contains("Ibkr:DisconnectTimeout is required.", errors);
        Assert.Contains("Notification:Provider is required.", errors);
    }

    [Fact]
    public void Validate_RejectsMissingAndInvalidDailyBaseline()
    {
        foreach (DailyBaselineKind? baseline in new DailyBaselineKind?[] { null, (DailyBaselineKind)999 })
        {
            ForexAlertOptions app = CreateForexAlertOptions();
            app.DailyBaseline = baseline;

            IReadOnlyList<string> errors = Validate(app, CreateIbkrOptions());

            Assert.Contains("ForexAlert:DailyBaseline must be PreviousClose or TradingDayOpen.", errors);
        }
    }

    [Fact]
    public void Validate_RejectsNonPositiveDisconnectTimeout()
    {
        IbkrOptions ibkr = CreateIbkrOptions();
        ibkr.DisconnectTimeout = TimeSpan.Zero;

        IReadOnlyList<string> errors = Validate(CreateForexAlertOptions(), ibkr);

        Assert.Contains("Ibkr:DisconnectTimeout must be positive.", errors);
    }

    private static ForexAlertOptions CreateForexAlertOptions() => new()
    {
        CurrencyPairs = ["eur/usd"],
        DailyThresholdPercent = 1.4,
        SleepWindowNegativeThresholdPercent = -2.4,
        HourlyThresholdPercent = 1.4,
        WeeklyThresholdPercent = 5,
        OneMinuteEnabled = false,
        OneMinuteThresholdPercent = 1.4,
        DailyBaseline = DailyBaselineKind.PreviousClose,
        Cooldown = TimeSpan.FromHours(24),
        EvaluationInterval = TimeSpan.FromMinutes(1),
        MarketTimeZone = "America/New_York",
        FridayCloseTime = TimeSpan.FromHours(17),
        SundayOpenTime = TimeSpan.FromHours(17),
        TradingDayOpenTime = TimeSpan.Zero,
        SleepWindowStart = new TimeSpan(23, 30, 0),
        SleepWindowEnd = TimeSpan.FromHours(5),
        MaxCandlesPerInterval = 2_000,
        CooldownStatePath = "cooldowns.json",
    };

    private static IbkrOptions CreateIbkrOptions() => new()
    {
        Host = "127.0.0.1",
        PaperTrading = true,
        PaperPort = 4002,
        LivePort = 4001,
        ClientId = 0,
        ConnectionTimeout = TimeSpan.FromSeconds(10),
        DisconnectTimeout = TimeSpan.FromSeconds(10),
        InitialDataTimeout = TimeSpan.FromSeconds(30),
        MaxRetryAttempts = 5,
        InitialRetryDelay = TimeSpan.FromSeconds(1),
        MaximumRetryDelay = TimeSpan.FromSeconds(30),
        QuoteStaleAfter = TimeSpan.FromSeconds(15),
        MaximumBidAskSkew = TimeSpan.FromSeconds(2),
        HistoricalRequestSpacing = TimeSpan.FromMilliseconds(250),
        DailyHistoryRefreshInterval = TimeSpan.FromMinutes(30),
    };

    private static IReadOnlyList<string> Validate(ForexAlertOptions app, IbkrOptions ibkr) =>
        OptionsValidation.Validate(
            app,
            ibkr,
            new NotificationOptions
            {
                Provider = NotificationProvider.LocalFile,
                LocalFilePath = "alerts.txt",
            },
            new TwilioOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                SuccessfulRecipientCacheDuration = TimeSpan.FromHours(24),
                SuccessfulRecipientCacheCapacity = 10_000,
            });
}
