using ForexAlert.Domain;

namespace ForexAlert.MarketData;

public interface IPriceStateStore
{
    bool Update(QuoteUpdate update);

    bool TryGetMidPrice(
        CurrencyPair pair,
        DateTimeOffset asOfUtc,
        TimeSpan maximumAge,
        out PricePoint? pricePoint);

    bool TryGetMidPrice(
        CurrencyPair pair,
        DateTimeOffset asOfUtc,
        TimeSpan maximumAge,
        TimeSpan maximumBidAskSkew,
        out PricePoint? pricePoint);

    IReadOnlyList<QuoteSnapshot> GetSnapshot();

    void Clear();
}
