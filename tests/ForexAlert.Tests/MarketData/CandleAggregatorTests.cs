using ForexAlert.Domain;
using ForexAlert.MarketData;

namespace ForexAlert.Tests.MarketData;

public sealed class CandleAggregatorTests
{
    private static readonly CurrencyPair EurUsd = CurrencyPair.Parse("EUR/USD");
    private static readonly CurrencyPair GbpUsd = CurrencyPair.Parse("GBP/USD");
    private static readonly DateTimeOffset StartUtc = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    [Fact]
    public void DiscardIncomplete_MidBucket_IgnoresRemainderUntilNextFullBucket()
    {
        CandleAggregator aggregator = new([OneMinute]);

        Assert.True(aggregator.Add(
            new PricePoint(EurUsd, 1.10d, StartUtc.AddSeconds(10))));

        aggregator.DiscardIncomplete(StartUtc.AddSeconds(30));

        // A quote after reconnect but inside the interrupted minute is ignored.
        Assert.False(aggregator.Add(
            new PricePoint(EurUsd, 1.20d, StartUtc.AddSeconds(40))));

        Assert.Empty(
            aggregator.CompleteThrough(StartUtc.AddMinutes(1)));

        // The next full interval is accepted normally.
        Assert.True(aggregator.Add(
            new PricePoint(EurUsd, 1.30d, StartUtc.AddMinutes(1))));

        Candle completed = Assert.Single(
            aggregator.CompleteThrough(StartUtc.AddMinutes(2)));

        Assert.Equal(StartUtc.AddMinutes(1), completed.StartUtc);
        Assert.Equal(1.30d, completed.Open);
        Assert.Equal(1.30d, completed.Close);
    }
    [Fact]
    public void CompleteThrough_BuildsOhlcFromAllTicksInBucket()
    {
        CandleAggregator aggregator = new([OneMinute]);

        Assert.True(Add(aggregator, EurUsd, 10, 1.10d));
        Assert.True(Add(aggregator, EurUsd, 20, 1.40d));
        Assert.True(Add(aggregator, EurUsd, 30, 1.00d));
        Assert.True(Add(aggregator, EurUsd, 50, 1.30d));

        Candle candle = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));
        Assert.Equal(EurUsd, candle.Pair);
        Assert.Equal(StartUtc, candle.StartUtc);
        Assert.Equal(OneMinute, candle.Interval);
        Assert.Equal(1.10d, candle.Open);
        Assert.Equal(1.40d, candle.High);
        Assert.Equal(1.00d, candle.Low);
        Assert.Equal(1.30d, candle.Close);
        Assert.Equal(StartUtc.AddMinutes(1), candle.EndUtc);
    }

    [Fact]
    public void ExactBoundary_StartsNextBucketAndEachBucketCompletesAtItsEnd()
    {
        CandleAggregator aggregator = new([OneMinute]);
        DateTimeOffset lastTickInFirstBucket = StartUtc.AddMinutes(1).AddTicks(-1);

        Assert.True(aggregator.Add(new PricePoint(EurUsd, 1.10d, lastTickInFirstBucket)));
        Assert.True(aggregator.Add(new PricePoint(EurUsd, 1.20d, StartUtc.AddMinutes(1))));

        IReadOnlyList<Candle> firstCompletion = aggregator.CompleteThrough(StartUtc.AddMinutes(1));
        Candle first = Assert.Single(firstCompletion);
        Assert.Equal(StartUtc, first.StartUtc);
        Assert.Equal(1.10d, first.Close);

        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddMinutes(2).AddTicks(-1)));

        Candle second = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(2)));
        Assert.Equal(StartUtc.AddMinutes(1), second.StartUtc);
        Assert.Equal(1.20d, second.Open);
    }

    [Fact]
    public void CompleteThrough_ExcludesIncompleteCandleWithoutDiscardingIt()
    {
        CandleAggregator aggregator = new([OneMinute]);
        Assert.True(Add(aggregator, EurUsd, 10, 1.10d));

        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddMinutes(1).AddTicks(-1)));

        Candle completed = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));
        Assert.Equal(StartUtc, completed.StartUtc);
        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));
    }

    [Fact]
    public void CompleteThrough_SparseBucketsEmitOnlyBucketsContainingTicks()
    {
        CandleAggregator aggregator = new([OneMinute]);
        Assert.True(Add(aggregator, EurUsd, 5, 1.10d));
        Assert.True(aggregator.Add(new PricePoint(EurUsd, 1.30d, StartUtc.AddMinutes(3).AddSeconds(5))));

        IReadOnlyList<Candle> completed = aggregator.CompleteThrough(StartUtc.AddMinutes(5));

        Assert.Equal(2, completed.Count);
        Assert.Equal(
            new[] { StartUtc, StartUtc.AddMinutes(3) },
            completed.Select(static candle => candle.StartUtc).ToArray());
    }

    [Fact]
    public void DuplicateAndOutOfOrderTicks_ProduceTimestampOrderedOpenAndClose()
    {
        CandleAggregator aggregator = new([OneMinute]);
        PricePoint closingTick = new(EurUsd, 1.20d, StartUtc.AddSeconds(50));

        Assert.True(aggregator.Add(closingTick));
        Assert.True(aggregator.Add(closingTick));
        Assert.True(Add(aggregator, EurUsd, 10, 1.10d));
        Assert.True(Add(aggregator, EurUsd, 30, 1.50d));
        Assert.True(Add(aggregator, EurUsd, 20, 1.00d));

        Candle candle = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));
        Assert.Equal(1.10d, candle.Open);
        Assert.Equal(1.50d, candle.High);
        Assert.Equal(1.00d, candle.Low);
        Assert.Equal(1.20d, candle.Close);
    }

    [Fact]
    public void Add_AcceptsOutOfOrderBucketsUntilTheyAreCompleted()
    {
        CandleAggregator aggregator = new([OneMinute]);

        Assert.True(aggregator.Add(new PricePoint(EurUsd, 1.30d, StartUtc.AddMinutes(2).AddSeconds(5))));
        Assert.True(Add(aggregator, EurUsd, 5, 1.10d));

        Candle first = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));
        Assert.Equal(StartUtc, first.StartUtc);

        Candle later = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(3)));
        Assert.Equal(StartUtc.AddMinutes(2), later.StartUtc);
    }

    [Fact]
    public void Add_RejectsTicksForEmittedBucketButAcceptsNewerBucket()
    {
        CandleAggregator aggregator = new([OneMinute]);
        Assert.True(Add(aggregator, EurUsd, 10, 1.10d));
        Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));

        Assert.False(Add(aggregator, EurUsd, 20, 1.20d));
        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));

        Assert.True(aggregator.Add(new PricePoint(EurUsd, 1.30d, StartUtc.AddMinutes(1).AddSeconds(10))));
        Candle newer = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(2)));
        Assert.Equal(StartUtc.AddMinutes(1), newer.StartUtc);
    }

    [Fact]
    public void SeriesForDifferentSymbols_AreIsolated()
    {
        CandleAggregator aggregator = new([OneMinute]);
        Assert.True(Add(aggregator, EurUsd, 10, 1.10d));
        Assert.True(Add(aggregator, GbpUsd, 10, 1.30d));
        Assert.True(Add(aggregator, EurUsd, 20, 1.20d));
        Assert.True(Add(aggregator, GbpUsd, 20, 1.40d));

        IReadOnlyList<Candle> completed = aggregator.CompleteThrough(StartUtc.AddMinutes(1));

        Assert.Equal(2, completed.Count);
        Assert.Equal(new[] { EurUsd, GbpUsd }, completed.Select(static candle => candle.Pair).ToArray());
        Assert.Equal(1.10d, completed[0].Open);
        Assert.Equal(1.20d, completed[0].Close);
        Assert.Equal(1.30d, completed[1].Open);
        Assert.Equal(1.40d, completed[1].Close);
    }

    [Fact]
    public void MultipleIntervals_AreDistinctOrderedAndCompleteIndependently()
    {
        TimeSpan fiveMinutes = TimeSpan.FromMinutes(5);
        CandleAggregator aggregator = new([fiveMinutes, OneMinute, fiveMinutes]);

        Assert.Equal(new[] { OneMinute, fiveMinutes }, aggregator.Intervals.ToArray());
        Assert.True(Add(aggregator, EurUsd, 10, 1.10d));

        Candle oneMinute = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));
        Assert.Equal(OneMinute, oneMinute.Interval);

        Candle fiveMinute = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(5)));
        Assert.Equal(fiveMinutes, fiveMinute.Interval);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Add_RejectsInvalidPrice(double invalidPrice)
    {
        CandleAggregator aggregator = new([OneMinute]);

        Assert.False(aggregator.Add(new PricePoint(EurUsd, invalidPrice, StartUtc)));
        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));
    }

    [Fact]
    public void Add_ReturnsFalseWhenNoIntervalsAreConfigured()
    {
        CandleAggregator aggregator = new([]);

        Assert.False(aggregator.Add(new PricePoint(EurUsd, 1.10d, StartUtc)));
        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddDays(1)));
    }

    [Fact]
    public void StartupPartialBucket_IsIgnoredUntilTheNextFullBucket()
    {
        CandleAggregator aggregator = new([OneMinute], StartUtc.AddSeconds(30));

        Assert.False(Add(aggregator, EurUsd, 45, 1.10d));
        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));

        Assert.True(aggregator.Add(new PricePoint(EurUsd, 1.20d, StartUtc.AddMinutes(1))));
        Candle completed = Assert.Single(aggregator.CompleteThrough(StartUtc.AddMinutes(2)));
        Assert.Equal(StartUtc.AddMinutes(1), completed.StartUtc);
    }

    [Fact]
    public void DiscardIncomplete_DisconnectAndReconnectMidBucket_DoesNotSpanMinuteOrHour()
    {
        TimeSpan oneHour = TimeSpan.FromHours(1);
        CandleAggregator aggregator = new([OneMinute, oneHour]);

        Assert.True(Add(aggregator, EurUsd, 10, 1.10d));

        aggregator.DiscardIncomplete(StartUtc.AddSeconds(30));

        // Both the current minute and hour were interrupted.
        Assert.False(Add(aggregator, EurUsd, 40, 1.20d));
        Assert.Empty(aggregator.CompleteThrough(StartUtc.AddMinutes(1)));

        // The next complete minute can start, but the interrupted hour
        // must remain blocked until the next hour boundary.
        Assert.True(aggregator.Add(
            new PricePoint(
                EurUsd,
                1.25d,
                StartUtc.AddMinutes(1))));

        IReadOnlyList<Candle> minuteCompletion =
            aggregator.CompleteThrough(StartUtc.AddMinutes(2));

        Candle minute = Assert.Single(
            minuteCompletion,
            candle => candle.Interval == OneMinute);

        Assert.Equal(StartUtc.AddMinutes(1), minute.StartUtc);
        Assert.Equal(1.25d, minute.Open);
        Assert.Equal(1.25d, minute.Close);
        Assert.DoesNotContain(
            minuteCompletion,
            candle => candle.Interval == oneHour);

        // The next full hour begins normally.
        Assert.True(aggregator.Add(
            new PricePoint(
                EurUsd,
                1.30d,
                StartUtc.AddHours(1))));

        IReadOnlyList<Candle> hourCompletion =
            aggregator.CompleteThrough(StartUtc.AddHours(2));

        Candle hour = Assert.Single(
            hourCompletion,
            candle => candle.Interval == oneHour);

        Assert.Equal(StartUtc.AddHours(1), hour.StartUtc);
        Assert.Equal(1.30d, hour.Open);
        Assert.Equal(1.30d, hour.Close);
    }
    private static bool Add(
        CandleAggregator aggregator,
        CurrencyPair pair,
        int secondsAfterStart,
        double price) =>
        aggregator.Add(new PricePoint(pair, price, StartUtc.AddSeconds(secondsAfterStart)));
}
