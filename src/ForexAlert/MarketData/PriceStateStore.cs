using System.Collections.Concurrent;
using ForexAlert.Domain;

namespace ForexAlert.MarketData;

public sealed class PriceStateStore : IPriceStateStore
{
    private readonly ConcurrentDictionary<CurrencyPair, QuoteState> _quotes = new();

    public bool Update(QuoteUpdate update)
    {
        if (!PriceMath.IsValid(update.Price))
        {
            return false;
        }

        QuoteState state = _quotes.GetOrAdd(update.Pair, static _ => new QuoteState());
        DateTimeOffset timestampUtc = update.TimestampUtc.ToUniversalTime();
        lock (state.Gate)
        {
            ref QuoteSideState side = ref (update.Side == QuoteSide.Bid ? ref state.Bid : ref state.Ask);
            if (side.HasValue && timestampUtc < side.TimestampUtc)
            {
                return false;
            }

            side = new QuoteSideState(true, update.Price, timestampUtc);
            return true;
        }
    }

    public bool TryGetMidPrice(
        CurrencyPair pair,
        DateTimeOffset asOfUtc,
        TimeSpan maximumAge,
        out PricePoint? pricePoint) =>
        TryGetMidPrice(pair, asOfUtc, maximumAge, maximumAge, out pricePoint);

    public bool TryGetMidPrice(
        CurrencyPair pair,
        DateTimeOffset asOfUtc,
        TimeSpan maximumAge,
        TimeSpan maximumBidAskSkew,
        out PricePoint? pricePoint)
    {
        pricePoint = null;
        if (maximumAge <= TimeSpan.Zero || maximumBidAskSkew <= TimeSpan.Zero ||
            !_quotes.TryGetValue(pair, out QuoteState? state))
        {
            return false;
        }

        DateTimeOffset normalizedAsOf = asOfUtc.ToUniversalTime();
        lock (state.Gate)
        {
            if (!state.Bid.HasValue || !state.Ask.HasValue ||
                state.Bid.TimestampUtc > normalizedAsOf || state.Ask.TimestampUtc > normalizedAsOf ||
                normalizedAsOf - state.Bid.TimestampUtc > maximumAge ||
                normalizedAsOf - state.Ask.TimestampUtc > maximumAge ||
                (state.Bid.TimestampUtc - state.Ask.TimestampUtc).Duration() > maximumBidAskSkew ||
                state.Bid.Price > state.Ask.Price)
            {
                return false;
            }

            DateTimeOffset timestamp = state.Bid.TimestampUtc >= state.Ask.TimestampUtc
                ? state.Bid.TimestampUtc
                : state.Ask.TimestampUtc;
            pricePoint = new PricePoint(pair, (state.Bid.Price + state.Ask.Price) / 2d, timestamp);
            return true;
        }
    }

    public IReadOnlyList<QuoteSnapshot> GetSnapshot()
    {
        List<QuoteSnapshot> snapshot = [];
        foreach ((CurrencyPair pair, QuoteState state) in _quotes)
        {
            lock (state.Gate)
            {
                if (state.Bid.HasValue && state.Ask.HasValue)
                {
                    snapshot.Add(new QuoteSnapshot(
                        pair,
                        state.Bid.Price,
                        state.Bid.TimestampUtc,
                        state.Ask.Price,
                        state.Ask.TimestampUtc));
                }
            }
        }

        return snapshot;
    }

    public void Clear() => _quotes.Clear();

    private sealed class QuoteState
    {
        public object Gate { get; } = new();

        public QuoteSideState Bid;

        public QuoteSideState Ask;
    }

    private readonly record struct QuoteSideState(bool HasValue, double Price, DateTimeOffset TimestampUtc);
}
