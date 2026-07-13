namespace ForexAlert.Scheduling;

public interface IMarketSchedule
{
    bool IsOpen(DateTimeOffset timestampUtc);

    bool IsSleepWindow(DateTimeOffset timestampUtc);

    DateTimeOffset ToMarketTime(DateTimeOffset timestampUtc);

    DateOnly GetMarketDate(DateTimeOffset timestampUtc);

    DateTimeOffset GetTradingDayOpenUtc(DateTimeOffset timestampUtc);

    string FormatMarketTime(DateTimeOffset timestampUtc);
}
