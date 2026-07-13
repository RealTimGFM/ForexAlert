using ForexAlert.Alerts.Rules;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Alerts;

public sealed class WeeklyMovementRuleTests
{
    private static readonly TimeSpan Daily = TimeSpan.FromDays(1);

    [Fact]
    public async Task UsesFiveDistinctTradingDaysAcrossWeekend()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        AddDaily(history, 2026, 7, 2, 100d);
        AddDaily(history, 2026, 7, 3, 101d);
        AddDaily(history, 2026, 7, 4, 50d);
        AddDaily(history, 2026, 7, 5, 200d);
        AddDaily(history, 2026, 7, 6, 102d);
        AddDaily(history, 2026, 7, 7, 103d);
        AddDaily(history, 2026, 7, 8, 105d);
        WeeklyMovementRule rule = CreateRule(history);
        DateTimeOffset evaluationTime = new(2026, 7, 9, 0, 0, 0, TimeSpan.Zero);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            evaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero), alert.BaselineTimestampUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero), alert.CurrentTimestampUtc);
        Assert.Equal(100d, alert.BaselinePrice);
        Assert.Equal(105d, alert.CurrentPrice);
        Assert.Equal(5d, alert.PercentageChange);
    }

    [Fact]
    public async Task DuplicateObservationsFromFourTradingDays_DoNotCountAsFiveDays()
    {
        Candle monday = DailyCandle(2026, 6, 1, 100d);
        Candle tuesday = DailyCandle(2026, 6, 2, 101d);
        Candle wednesday = DailyCandle(2026, 6, 3, 102d);
        Candle thursday = DailyCandle(2026, 6, 4, 103d);
        Candle duplicateThursday = DailyCandle(2026, 6, 4, 110d);
        ScriptedCompletedCandleHistory history = new(
            [monday, tuesday, wednesday, thursday, duplicateThursday]);
        WeeklyMovementRule rule = CreateRule(history);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IncompleteNewestDailyCandle_IsExcluded()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        AddDaily(history, 2026, 7, 6, 100d);
        AddDaily(history, 2026, 7, 7, 101d);
        AddDaily(history, 2026, 7, 8, 102d);
        AddDaily(history, 2026, 7, 9, 103d);
        AddDaily(history, 2026, 7, 10, 105d);
        AddDaily(history, 2026, 7, 13, 150d);
        WeeklyMovementRule rule = CreateRule(history);
        DateTimeOffset evaluationTime = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            evaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero), alert.CurrentTimestampUtc);
        Assert.Equal(105d, alert.CurrentPrice);
        Assert.Equal(5d, alert.PercentageChange);
    }

    [Fact]
    public async Task FewerThanFiveDistinctTradingDays_DoesNotTrigger()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        AddDaily(history, 2026, 7, 6, 100d);
        AddDaily(history, 2026, 7, 7, 103d);
        AddDaily(history, 2026, 7, 8, 106d);
        AddDaily(history, 2026, 7, 9, 110d);
        WeeklyMovementRule rule = CreateRule(history);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Null(result);
    }

    private static WeeklyMovementRule CreateRule(IPriceHistoryStore history) =>
        new(
            history,
            new DeterministicAlertRuleMarketSchedule(),
            Options.Create(new AlertOptions { WeeklyThresholdPercent = 5d }));

    private static void AddDaily(PriceHistoryStore history, int year, int month, int day, double close) =>
        history.AddCompleted(DailyCandle(year, month, day, close));

    private static Candle DailyCandle(int year, int month, int day, double close) =>
        AlertRuleTestData.Candle(
            new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero),
            Daily,
            close,
            close);
}
