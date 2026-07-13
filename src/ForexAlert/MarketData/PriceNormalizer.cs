using ForexAlert.Domain;

namespace ForexAlert.MarketData;

public static class PriceNormalizer
{
    public static (QuoteSide Side, double Price) Normalize(
        QuoteSide rawSide,
        double rawPrice,
        bool inverse)
    {
        if (!PriceMath.IsValid(rawPrice))
        {
            throw new ArgumentOutOfRangeException(nameof(rawPrice));
        }

        if (!inverse)
        {
            return (rawSide, rawPrice);
        }

        return rawSide == QuoteSide.Bid
            ? (QuoteSide.Ask, 1d / rawPrice)
            : (QuoteSide.Bid, 1d / rawPrice);
    }

    public static Candle Normalize(Candle rawCandle, CurrencyPair requestedPair, bool inverse)
    {
        if (!inverse)
        {
            return rawCandle.Pair == requestedPair
                ? rawCandle
                : new Candle(requestedPair, rawCandle.StartUtc, rawCandle.Interval, rawCandle.Open, rawCandle.High, rawCandle.Low, rawCandle.Close);
        }

        return new Candle(
            requestedPair,
            rawCandle.StartUtc,
            rawCandle.Interval,
            1d / rawCandle.Open,
            1d / rawCandle.Low,
            1d / rawCandle.High,
            1d / rawCandle.Close);
    }
}
