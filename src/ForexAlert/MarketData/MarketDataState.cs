using System.Collections.Concurrent;
using ForexAlert.Domain;
using Microsoft.Extensions.Options;

namespace ForexAlert.MarketData;

public sealed class MarketDataState
{
    private readonly ConcurrentDictionary<CurrencyPair, QuoteState> _quotes = new();
    private readonly ConcurrentDictionary<SeriesKey, CandleSeries> _history = new();
    private readonly CandleAggregator _candles;
    private readonly TimeSpan _maximumQuoteAge;
    private readonly TimeSpan _maximumBidAskSkew;
    private readonly int _historyCapacity;
    private readonly object _liveGate = new();

    public MarketDataState(IOptions<ForexAlertOptions> app, IOptions<IbkrOptions> ibkr, TimeProvider timeProvider)
    {
        _maximumQuoteAge = ibkr.Value.QuoteStaleAfter;
        _maximumBidAskSkew = ibkr.Value.MaximumBidAskSkew;
        _historyCapacity = app.Value.MaxCandlesPerInterval;
        _candles = new CandleAggregator([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)], timeProvider.GetUtcNow());
    }

    public void ProcessQuote(QuoteUpdate update)
    {
        lock (_liveGate)
        {
            if (!UpdateQuote(update) || !TryGetMidPrice(update.Pair, update.TimestampUtc, out PricePoint? midpoint) || midpoint is null) return;
            _candles.Add(midpoint);
        }
    }

    public void AddHistoricalCandle(Candle candle) => AddCompleted(candle);

    public bool HasFreshMidpoint(CurrencyPair pair, DateTimeOffset asOfUtc) => TryGetMidPrice(pair, asOfUtc, out _);

    public bool TryGetMidPrice(CurrencyPair pair, DateTimeOffset asOfUtc, out PricePoint? midpoint)
    {
        midpoint = null;
        if (!_quotes.TryGetValue(pair, out QuoteState? state)) return false;
        DateTimeOffset now = asOfUtc.ToUniversalTime();
        lock (state.Gate)
        {
            if (!state.Bid.HasValue || !state.Ask.HasValue ||
                state.Bid.TimestampUtc > now || state.Ask.TimestampUtc > now ||
                now - state.Bid.TimestampUtc > _maximumQuoteAge || now - state.Ask.TimestampUtc > _maximumQuoteAge ||
                (state.Bid.TimestampUtc - state.Ask.TimestampUtc).Duration() > _maximumBidAskSkew ||
                state.Bid.Price > state.Ask.Price) return false;
            DateTimeOffset timestamp = state.Bid.TimestampUtc >= state.Ask.TimestampUtc ? state.Bid.TimestampUtc : state.Ask.TimestampUtc;
            midpoint = new PricePoint(pair, (state.Bid.Price + state.Ask.Price) / 2d, timestamp);
            return true;
        }
    }

    public IReadOnlyList<Candle> GetCompletedCandles(CurrencyPair pair, TimeSpan interval, DateTimeOffset asOfUtc, int maximumCount)
    {
        if (maximumCount <= 0 || !_history.TryGetValue(new SeriesKey(pair, interval), out CandleSeries? series)) return [];
        DateTimeOffset now = asOfUtc.ToUniversalTime();
        lock (series.Gate) return series.Candles.Values.Where(candle => candle.EndUtc <= now).TakeLast(maximumCount).ToArray();
    }

    public IReadOnlyList<Candle> AdvanceTo(DateTimeOffset watermarkUtc)
    {
        lock (_liveGate)
        {
            IReadOnlyList<Candle> completed = _candles.CompleteThrough(watermarkUtc);
            foreach (Candle candle in completed) AddCompleted(candle);
            return completed;
        }
    }

    public void ResetAfterDisconnect(DateTimeOffset resetAtUtc)
    {
        lock (_liveGate)
        {
            _quotes.Clear();
            _candles.DiscardIncomplete(resetAtUtc);
        }
    }

    private bool UpdateQuote(QuoteUpdate update)
    {
        if (!PriceMath.IsValid(update.Price)) return false;
        QuoteState state = _quotes.GetOrAdd(update.Pair, static _ => new QuoteState());
        DateTimeOffset timestamp = update.TimestampUtc.ToUniversalTime();
        lock (state.Gate)
        {
            ref QuoteSideState side = ref (update.Side == QuoteSide.Bid ? ref state.Bid : ref state.Ask);
            if (side.HasValue && timestamp < side.TimestampUtc) return false;
            side = new QuoteSideState(true, update.Price, timestamp);
            return true;
        }
    }

    private void AddCompleted(Candle candle)
    {
        CandleSeries series = _history.GetOrAdd(new SeriesKey(candle.Pair, candle.Interval), static _ => new CandleSeries());
        lock (series.Gate)
        {
            series.Candles[candle.StartUtc] = candle;
            while (series.Candles.Count > _historyCapacity) series.Candles.Remove(series.Candles.Keys.First());
        }
    }

    private readonly record struct SeriesKey(CurrencyPair Pair, TimeSpan Interval);
    private readonly record struct QuoteSideState(bool HasValue, double Price, DateTimeOffset TimestampUtc);
    private sealed class QuoteState { public object Gate { get; } = new(); public QuoteSideState Bid; public QuoteSideState Ask; }
    private sealed class CandleSeries { public object Gate { get; } = new(); public SortedDictionary<DateTimeOffset, Candle> Candles { get; } = []; }
}
