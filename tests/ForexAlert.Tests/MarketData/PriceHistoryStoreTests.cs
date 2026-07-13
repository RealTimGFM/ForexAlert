using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.MarketData;

public sealed class PriceHistoryStoreTests
{
    private static readonly CurrencyPair EurUsd = CurrencyPair.Parse("EUR/USD");
    private static readonly CurrencyPair GbpUsd = CurrencyPair.Parse("GBP/USD");
    private static readonly DateTimeOffset StartUtc = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    [Fact]
    public void AddCompleted_BoundsEachSeriesToConfiguredCapacity()
    {
        PriceHistoryStore store = CreateStore(capacity: 5);
        for (int minute = 0; minute < 7; minute++)
        {
            store.AddCompleted(CreateCandle(EurUsd, StartUtc.AddMinutes(minute), OneMinute, 1d + minute));
        }

        IReadOnlyList<Candle> candles = store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc.AddMinutes(10),
            maximumCount: 10);

        Assert.Equal(5, candles.Count);
        Assert.Equal(
            Enumerable.Range(2, 5).Select(offset => StartUtc.AddMinutes(offset)).ToArray(),
            candles.Select(static candle => candle.StartUtc).ToArray());
    }

    [Fact]
    public void GetCompletedCandles_IncludesOnlyCandlesEndedByAsOfTime()
    {
        PriceHistoryStore store = CreateStore(capacity: 10);
        store.AddCompleted(CreateCandle(EurUsd, StartUtc, OneMinute, 1.10d));
        store.AddCompleted(CreateCandle(EurUsd, StartUtc.AddMinutes(1), OneMinute, 1.20d));
        store.AddCompleted(CreateCandle(EurUsd, StartUtc.AddMinutes(2), OneMinute, 1.30d));

        Assert.Empty(store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc.AddMinutes(1).AddTicks(-1),
            maximumCount: 10));

        IReadOnlyList<Candle> atExactBoundary = store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc.AddMinutes(2),
            maximumCount: 10);

        Assert.Equal(2, atExactBoundary.Count);
        Assert.Equal(
            new[] { StartUtc, StartUtc.AddMinutes(1) },
            atExactBoundary.Select(static candle => candle.StartUtc).ToArray());
    }

    [Fact]
    public void GetCompletedCandles_ReturnsMostRecentRequestedCountInChronologicalOrder()
    {
        PriceHistoryStore store = CreateStore(capacity: 10);
        for (int minute = 4; minute >= 0; minute--)
        {
            store.AddCompleted(CreateCandle(EurUsd, StartUtc.AddMinutes(minute), OneMinute, 1d + minute));
        }

        IReadOnlyList<Candle> candles = store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc.AddMinutes(10),
            maximumCount: 2);

        Assert.Equal(
            new[] { StartUtc.AddMinutes(3), StartUtc.AddMinutes(4) },
            candles.Select(static candle => candle.StartUtc).ToArray());
    }

    [Fact]
    public void AddCompleted_ReplacesCandleWithSameSeriesAndStartTime()
    {
        PriceHistoryStore store = CreateStore(capacity: 5);
        store.AddCompleted(CreateCandle(EurUsd, StartUtc, OneMinute, 1.10d));
        store.AddCompleted(CreateCandle(EurUsd, StartUtc, OneMinute, 1.20d));

        Candle candle = Assert.Single(store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc.AddMinutes(1),
            maximumCount: 5));
        Assert.Equal(1.20d, candle.Close);
    }

    [Fact]
    public void History_IsIsolatedByPairAndInterval()
    {
        TimeSpan fiveMinutes = TimeSpan.FromMinutes(5);
        PriceHistoryStore store = CreateStore(capacity: 5);
        store.AddCompleted(CreateCandle(EurUsd, StartUtc, OneMinute, 1.10d));
        store.AddCompleted(CreateCandle(GbpUsd, StartUtc, OneMinute, 1.30d));
        store.AddCompleted(CreateCandle(EurUsd, StartUtc, fiveMinutes, 1.50d));

        Assert.Equal(1.10d, Assert.Single(store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc.AddMinutes(10),
            maximumCount: 5)).Close);
        Assert.Equal(1.30d, Assert.Single(store.GetCompletedCandles(
            GbpUsd,
            OneMinute,
            StartUtc.AddMinutes(10),
            maximumCount: 5)).Close);
        Assert.Equal(1.50d, Assert.Single(store.GetCompletedCandles(
            EurUsd,
            fiveMinutes,
            StartUtc.AddMinutes(10),
            maximumCount: 5)).Close);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetCompletedCandles_ReturnsEmptyForNonPositiveMaximumCount(int maximumCount)
    {
        PriceHistoryStore store = CreateStore(capacity: 5);
        store.AddCompleted(CreateCandle(EurUsd, StartUtc, OneMinute, 1.10d));

        Assert.Empty(store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc.AddMinutes(1),
            maximumCount));
    }

    [Fact]
    public void GetCompletedCandles_ReturnsEmptyForMissingSeries()
    {
        PriceHistoryStore store = CreateStore(capacity: 5);

        Assert.Empty(store.GetCompletedCandles(
            EurUsd,
            OneMinute,
            StartUtc,
            maximumCount: 5));
    }

    private static PriceHistoryStore CreateStore(int capacity) =>
        new(Options.Create(new PersistenceOptions { MaxCandlesPerInterval = capacity }));

    private static Candle CreateCandle(
        CurrencyPair pair,
        DateTimeOffset startUtc,
        TimeSpan interval,
        double price) =>
        new(pair, startUtc, interval, price, price, price, price);
}
