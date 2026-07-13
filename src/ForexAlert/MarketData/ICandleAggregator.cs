using ForexAlert.Domain;

namespace ForexAlert.MarketData;

public interface ICandleAggregator
{
    IReadOnlyCollection<TimeSpan> Intervals { get; }

    bool Add(PricePoint pricePoint);

    IReadOnlyList<Candle> CompleteThrough(DateTimeOffset watermarkUtc);

    void DiscardIncomplete(DateTimeOffset resetAtUtc);
}
