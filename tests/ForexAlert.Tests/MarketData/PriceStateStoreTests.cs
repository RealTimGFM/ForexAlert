using ForexAlert.Domain;
using ForexAlert.MarketData;

namespace ForexAlert.Tests.MarketData;

public sealed class PriceStateStoreTests
{
    private static readonly CurrencyPair Pair = CurrencyPair.Parse("EUR/USD");
    private static readonly DateTimeOffset StartUtc = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryGetMidPrice_ReturnsMidpointAndNewestSideTimestamp()
    {
        PriceStateStore store = new();
        DateTimeOffset bidTimestamp = StartUtc;
        DateTimeOffset askTimestamp = StartUtc.AddSeconds(2);

        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, 1.10d, bidTimestamp)));
        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Ask, 1.20d, askTimestamp)));

        bool found = store.TryGetMidPrice(
            Pair,
            StartUtc.AddSeconds(5),
            TimeSpan.FromSeconds(10),
            out PricePoint? result);

        Assert.True(found);
        PricePoint pricePoint = Assert.IsType<PricePoint>(result);
        Assert.Equal(Pair, pricePoint.Pair);
        Assert.Equal(1.15d, pricePoint.Price, precision: 12);
        Assert.Equal(askTimestamp, pricePoint.TimestampUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(QuoteSide.Bid)]
    [InlineData(QuoteSide.Ask)]
    public void TryGetMidPrice_RejectsMissingPairOrSide(QuoteSide? availableSide)
    {
        PriceStateStore store = new();
        if (availableSide is QuoteSide side)
        {
            Assert.True(store.Update(new QuoteUpdate(Pair, side, 1.10d, StartUtc)));
        }

        bool found = store.TryGetMidPrice(
            Pair,
            StartUtc,
            TimeSpan.FromSeconds(1),
            out PricePoint? result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void TryGetMidPrice_AcceptsSideAtMaximumAgeAndRejectsItOneTickLater()
    {
        PriceStateStore store = CreateCompleteStore(
            bid: 1.10d,
            bidTimestamp: StartUtc,
            ask: 1.20d,
            askTimestamp: StartUtc.AddSeconds(5));
        TimeSpan maximumAge = TimeSpan.FromSeconds(10);

        Assert.True(store.TryGetMidPrice(
            Pair,
            StartUtc.Add(maximumAge),
            maximumAge,
            out PricePoint? atBoundary));
        Assert.NotNull(atBoundary);

        Assert.False(store.TryGetMidPrice(
            Pair,
            StartUtc.Add(maximumAge).AddTicks(1),
            maximumAge,
            out PricePoint? afterBoundary));
        Assert.Null(afterBoundary);
    }

    [Fact]
    public void TryGetMidPrice_RejectsSideTimestampedAfterAsOfTime()
    {
        PriceStateStore store = CreateCompleteStore(
            bid: 1.10d,
            bidTimestamp: StartUtc,
            ask: 1.20d,
            askTimestamp: StartUtc.AddSeconds(1));

        Assert.False(store.TryGetMidPrice(
            Pair,
            StartUtc,
            TimeSpan.FromMinutes(1),
            out PricePoint? result));
        Assert.Null(result);
    }

    [Fact]
    public void TryGetMidPrice_EnforcesMaximumBidAskTimestampSkewInclusively()
    {
        PriceStateStore store = CreateCompleteStore(
            bid: 1.10d,
            bidTimestamp: StartUtc,
            ask: 1.20d,
            askTimestamp: StartUtc.AddSeconds(2));

        Assert.True(store.TryGetMidPrice(
            Pair,
            StartUtc.AddSeconds(3),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(2),
            out PricePoint? atBoundary));
        Assert.NotNull(atBoundary);

        Assert.False(store.TryGetMidPrice(
            Pair,
            StartUtc.AddSeconds(3),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(2).Subtract(TimeSpan.FromTicks(1)),
            out PricePoint? beyondBoundary));
        Assert.Null(beyondBoundary);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryGetMidPrice_RejectsNonPositiveMaximumAge(int ageInSeconds)
    {
        PriceStateStore store = CreateCompleteStore(1.10d, StartUtc, 1.20d, StartUtc);

        Assert.False(store.TryGetMidPrice(
            Pair,
            StartUtc,
            TimeSpan.FromSeconds(ageInSeconds),
            out PricePoint? result));
        Assert.Null(result);
    }

    [Fact]
    public void Update_RejectsOutOfOrderSideAndPreservesNewerValue()
    {
        PriceStateStore store = new();
        DateTimeOffset newerTimestamp = StartUtc.AddSeconds(2);

        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, 1.10d, newerTimestamp)));
        Assert.False(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, 0.80d, StartUtc)));
        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Ask, 1.20d, newerTimestamp)));

        Assert.True(store.TryGetMidPrice(
            Pair,
            newerTimestamp,
            TimeSpan.FromSeconds(1),
            out PricePoint? result));
        PricePoint pricePoint = Assert.IsType<PricePoint>(result);
        Assert.Equal(1.15d, pricePoint.Price, precision: 12);
    }

    [Fact]
    public void Update_AllowsReplacementAtSameTimestamp()
    {
        PriceStateStore store = new();

        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, 1.10d, StartUtc)));
        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, 1.14d, StartUtc)));
        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Ask, 1.16d, StartUtc)));

        Assert.True(store.TryGetMidPrice(
            Pair,
            StartUtc,
            TimeSpan.FromSeconds(1),
            out PricePoint? result));
        PricePoint pricePoint = Assert.IsType<PricePoint>(result);
        Assert.Equal(1.15d, pricePoint.Price, precision: 12);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Update_RejectsInvalidPriceWithoutCreatingQuote(double invalidPrice)
    {
        PriceStateStore store = new();

        Assert.False(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, invalidPrice, StartUtc)));
        Assert.Empty(store.GetSnapshot());
        Assert.False(store.TryGetMidPrice(
            Pair,
            StartUtc,
            TimeSpan.FromSeconds(1),
            out PricePoint? result));
        Assert.Null(result);
    }

    [Fact]
    public void TryGetMidPrice_RejectsCrossedSidesButAllowsEqualSides()
    {
        PriceStateStore crossedStore = CreateCompleteStore(1.20d, StartUtc, 1.10d, StartUtc);

        Assert.False(crossedStore.TryGetMidPrice(
            Pair,
            StartUtc,
            TimeSpan.FromSeconds(1),
            out PricePoint? crossedResult));
        Assert.Null(crossedResult);

        PriceStateStore equalStore = CreateCompleteStore(1.15d, StartUtc, 1.15d, StartUtc);
        Assert.True(equalStore.TryGetMidPrice(
            Pair,
            StartUtc,
            TimeSpan.FromSeconds(1),
            out PricePoint? equalResult));
        PricePoint pricePoint = Assert.IsType<PricePoint>(equalResult);
        Assert.Equal(1.15d, pricePoint.Price);
    }

    [Fact]
    public void Updates_NormalizeNonUtcTimestampsAndSnapshotOnlyCompleteQuotes()
    {
        PriceStateStore store = new();
        CurrencyPair incompletePair = CurrencyPair.Parse("GBP/USD");
        DateTimeOffset localTimestamp = new(2026, 1, 2, 7, 0, 0, TimeSpan.FromHours(-5));

        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, 1.10d, localTimestamp)));
        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Ask, 1.20d, localTimestamp)));
        Assert.True(store.Update(new QuoteUpdate(incompletePair, QuoteSide.Bid, 1.30d, localTimestamp)));

        QuoteSnapshot snapshot = Assert.Single(store.GetSnapshot());
        Assert.Equal(Pair, snapshot.Pair);
        Assert.Equal(StartUtc, snapshot.BidTimestampUtc);
        Assert.Equal(StartUtc, snapshot.AskTimestampUtc);
        Assert.Equal(1.15d, snapshot.MidPrice, precision: 12);
        Assert.Equal(StartUtc, snapshot.TimestampUtc);
    }

    private static PriceStateStore CreateCompleteStore(
        double bid,
        DateTimeOffset bidTimestamp,
        double ask,
        DateTimeOffset askTimestamp)
    {
        PriceStateStore store = new();
        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Bid, bid, bidTimestamp)));
        Assert.True(store.Update(new QuoteUpdate(Pair, QuoteSide.Ask, ask, askTimestamp)));
        return store;
    }
}
