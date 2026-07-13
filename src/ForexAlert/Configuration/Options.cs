namespace ForexAlert.Configuration;


public static class ConfigurationSections
{
    public const string Ibkr = "Ibkr";
    public const string Twilio = "Twilio";
    public const string LocalFile = "LocalFile";
    public const string Alerts = "Alerts";
    public const string MarketSchedule = "MarketSchedule";
    public const string CurrencyPairs = "CurrencyPairs";
    public const string Persistence = "Persistence";
    public const string Notifications = "Notifications";
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

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    public string FromNumber { get; set; } = string.Empty;

    public List<string> Recipients { get; set; } = [];

    public TimeSpan SuccessfulRecipientCacheDuration { get; set; } = TimeSpan.FromHours(24);

    public int SuccessfulRecipientCacheCapacity { get; set; } = 10_000;
}

public enum DailyBaselineKind
{
    PreviousClose,
    TradingDayOpen,
}

public sealed class AlertOptions
{
    public double DailyNormalThresholdPercent { get; set; } = 1.4d;

    public double SleepWindowNegativeThresholdPercent { get; set; } = -2.4d;

    public double HourlyThresholdPercent { get; set; } = 1.4d;

    public double WeeklyThresholdPercent { get; set; } = 5d;

    public bool OneMinuteEnabled { get; set; }

    public double OneMinuteThresholdPercent { get; set; } = 1.4d;

    public DailyBaselineKind DailyBaseline { get; set; } = DailyBaselineKind.PreviousClose;

    public TimeSpan Cooldown { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class MarketScheduleOptions
{
    public string TimeZone { get; set; } = "America/New_York";

    public DayOfWeek FridayCloseDay { get; set; } = DayOfWeek.Friday;

    public TimeSpan FridayCloseTime { get; set; } = TimeSpan.FromHours(17);

    public DayOfWeek SundayOpenDay { get; set; } = DayOfWeek.Sunday;

    public TimeSpan SundayOpenTime { get; set; } = TimeSpan.FromHours(17);

    public TimeSpan TradingDayOpenTime { get; set; } = TimeSpan.Zero;

    public TimeSpan SleepWindowStart { get; set; } = new(23, 30, 0);

    public TimeSpan SleepWindowEnd { get; set; } = TimeSpan.FromHours(5);
}

public sealed class CurrencyPairOptions
{
    public List<string> Pairs { get; set; } = [];
}

public sealed class PersistenceOptions
{
    public string AlertStatePath { get; set; } = GetDefaultAlertStatePath();

    public int MaxCandlesPerInterval { get; set; } = 2_000;

    private static string GetDefaultAlertStatePath()
    {
        string applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(applicationData))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            applicationData = string.IsNullOrWhiteSpace(userProfile)
                ? Path.GetTempPath()
                : Path.Combine(userProfile, ".local", "share");
        }

        return Path.Combine(applicationData, "ForexAlert", "alert-state.json");
    }
}

public sealed class LocalFileOptions
{
    public string AlertLogPath { get; set; } = GetDefaultAlertLogPath();

    private static string GetDefaultAlertLogPath()
    {
        string applicationData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(applicationData))
        {
            string userProfile =
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            applicationData = string.IsNullOrWhiteSpace(userProfile)
                ? System.IO.Path.GetTempPath()
                : System.IO.Path.Combine(userProfile, ".local", "share");
        }

        return System.IO.Path.Combine(
            applicationData,
            "ForexAlert",
            "alerts.txt");
    }
}

public enum NotificationProvider
{
    Console,
    DryRun,
    LocalFile,
    Null,
    Twilio,
}

public sealed class NotificationOptions
{
    public NotificationProvider Provider { get; set; } = NotificationProvider.DryRun;

    public bool DryRun { get; set; } = true;
}
