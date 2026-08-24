using System.Collections.Concurrent;
using ForexAlert.Domain;

namespace ForexAlert.MarketData;

public sealed class CandleAggregator(
    IEnumerable<TimeSpan> intervals,
    DateTimeOffset? startedAtUtc = null)
{
    private readonly TimeSpan[] _intervals = intervals.Distinct().Order().ToArray();
    private long _resetAtUtcTicks =
        startedAtUtc?.ToUniversalTime().UtcTicks ?? 0L;
    private readonly ConcurrentDictionary<SeriesKey, SeriesState> _series = new();

    public IReadOnlyCollection<TimeSpan> Intervals => _intervals;

    public bool Add(PricePoint pricePoint)
    {
        if (!PriceMath.IsValid(pricePoint.Price) || _intervals.Length == 0)
        {
            return false;
        }

        DateTimeOffset timestampUtc = pricePoint.TimestampUtc.ToUniversalTime();
        bool accepted = false;
        foreach (TimeSpan interval in _intervals)
        {
            SeriesState state = _series.GetOrAdd(
                new SeriesKey(pricePoint.Pair, interval),
                static _ => new SeriesState());
            long bucketTicks = timestampUtc.UtcTicks - (timestampUtc.UtcTicks % interval.Ticks);
            long resetAtUtcTicks = Volatile.Read(ref _resetAtUtcTicks);
            if (resetAtUtcTicks > 0)
            {
                long resetBucketTicks =
                    resetAtUtcTicks - (resetAtUtcTicks % interval.Ticks);

                // A reconnect midway through an interval must not produce
                // a partial candle for the remainder of that interval.
                if (bucketTicks == resetBucketTicks &&
                    resetAtUtcTicks > resetBucketTicks)
                {
                    continue;
                }
            }
            lock (state.Gate)
            {
                if (bucketTicks <= state.LastCompletedBucketTicks)
                {
                    continue;
                }

                if (!state.Active.TryGetValue(bucketTicks, out MutableCandle? candle))
                {
                    candle = new MutableCandle(timestampUtc, pricePoint.Price);
                    state.Active.Add(bucketTicks, candle);
                }
                else
                {
                    candle.Add(timestampUtc, pricePoint.Price);
                }

                accepted = true;
            }
        }

        return accepted;
    }

    public IReadOnlyList<Candle> CompleteThrough(DateTimeOffset watermarkUtc)
    {
        DateTimeOffset normalizedWatermark = watermarkUtc.ToUniversalTime();
        List<Candle> completed = [];

        foreach ((SeriesKey key, SeriesState state) in _series)
        {
            lock (state.Gate)
            {
                long[] completedBuckets = state.Active.Keys
                    .Where(bucketTicks => new DateTimeOffset(bucketTicks, TimeSpan.Zero).Add(key.Interval) <= normalizedWatermark)
                    .ToArray();

                foreach (long bucketTicks in completedBuckets)
                {
                    MutableCandle active = state.Active[bucketTicks];
                    completed.Add(active.ToCandle(
                        key.Pair,
                        new DateTimeOffset(bucketTicks, TimeSpan.Zero),
                        key.Interval));
                    state.Active.Remove(bucketTicks);
                    state.LastCompletedBucketTicks = Math.Max(state.LastCompletedBucketTicks, bucketTicks);
                }
            }
        }

        completed.Sort(static (left, right) =>
        {
            int timeComparison = left.StartUtc.CompareTo(right.StartUtc);
            return timeComparison != 0
                ? timeComparison
                : string.CompareOrdinal(left.Pair.CanonicalSymbol, right.Pair.CanonicalSymbol);
        });
        return completed;
    }

    public void DiscardIncomplete(DateTimeOffset resetAtUtc)
    {
        Interlocked.Exchange(
            ref _resetAtUtcTicks,
            resetAtUtc.ToUniversalTime().UtcTicks);

        foreach (SeriesState state in _series.Values)
        {
            lock (state.Gate)
            {
                state.Active.Clear();
            }
        }
    }
    private readonly record struct SeriesKey(CurrencyPair Pair, TimeSpan Interval);

    private sealed class SeriesState
    {
        public object Gate { get; } = new();

        public SortedDictionary<long, MutableCandle> Active { get; } = [];

        public long LastCompletedBucketTicks { get; set; } = long.MinValue;
    }

    private sealed class MutableCandle(DateTimeOffset timestampUtc, double price)
    {
        private DateTimeOffset _openTimestampUtc = timestampUtc;
        private DateTimeOffset _closeTimestampUtc = timestampUtc;
        private double _open = price;
        private double _close = price;
        private double _high = price;
        private double _low = price;

        public void Add(DateTimeOffset timestampUtc, double price)
        {
            if (timestampUtc < _openTimestampUtc)
            {
                _openTimestampUtc = timestampUtc;
                _open = price;
            }

            if (timestampUtc >= _closeTimestampUtc)
            {
                _closeTimestampUtc = timestampUtc;
                _close = price;
            }

            _high = Math.Max(_high, price);
            _low = Math.Min(_low, price);
        }

        public Candle ToCandle(CurrencyPair pair, DateTimeOffset startUtc, TimeSpan interval) =>
            new(pair, startUtc, interval, _open, _high, _low, _close);
    }
}
