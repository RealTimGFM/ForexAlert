using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Configuration;

public sealed class OptionsValidationTests
{
    [Fact]
    public void Defaults_KeepExternalDeliveryDisabledAndUsePaperTrading()
    {
        IbkrOptions ibkr = new();
        NotificationOptions notifications = new();
        TwilioOptions twilio = new();
        AlertOptions alerts = new();

        Assert.True(ibkr.PaperTrading);
        Assert.Equal(4002, ibkr.Port);
        Assert.Equal("127.0.0.1", ibkr.Host);
        Assert.Equal(NotificationProvider.DryRun, notifications.Provider);
        Assert.True(notifications.DryRun);
        Assert.Empty(twilio.AccountSid);
        Assert.Empty(twilio.AuthToken);
        Assert.Empty(twilio.FromNumber);
        Assert.Empty(twilio.Recipients);
        Assert.False(alerts.OneMinuteEnabled);
        Assert.Equal(DailyBaselineKind.PreviousClose, alerts.DailyBaseline);
        Assert.True(Path.IsPathFullyQualified(new PersistenceOptions().AlertStatePath));
    }

    [Fact]
    public void Validators_AcceptSafeValidConfiguration()
    {
        Assert.True(new IbkrOptionsValidator().Validate(null, new IbkrOptions()).Succeeded);
        Assert.True(new AlertOptionsValidator().Validate(null, new AlertOptions()).Succeeded);
        Assert.True(new CurrencyPairOptionsValidator().Validate(
            null,
            new CurrencyPairOptions { Pairs = ["EUR/USD", "USD/JPY"] }).Succeeded);
        Assert.True(new MarketScheduleOptionsValidator().Validate(null, new MarketScheduleOptions()).Succeeded);
        Assert.True(new PersistenceOptionsValidator().Validate(null, new PersistenceOptions()).Succeeded);
    }

    [Fact]
    public void IbkrValidator_ReportsEveryUnsafeBoundary()
    {
        IbkrOptions invalid = new()
        {
            Host = " ",
            PaperPort = 0,
            LivePort = 65_536,
            ClientId = -1,
            ConnectionTimeout = TimeSpan.Zero,
            MaxRetryAttempts = 0,
            InitialRetryDelay = TimeSpan.Zero,
            MaximumRetryDelay = TimeSpan.FromMilliseconds(-1),
            QuoteStaleAfter = TimeSpan.Zero,
            MaximumBidAskSkew = TimeSpan.Zero,
            HistoricalRequestSpacing = TimeSpan.FromMilliseconds(-1),
            DailyHistoryRefreshInterval = TimeSpan.FromDays(2),
        };

        ValidateOptionsResult result = new IbkrOptionsValidator().Validate(null, invalid);

        AssertFailuresContain(
            result,
            "Host",
            "PaperPort",
            "LivePort",
            "ClientId",
            "ConnectionTimeout",
            "MaxRetryAttempts",
            "InitialRetryDelay",
            "MaximumRetryDelay",
            "QuoteStaleAfter",
            "MaximumBidAskSkew",
            "HistoricalRequestSpacing",
            "DailyHistoryRefreshInterval");
    }

    [Fact]
    public void AlertValidator_RejectsInvalidThresholdsAndDurations()
    {
        AlertOptions invalid = new()
        {
            DailyNormalThresholdPercent = double.NaN,
            SleepWindowNegativeThresholdPercent = 0,
            HourlyThresholdPercent = double.PositiveInfinity,
            WeeklyThresholdPercent = 101,
            OneMinuteThresholdPercent = 0,
            Cooldown = TimeSpan.FromTicks(-1),
            EvaluationInterval = TimeSpan.Zero,
        };

        ValidateOptionsResult result = new AlertOptionsValidator().Validate(null, invalid);

        AssertFailuresContain(
            result,
            "DailyNormalThresholdPercent",
            "SleepWindowNegativeThresholdPercent",
            "HourlyThresholdPercent",
            "WeeklyThresholdPercent",
            "OneMinuteThresholdPercent",
            "Cooldown",
            "EvaluationInterval");
    }

    [Fact]
    public void CurrencyPairValidator_RejectsMalformedAndCanonicalDuplicates()
    {
        CurrencyPairOptions invalid = new()
        {
            Pairs = ["EURUSD", "eur/usd", "EUR/USD"],
        };

        ValidateOptionsResult result = new CurrencyPairOptionsValidator().Validate(null, invalid);

        AssertFailuresContain(result, "invalid pair", "duplicate pair");
    }

    [Fact]
    public void MarketScheduleValidator_RejectsUnknownZoneAndInvalidClockTimes()
    {
        MarketScheduleOptions invalid = new()
        {
            TimeZone = "Not/A_Real_Zone",
            FridayCloseTime = TimeSpan.FromDays(1),
            SundayOpenTime = TimeSpan.FromTicks(-1),
            SleepWindowStart = TimeSpan.FromHours(5),
            SleepWindowEnd = TimeSpan.FromHours(5),
        };

        ValidateOptionsResult result = new MarketScheduleOptionsValidator().Validate(null, invalid);

        AssertFailuresContain(result, "TimeZone", "FridayCloseTime", "SundayOpenTime", "cannot be equal");
    }

    [Fact]
    public void PersistenceValidator_RejectsBlankPathAndOutOfRangeCapacity()
    {
        PersistenceOptions invalid = new()
        {
            AlertStatePath = " ",
            MaxCandlesPerInterval = 4,
        };

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(null, invalid);

        AssertFailuresContain(result, "AlertStatePath", "MaxCandlesPerInterval");
    }

    [Theory]
    [InlineData(NotificationProvider.DryRun, false)]
    [InlineData(NotificationProvider.Console, false)]
    [InlineData(NotificationProvider.Null, false)]
    [InlineData(NotificationProvider.Twilio, true)]
    public void TwilioValidator_DoesNotRequireSecretsWhenExternalTwilioDeliveryIsDisabled(
        NotificationProvider provider,
        bool dryRun)
    {
        TwilioOptionsValidator validator = CreateTwilioValidator(provider, dryRun);

        ValidateOptionsResult result = validator.Validate(null, new TwilioOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void TwilioValidator_RequiresCredentialsAndE164NumbersForLiveDelivery()
    {
        TwilioOptionsValidator validator = CreateTwilioValidator(NotificationProvider.Twilio, dryRun: false);

        ValidateOptionsResult result = validator.Validate(null, new TwilioOptions());

        AssertFailuresContain(result, "AccountSid", "AuthToken", "FromNumber", "Recipients");
    }

    [Fact]
    public void TwilioValidator_AcceptsNonSecretTestValuesForLiveDelivery()
    {
        TwilioOptionsValidator validator = CreateTwilioValidator(NotificationProvider.Twilio, dryRun: false);
        TwilioOptions options = new()
        {
            AccountSid = "AC_test_account",
            AuthToken = "test-token-not-a-secret",
            FromNumber = "+15555550101",
            Recipients = ["+15555550102"],
        };

        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    private static TwilioOptionsValidator CreateTwilioValidator(
        NotificationProvider provider,
        bool dryRun) =>
        new(Options.Create(new NotificationOptions { Provider = provider, DryRun = dryRun }));

    private static void AssertFailuresContain(ValidateOptionsResult result, params string[] fragments)
    {
        Assert.False(result.Succeeded);
        IEnumerable<string> failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
        foreach (string fragment in fragments)
        {
            Assert.Contains(failures, failure => failure.Contains(fragment, StringComparison.Ordinal));
        }
    }
}
