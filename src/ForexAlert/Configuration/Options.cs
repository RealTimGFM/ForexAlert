using ForexAlert.Domain;

namespace ForexAlert.Configuration;

public sealed class ForexAlertOptions
{
    public List<string> CurrencyPairs { get; set; } = [];
    public double DailyThresholdPercent { get; set; } = 1.4d;
    public double SleepWindowNegativeThresholdPercent { get; set; } = -2.4d;
    public double HourlyThresholdPercent { get; set; } = 1.4d;
    public double WeeklyThresholdPercent { get; set; } = 5d;
    public bool OneMinuteEnabled { get; set; }
    public double OneMinuteThresholdPercent { get; set; } = 1.4d;
    public DailyBaselineKind DailyBaseline { get; set; } = DailyBaselineKind.PreviousClose;
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(1);
    public string MarketTimeZone { get; set; } = "America/New_York";
    public TimeSpan FridayCloseTime { get; set; } = TimeSpan.FromHours(17);
    public TimeSpan SundayOpenTime { get; set; } = TimeSpan.FromHours(17);
    public TimeSpan TradingDayOpenTime { get; set; } = TimeSpan.Zero;
    public TimeSpan SleepWindowStart { get; set; } = new(23, 30, 0);
    public TimeSpan SleepWindowEnd { get; set; } = TimeSpan.FromHours(5);
    public int MaxCandlesPerInterval { get; set; } = 2_000;
    public string CooldownStatePath { get; set; } = DefaultPath("cooldowns.json");

    private static string DefaultPath(string fileName)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(string.IsNullOrWhiteSpace(root) ? Path.GetTempPath() : root, "ForexAlert", fileName);
    }
}

public sealed class IbkrOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public bool PaperTrading { get; set; } = true;
    public int PaperPort { get; set; } = 4002;
    public int LivePort { get; set; } = 4001;
    public int ClientId { get; set; }
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan InitialDataTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 5;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan QuoteStaleAfter { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan MaximumBidAskSkew { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan HistoricalRequestSpacing { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan DailyHistoryRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);
    public int Port => PaperTrading ? PaperPort : LivePort;
}

public enum NotificationProvider { LocalFile, Twilio }

public sealed class NotificationOptions
{
    public NotificationProvider Provider { get; set; } = NotificationProvider.LocalFile;
    public string LocalFilePath { get; set; } = DefaultPath();

    private static string DefaultPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(string.IsNullOrWhiteSpace(root) ? Path.GetTempPath() : root, "ForexAlert", "alerts.txt");
    }
}

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = [];
    public TimeSpan SuccessfulRecipientCacheDuration { get; set; } = TimeSpan.FromHours(24);
    public int SuccessfulRecipientCacheCapacity { get; set; } = 10_000;
}

public enum DailyBaselineKind { PreviousClose, TradingDayOpen }

public static class OptionsValidation
{
    public static IReadOnlyList<string> Validate(ForexAlertOptions app, IbkrOptions ibkr, NotificationOptions notification, TwilioOptions twilio)
    {
        List<string> errors = [];
        ValidatePairs(app, errors);
        ValidateThreshold(app.DailyThresholdPercent, "ForexAlert:DailyThresholdPercent", errors);
        ValidateThreshold(app.HourlyThresholdPercent, "ForexAlert:HourlyThresholdPercent", errors);
        ValidateThreshold(app.WeeklyThresholdPercent, "ForexAlert:WeeklyThresholdPercent", errors);
        ValidateThreshold(app.OneMinuteThresholdPercent, "ForexAlert:OneMinuteThresholdPercent", errors);
        if (!double.IsFinite(app.SleepWindowNegativeThresholdPercent) || app.SleepWindowNegativeThresholdPercent is >= 0d or < -100d) errors.Add("ForexAlert:SleepWindowNegativeThresholdPercent must be between -100 and 0.");
        if (app.Cooldown < TimeSpan.Zero) errors.Add("ForexAlert:Cooldown cannot be negative.");
        if (app.EvaluationInterval <= TimeSpan.Zero) errors.Add("ForexAlert:EvaluationInterval must be positive.");
        if (app.MaxCandlesPerInterval is < 5 or > 100_000) errors.Add("ForexAlert:MaxCandlesPerInterval must be between 5 and 100000.");
        if (app.DailyBaseline == DailyBaselineKind.TradingDayOpen && app.MaxCandlesPerInterval < 1_440) errors.Add("ForexAlert:MaxCandlesPerInterval must be at least 1440 for a trading-day-open baseline.");
        if (!Enum.IsDefined(app.DailyBaseline)) errors.Add("ForexAlert:DailyBaseline is invalid.");
        ValidatePath(app.CooldownStatePath, "ForexAlert:CooldownStatePath", errors);
        ValidateTimeZone(app.MarketTimeZone, errors);
        ValidateClock(app.SleepWindowStart, "ForexAlert:SleepWindowStart", errors);
        ValidateClock(app.SleepWindowEnd, "ForexAlert:SleepWindowEnd", errors);
        ValidateClock(app.FridayCloseTime, "ForexAlert:FridayCloseTime", errors);
        ValidateClock(app.SundayOpenTime, "ForexAlert:SundayOpenTime", errors);
        ValidateClock(app.TradingDayOpenTime, "ForexAlert:TradingDayOpenTime", errors);
        if (app.SleepWindowStart == app.SleepWindowEnd) errors.Add("ForexAlert sleep-window start and end cannot be equal.");

        if (string.IsNullOrWhiteSpace(ibkr.Host)) errors.Add("Ibkr:Host is required.");
        if (!IsPort(ibkr.PaperPort) || !IsPort(ibkr.LivePort)) errors.Add("IBKR ports must be between 1 and 65535.");
        if (ibkr.ClientId < 0) errors.Add("Ibkr:ClientId cannot be negative.");
        if (ibkr.ConnectionTimeout <= TimeSpan.Zero || ibkr.InitialDataTimeout <= TimeSpan.Zero) errors.Add("IBKR timeouts must be positive.");
        if (ibkr.MaxRetryAttempts is < 1 or > 20) errors.Add("Ibkr:MaxRetryAttempts must be between 1 and 20.");
        if (ibkr.InitialRetryDelay <= TimeSpan.Zero || ibkr.MaximumRetryDelay < ibkr.InitialRetryDelay) errors.Add("IBKR retry delays are invalid.");
        if (ibkr.QuoteStaleAfter <= TimeSpan.Zero || ibkr.MaximumBidAskSkew <= TimeSpan.Zero) errors.Add("IBKR quote age and skew limits must be positive.");
        if (ibkr.HistoricalRequestSpacing < TimeSpan.Zero || ibkr.DailyHistoryRefreshInterval <= TimeSpan.Zero) errors.Add("IBKR historical-data intervals are invalid.");

        if (!Enum.IsDefined(notification.Provider)) errors.Add("Notification:Provider must be LocalFile or Twilio.");
        if (notification.Provider == NotificationProvider.LocalFile) ValidatePath(notification.LocalFilePath, "Notification:LocalFilePath", errors);
        if (notification.Provider == NotificationProvider.Twilio)
        {
            if (string.IsNullOrWhiteSpace(twilio.AccountSid) || string.IsNullOrWhiteSpace(twilio.AuthToken)) errors.Add("Twilio credentials are required when Twilio is selected.");
            if (!IsE164(twilio.FromNumber) || twilio.Recipients is null || twilio.Recipients.Count == 0 || twilio.Recipients.Any(number => !IsE164(number))) errors.Add("Twilio numbers must use E.164 format and include at least one recipient.");
        }
        if (twilio.SuccessfulRecipientCacheDuration <= TimeSpan.Zero || twilio.SuccessfulRecipientCacheCapacity <= 0) errors.Add("Twilio recipient-cache settings must be positive.");
        return errors;
    }

    private static void ValidatePairs(ForexAlertOptions options, List<string> errors)
    {
        if (options.CurrencyPairs is null || options.CurrencyPairs.Count == 0) { errors.Add("ForexAlert:CurrencyPairs must contain at least one pair."); return; }
        HashSet<CurrencyPair> pairs = [];
        foreach (string value in options.CurrencyPairs)
        {
            if (!CurrencyPair.TryParse(value, out CurrencyPair pair)) errors.Add($"Invalid currency pair '{value}'.");
            else if (!pairs.Add(pair)) errors.Add($"Duplicate currency pair '{pair}'.");
        }
    }

    private static void ValidateThreshold(double value, string name, List<string> errors)
    { if (!double.IsFinite(value) || value is <= 0d or > 100d) errors.Add($"{name} must be greater than zero and at most 100."); }
    private static void ValidateClock(TimeSpan value, string name, List<string> errors)
    { if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1)) errors.Add($"{name} must be a time of day."); }
    private static void ValidatePath(string value, string name, List<string> errors)
    { try { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(); _ = Path.GetFullPath(value); } catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { errors.Add($"{name} is not a valid path."); } }
    private static void ValidateTimeZone(string value, List<string> errors)
    { try { _ = TimeZoneInfo.FindSystemTimeZoneById(value); } catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException) { errors.Add($"ForexAlert:MarketTimeZone '{value}' is invalid."); } }
    private static bool IsPort(int value) => value is > 0 and <= 65_535;
    private static bool IsE164(string? value) => value is { Length: >= 9 and <= 16 } && value[0] == '+' && value[1..].All(char.IsAsciiDigit);
}
