using ForexAlert.Domain;
using ForexAlert.MarketData;

namespace ForexAlert.Tests.MarketData;

public sealed class PriceNormalizerTests
{
    private static readonly CurrencyPair EurUsd = CurrencyPair.Parse("EUR/USD");
    private static readonly CurrencyPair UsdEur = CurrencyPair.Parse("USD/EUR");
    private static readonly DateTimeOffset StartUtc = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(QuoteSide.Bid)]
    [InlineData(QuoteSide.Ask)]
    public void NormalizeQuote_WhenNotInverse_PreservesSideAndPrice(QuoteSide side)
    {
        (QuoteSide normalizedSide, double normalizedPrice) = PriceNormalizer.Normalize(side, 1.25d, inverse: false);

        Assert.Equal(side, normalizedSide);
        Assert.Equal(1.25d, normalizedPrice);
    }

    [Theory]
    [InlineData(QuoteSide.Bid, QuoteSide.Ask)]
    [InlineData(QuoteSide.Ask, QuoteSide.Bid)]
    public void NormalizeQuote_WhenInverse_SwapsSideAndReciprocatesPrice(
        QuoteSide rawSide,
        QuoteSide expectedSide)
    {
        (QuoteSide normalizedSide, double normalizedPrice) = PriceNormalizer.Normalize(rawSide, 1.25d, inverse: true);

        Assert.Equal(expectedSide, normalizedSide);
        Assert.Equal(1d / 1.25d, normalizedPrice);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NormalizeQuote_RejectsInvalidRawPrice(double rawPrice)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PriceNormalizer.Normalize(QuoteSide.Bid, rawPrice, inverse: true));

        Assert.Equal("rawPrice", exception.ParamName);
    }

    [Fact]
    public void NormalizeCandle_WhenInverse_ReciprocatesOhlcAndSwapsHighLow()
    {
        Candle raw = new(
            UsdEur,
            StartUtc,
            TimeSpan.FromMinutes(1),
            open: 2d,
            high: 4d,
            low: 1d,
            close: 3d);

        Candle normalized = PriceNormalizer.Normalize(raw, EurUsd, inverse: true);

        Assert.Equal(EurUsd, normalized.Pair);
        Assert.Equal(raw.StartUtc, normalized.StartUtc);
        Assert.Equal(raw.Interval, normalized.Interval);
        Assert.Equal(1d / raw.Open, normalized.Open);
        Assert.Equal(1d / raw.Low, normalized.High);
        Assert.Equal(1d / raw.High, normalized.Low);
        Assert.Equal(1d / raw.Close, normalized.Close);
    }

    [Fact]
    public void NormalizeCandle_WhenNotInverseAndPairMatches_ReturnsOriginalCandle()
    {
        Candle raw = CreateFlatCandle(EurUsd, 1.10d);

        Candle normalized = PriceNormalizer.Normalize(raw, EurUsd, inverse: false);

        Assert.Same(raw, normalized);
    }

    [Fact]
    public void NormalizeCandle_WhenNotInverseAndPairDiffers_RelabelsWithoutChangingOhlc()
    {
        Candle raw = new(
            UsdEur,
            StartUtc,
            TimeSpan.FromMinutes(5),
            open: 1.1d,
            high: 1.4d,
            low: 1d,
            close: 1.2d);

        Candle normalized = PriceNormalizer.Normalize(raw, EurUsd, inverse: false);

        Assert.NotSame(raw, normalized);
        Assert.Equal(EurUsd, normalized.Pair);
        Assert.Equal(raw.StartUtc, normalized.StartUtc);
        Assert.Equal(raw.Interval, normalized.Interval);
        Assert.Equal(raw.Open, normalized.Open);
        Assert.Equal(raw.High, normalized.High);
        Assert.Equal(raw.Low, normalized.Low);
        Assert.Equal(raw.Close, normalized.Close);
    }

    private static Candle CreateFlatCandle(CurrencyPair pair, double price) =>
        new(pair, StartUtc, TimeSpan.FromMinutes(1), price, price, price, price);
}
