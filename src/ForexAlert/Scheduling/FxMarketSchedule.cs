using System.Globalization;
using Microsoft.Extensions.Options;

namespace ForexAlert.Scheduling;

public sealed class FxMarketSchedule : IMarketSchedule
{
    private readonly MarketScheduleOptions _options;
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeSpan _weeklyClose;
    private readonly TimeSpan _weeklyOpen;

    public FxMarketSchedule(IOptions<MarketScheduleOptions> options)
    {
        _options = options.Value;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        _weeklyClose = TimeSpan.FromDays((int)_options.FridayCloseDay).Add(_options.FridayCloseTime);
        _weeklyOpen = TimeSpan.FromDays((int)_options.SundayOpenDay).Add(_options.SundayOpenTime);
    }

    public bool IsOpen(DateTimeOffset timestampUtc)
    {
        DateTimeOffset marketTime = ToMarketTime(timestampUtc);
        TimeSpan weekPosition = TimeSpan.FromDays((int)marketTime.DayOfWeek).Add(marketTime.TimeOfDay);

        bool isClosed = _weeklyClose <= _weeklyOpen
            ? weekPosition >= _weeklyClose && weekPosition < _weeklyOpen
            : weekPosition >= _weeklyClose || weekPosition < _weeklyOpen;
        return !isClosed;
    }

    public bool IsSleepWindow(DateTimeOffset timestampUtc)
    {
        TimeSpan marketTime = ToMarketTime(timestampUtc).TimeOfDay;
        return _options.SleepWindowStart < _options.SleepWindowEnd
            ? marketTime >= _options.SleepWindowStart && marketTime < _options.SleepWindowEnd
            : marketTime >= _options.SleepWindowStart || marketTime < _options.SleepWindowEnd;
    }

    public DateTimeOffset ToMarketTime(DateTimeOffset timestampUtc) =>
        TimeZoneInfo.ConvertTime(timestampUtc.ToUniversalTime(), _timeZone);

    public DateOnly GetMarketDate(DateTimeOffset timestampUtc) =>
        DateOnly.FromDateTime(ToMarketTime(timestampUtc).Date);

    public DateTimeOffset GetTradingDayOpenUtc(DateTimeOffset timestampUtc)
    {
        DateTimeOffset marketTime = ToMarketTime(timestampUtc);
        DateTime openingDate = marketTime.Date;
        TimeSpan openingTime = marketTime.DayOfWeek == _options.SundayOpenDay
            ? _options.SundayOpenTime
            : _options.TradingDayOpenTime;
        if (marketTime.TimeOfDay < openingTime)
        {
            openingDate = openingDate.AddDays(-1);
            openingTime = openingDate.DayOfWeek == _options.SundayOpenDay
                ? _options.SundayOpenTime
                : _options.TradingDayOpenTime;
        }

        DateTime localOpening = DateTime.SpecifyKind(
            openingDate.Add(openingTime),
            DateTimeKind.Unspecified);
        if (_timeZone.IsInvalidTime(localOpening))
        {
            throw new InvalidOperationException(
                $"Configured trading-day opening {localOpening:O} is invalid in time zone {_options.TimeZone}.");
        }

        DateTime utcOpening = TimeZoneInfo.ConvertTimeToUtc(localOpening, _timeZone);
        return new DateTimeOffset(utcOpening, TimeSpan.Zero);
    }

    public string FormatMarketTime(DateTimeOffset timestampUtc) =>
        $"{ToMarketTime(timestampUtc).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)} ({_options.TimeZone})";
}
