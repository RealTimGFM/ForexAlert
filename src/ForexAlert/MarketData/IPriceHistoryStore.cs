using ForexAlert.Domain;

namespace ForexAlert.MarketData;

public interface IPriceHistoryStore
{
    void AddCompleted(Candle candle);

    IReadOnlyList<Candle> GetCompletedCandles(
        CurrencyPair pair,
        TimeSpan interval,
        DateTimeOffset asOfUtc,
        int maximumCount);
}
