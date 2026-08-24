namespace ForexAlert.Tests.Configuration;

public sealed class OptionsValidationTests
{
    [Fact]
    public void Validate_AcceptsSafeLocalFileConfiguration()
    {
        IReadOnlyList<string> errors = OptionsValidation.Validate(
            new ForexAlertOptions { CurrencyPairs = ["eur/usd"] },
            new IbkrOptions(),
            new NotificationOptions { Provider = NotificationProvider.LocalFile, LocalFilePath = "alerts.txt" },
            new TwilioOptions());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsMissingPairsInvalidThresholdAndUnconfiguredTwilio()
    {
        IReadOnlyList<string> errors = OptionsValidation.Validate(
            new ForexAlertOptions { DailyThresholdPercent = 0 },
            new IbkrOptions(),
            new NotificationOptions { Provider = NotificationProvider.Twilio },
            new TwilioOptions());

        Assert.Contains(errors, error => error.Contains("CurrencyPairs", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DailyThreshold", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Twilio credentials", StringComparison.Ordinal));
    }
}
