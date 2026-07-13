using System.Collections.Concurrent;
using ForexAlert.Domain;
using Microsoft.Extensions.Options;

namespace ForexAlert.MarketData;

public sealed class PriceHistoryStore(IOptions<PersistenceOptions> options) : IPriceHistoryStore
{
    private readonly int _capacity = options.Value.MaxCandlesPerInterval;
    private readonly ConcurrentDictionary<SeriesKey, CandleSeries> _series = new();

    public void AddCompleted(Candle candle)
    {
        CandleSeries series = _series.GetOrAdd(
            new SeriesKey(candle.Pair, candle.Interval),
            static _ => new CandleSeries());

        lock (series.Gate)
        {
            series.Candles[candle.StartUtc] = candle;
            while (series.Candles.Count > _capacity)
            {
                series.Candles.Remove(series.Candles.Keys.First());
            }
        }
    }

    public IReadOnlyList<Candle> GetCompletedCandles(
        CurrencyPair pair,
        TimeSpan interval,
        DateTimeOffset asOfUtc,
        int maximumCount)
    {
        if (maximumCount <= 0 || !_series.TryGetValue(new SeriesKey(pair, interval), out CandleSeries? series))
        {
            return [];
        }

        DateTimeOffset normalizedAsOf = asOfUtc.ToUniversalTime();
        lock (series.Gate)
        {
            return series.Candles.Values
                .Where(candle => candle.EndUtc <= normalizedAsOf)
                .TakeLast(maximumCount)
                .ToArray();
        }
    }

    private readonly record struct SeriesKey(CurrencyPair Pair, TimeSpan Interval);

    private sealed class CandleSeries
    {
        public object Gate { get; } = new();

        public SortedDictionary<DateTimeOffset, Candle> Candles { get; } = [];
    }
}
