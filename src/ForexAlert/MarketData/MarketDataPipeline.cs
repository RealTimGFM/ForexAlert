using ForexAlert.Domain;
using Microsoft.Extensions.Options;

namespace ForexAlert.MarketData;

public interface IMarketDataPipeline
{
    void ProcessQuote(QuoteUpdate update);

    void ProcessHistoricalCandle(Candle candle);

    bool HasFreshMidpoint(CurrencyPair pair, DateTimeOffset asOfUtc);

    IReadOnlyList<Candle> AdvanceTo(DateTimeOffset watermarkUtc);

    void DiscardIncompleteLiveCandles(DateTimeOffset resetAtUtc);
}

public sealed class MarketDataPipeline(
    IPriceStateStore priceState,
    IPriceHistoryStore history,
    ICandleAggregator candleAggregator,
    IOptions<IbkrOptions> ibkrOptions) : IMarketDataPipeline
{
    private readonly TimeSpan _maximumQuoteAge = ibkrOptions.Value.QuoteStaleAfter;
    private readonly TimeSpan _maximumBidAskSkew = ibkrOptions.Value.MaximumBidAskSkew;
    private readonly object _liveGate = new();

    public void ProcessQuote(QuoteUpdate update)
    {
        lock (_liveGate)
        {
            if (!priceState.Update(update))
            {
                return;
            }

            if (priceState.TryGetMidPrice(
                update.Pair,
                update.TimestampUtc,
                _maximumQuoteAge,
                _maximumBidAskSkew,
                out PricePoint? midpoint) && midpoint is not null)
            {
                candleAggregator.Add(midpoint);
            }
        }
    }

    public void ProcessHistoricalCandle(Candle candle) => history.AddCompleted(candle);

    public bool HasFreshMidpoint(CurrencyPair pair, DateTimeOffset asOfUtc) =>
        priceState.TryGetMidPrice(
            pair,
            asOfUtc,
            _maximumQuoteAge,
            _maximumBidAskSkew,
            out _);

    public IReadOnlyList<Candle> AdvanceTo(DateTimeOffset watermarkUtc)
    {
        lock (_liveGate)
        {
            IReadOnlyList<Candle> completed =
                candleAggregator.CompleteThrough(watermarkUtc);

            foreach (Candle candle in completed)
            {
                history.AddCompleted(candle);
            }

            return completed;
        }
    }

    public void DiscardIncompleteLiveCandles(DateTimeOffset resetAtUtc)
    {
        lock (_liveGate)
        {
            priceState.Clear();
            candleAggregator.DiscardIncomplete(resetAtUtc);
        }
    }
}
