using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Alerts;

internal sealed class DeterministicAlertRuleMarketSchedule(
    bool isSleepWindow = false,
    DateTimeOffset? tradingDayOpenUtc = null) : IMarketSchedule
{
    public bool IsOpen(DateTimeOffset timestampUtc) => true;

    public bool IsSleepWindow(DateTimeOffset timestampUtc) => isSleepWindow;

    public DateTimeOffset ToMarketTime(DateTimeOffset timestampUtc) => timestampUtc.ToUniversalTime();

    public DateOnly GetMarketDate(DateTimeOffset timestampUtc) =>
        DateOnly.FromDateTime(timestampUtc.ToUniversalTime().UtcDateTime);

    public DateTimeOffset GetTradingDayOpenUtc(DateTimeOffset timestampUtc) =>
        tradingDayOpenUtc ?? new DateTimeOffset(
            timestampUtc.UtcDateTime.Date,
            TimeSpan.Zero);

    public string FormatMarketTime(DateTimeOffset timestampUtc) =>
        $"{timestampUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss zzz} (Test/UTC)";
}

internal sealed class ScriptedCompletedCandleHistory(IEnumerable<Candle> candles) : IPriceHistoryStore
{
    private readonly List<Candle> _candles = candles.ToList();

    public void AddCompleted(Candle candle) => _candles.Add(candle);

    public IReadOnlyList<Candle> GetCompletedCandles(
        CurrencyPair pair,
        TimeSpan interval,
        DateTimeOffset asOfUtc,
        int maximumCount) =>
        _candles
            .Where(candle => candle.Pair == pair && candle.Interval == interval)
            .TakeLast(maximumCount)
            .ToArray();
}

internal static class AlertRuleTestData
{
    public static readonly CurrencyPair EurUsd = CurrencyPair.Parse("EUR/USD");

    public static PriceHistoryStore CreateHistoryStore() =>
        new(Options.Create(new PersistenceOptions { MaxCandlesPerInterval = 100 }));

    public static Candle Candle(
        DateTimeOffset startUtc,
        TimeSpan interval,
        double open,
        double close,
        CurrencyPair? pair = null,
        double? high = null,
        double? low = null) =>
        new(
            pair ?? EurUsd,
            startUtc,
            interval,
            open,
            high ?? Math.Max(open, close),
            low ?? Math.Min(open, close),
            close);

    public static void SetMidPrice(
        PriceStateStore prices,
        CurrencyPair pair,
        double price,
        DateTimeOffset timestampUtc)
    {
        prices.Update(new QuoteUpdate(pair, QuoteSide.Bid, price, timestampUtc));
        prices.Update(new QuoteUpdate(pair, QuoteSide.Ask, price, timestampUtc));
    }
}
