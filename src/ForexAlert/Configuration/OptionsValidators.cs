using ForexAlert.Domain;
using Microsoft.Extensions.Options;

namespace ForexAlert.Configuration;

public sealed class LocalFileOptionsValidator :
    IValidateOptions<LocalFileOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        LocalFileOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AlertLogPath))
        {
            return ValidateOptionsResult.Fail(
                "LocalFile:AlertLogPath is required.");
        }

        try
        {
            _ = Path.GetFullPath(options.AlertLogPath);
            return ValidateOptionsResult.Success;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return ValidateOptionsResult.Fail(
                "LocalFile:AlertLogPath is not a valid path.");
        }
    }
}

public sealed class IbkrOptionsValidator : IValidateOptions<IbkrOptions>
{
    public ValidateOptionsResult Validate(string? name, IbkrOptions options)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(options.Host)) errors.Add("Ibkr:Host is required.");
        if (!IsPort(options.PaperPort)) errors.Add("Ibkr:PaperPort must be between 1 and 65535.");
        if (!IsPort(options.LivePort)) errors.Add("Ibkr:LivePort must be between 1 and 65535.");
        if (options.ClientId < 0) errors.Add("Ibkr:ClientId cannot be negative.");
        if (options.ConnectionTimeout <= TimeSpan.Zero || options.ConnectionTimeout > TimeSpan.FromMinutes(5)) errors.Add("Ibkr:ConnectionTimeout must be greater than zero and no more than five minutes.");
        if (options.InitialDataTimeout <= TimeSpan.Zero || options.InitialDataTimeout > TimeSpan.FromMinutes(10)) errors.Add("Ibkr:InitialDataTimeout must be greater than zero and no more than ten minutes.");
        if (options.MaxRetryAttempts is < 1 or > 20) errors.Add("Ibkr:MaxRetryAttempts must be between 1 and 20.");
        if (options.InitialRetryDelay <= TimeSpan.Zero) errors.Add("Ibkr:InitialRetryDelay must be greater than zero.");
        if (options.MaximumRetryDelay < options.InitialRetryDelay) errors.Add("Ibkr:MaximumRetryDelay cannot be less than InitialRetryDelay.");
        if (options.QuoteStaleAfter <= TimeSpan.Zero) errors.Add("Ibkr:QuoteStaleAfter must be greater than zero.");
        if (options.MaximumBidAskSkew <= TimeSpan.Zero) errors.Add("Ibkr:MaximumBidAskSkew must be greater than zero.");
        if (options.HistoricalRequestSpacing < TimeSpan.Zero) errors.Add("Ibkr:HistoricalRequestSpacing cannot be negative.");
        if (options.DailyHistoryRefreshInterval <= TimeSpan.Zero || options.DailyHistoryRefreshInterval > TimeSpan.FromDays(1)) errors.Add("Ibkr:DailyHistoryRefreshInterval must be greater than zero and no more than one day.");
        return Build(errors);
    }

    private static bool IsPort(int value) => value is > 0 and <= 65_535;

    private static ValidateOptionsResult Build(List<string> errors) =>
        errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
}

public sealed class AlertOptionsValidator : IValidateOptions<AlertOptions>
{
    public ValidateOptionsResult Validate(string? name, AlertOptions options)
    {
        List<string> errors = [];
        if (!IsThreshold(options.DailyNormalThresholdPercent)) errors.Add("Alerts:DailyNormalThresholdPercent must be greater than zero and at most 100.");
        if (!double.IsFinite(options.SleepWindowNegativeThresholdPercent) || options.SleepWindowNegativeThresholdPercent >= 0d || options.SleepWindowNegativeThresholdPercent < -100d) errors.Add("Alerts:SleepWindowNegativeThresholdPercent must be finite, negative, and at least -100.");
        if (!IsThreshold(options.HourlyThresholdPercent)) errors.Add("Alerts:HourlyThresholdPercent must be greater than zero and at most 100.");
        if (!IsThreshold(options.WeeklyThresholdPercent)) errors.Add("Alerts:WeeklyThresholdPercent must be greater than zero and at most 100.");
        if (!IsThreshold(options.OneMinuteThresholdPercent)) errors.Add("Alerts:OneMinuteThresholdPercent must be greater than zero and at most 100.");
        if (!Enum.IsDefined(options.DailyBaseline)) errors.Add("Alerts:DailyBaseline is not a supported value.");
        if (options.Cooldown < TimeSpan.Zero) errors.Add("Alerts:Cooldown cannot be negative.");
        if (options.EvaluationInterval <= TimeSpan.Zero) errors.Add("Alerts:EvaluationInterval must be greater than zero.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsThreshold(double value) => double.IsFinite(value) && value is > 0d and <= 100d;
}

public sealed class CurrencyPairOptionsValidator : IValidateOptions<CurrencyPairOptions>
{
    public ValidateOptionsResult Validate(string? name, CurrencyPairOptions options)
    {
        if (options.Pairs is null || options.Pairs.Count == 0)
        {
            return ValidateOptionsResult.Fail("CurrencyPairs:Pairs must contain at least one pair.");
        }

        List<string> errors = [];
        HashSet<CurrencyPair> unique = [];
        foreach (string configuredPair in options.Pairs)
        {
            if (!CurrencyPair.TryParse(configuredPair, out CurrencyPair pair))
            {
                errors.Add($"CurrencyPairs contains invalid pair '{configuredPair}'.");
            }
            else if (!unique.Add(pair))
            {
                errors.Add($"CurrencyPairs contains duplicate pair '{pair}'.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class MarketScheduleOptionsValidator : IValidateOptions<MarketScheduleOptions>
{
    public ValidateOptionsResult Validate(string? name, MarketScheduleOptions options)
    {
        List<string> errors = [];
        if (!Enum.IsDefined(options.FridayCloseDay)) errors.Add("MarketSchedule:FridayCloseDay is not a valid day of week.");
        if (!Enum.IsDefined(options.SundayOpenDay)) errors.Add("MarketSchedule:SundayOpenDay is not a valid day of week.");
        if (string.IsNullOrWhiteSpace(options.TimeZone))
        {
            errors.Add("MarketSchedule:TimeZone is required. Use an IANA identifier such as America/New_York.");
        }
        else try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                errors.Add($"MarketSchedule:TimeZone '{options.TimeZone}' is not available. Use an IANA identifier such as America/New_York.");
            }
            catch (InvalidTimeZoneException)
            {
                errors.Add($"MarketSchedule:TimeZone '{options.TimeZone}' is invalid.");
            }

        ValidateClockTime(options.FridayCloseTime, "FridayCloseTime", errors);
        ValidateClockTime(options.SundayOpenTime, "SundayOpenTime", errors);
        ValidateClockTime(options.TradingDayOpenTime, "TradingDayOpenTime", errors);
        ValidateClockTime(options.SleepWindowStart, "SleepWindowStart", errors);
        ValidateClockTime(options.SleepWindowEnd, "SleepWindowEnd", errors);
        if (options.SleepWindowStart == options.SleepWindowEnd) errors.Add("MarketSchedule sleep-window start and end cannot be equal.");
        if (options.FridayCloseDay == options.SundayOpenDay && options.FridayCloseTime == options.SundayOpenTime) errors.Add("MarketSchedule weekly close and open cannot be the same instant.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateClockTime(TimeSpan value, string name, List<string> errors)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
        {
            errors.Add($"MarketSchedule:{name} must be a time between 00:00:00 and 23:59:59.");
        }
    }
}

public sealed class PersistenceOptionsValidator : IValidateOptions<PersistenceOptions>
{
    public ValidateOptionsResult Validate(string? name, PersistenceOptions options)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(options.AlertStatePath)) errors.Add("Persistence:AlertStatePath is required.");
        else
        {
            try
            {
                _ = Path.GetFullPath(options.AlertStatePath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add("Persistence:AlertStatePath is not a valid path.");
            }
        }
        if (options.MaxCandlesPerInterval is < 5 or > 100_000) errors.Add("Persistence:MaxCandlesPerInterval must be between 5 and 100000.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class TwilioOptionsValidator(IOptions<NotificationOptions> notificationOptions) : IValidateOptions<TwilioOptions>
{
    public ValidateOptionsResult Validate(string? name, TwilioOptions options)
    {
        List<string> errors = [];
        if (options.SuccessfulRecipientCacheDuration <= TimeSpan.Zero ||
            options.SuccessfulRecipientCacheDuration > TimeSpan.FromDays(30))
        {
            errors.Add("Twilio:SuccessfulRecipientCacheDuration must be greater than zero and no more than 30 days.");
        }

        if (options.SuccessfulRecipientCacheCapacity is < 1 or > 1_000_000)
        {
            errors.Add("Twilio:SuccessfulRecipientCacheCapacity must be between 1 and 1000000.");
        }

        NotificationOptions notification = notificationOptions.Value;
        if (notification.Provider != NotificationProvider.Twilio || notification.DryRun)
        {
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }

        if (string.IsNullOrWhiteSpace(options.AccountSid)) errors.Add("Twilio:AccountSid is required when live Twilio delivery is enabled.");
        if (string.IsNullOrWhiteSpace(options.AuthToken)) errors.Add("Twilio:AuthToken is required when live Twilio delivery is enabled.");
        if (!IsE164(options.FromNumber)) errors.Add("Twilio:FromNumber must be a valid E.164 number when live Twilio delivery is enabled.");
        if (options.Recipients is null || options.Recipients.Count == 0 || options.Recipients.Any(static recipient => !IsE164(recipient))) errors.Add("Twilio:Recipients must contain only valid E.164 numbers when live Twilio delivery is enabled.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsE164(string? value) =>
        value is not null && value.Length is >= 9 and <= 16 && value[0] == '+' && value[1..].All(char.IsAsciiDigit);
}

public sealed class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationOptions options) =>
        Enum.IsDefined(options.Provider)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Notifications:Provider is not a supported value.");
}

public sealed class PersistenceHistoryCapacityValidator(IOptions<AlertOptions> alertOptions) : IValidateOptions<PersistenceOptions>
{
    public ValidateOptionsResult Validate(string? name, PersistenceOptions options) =>
        alertOptions.Value.DailyBaseline == DailyBaselineKind.TradingDayOpen &&
        options.MaxCandlesPerInterval < 1_440
            ? ValidateOptionsResult.Fail(
                "Persistence:MaxCandlesPerInterval must be at least 1440 when Alerts:DailyBaseline is TradingDayOpen.")
            : ValidateOptionsResult.Success;
}
