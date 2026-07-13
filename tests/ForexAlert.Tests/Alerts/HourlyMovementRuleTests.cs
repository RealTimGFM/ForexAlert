using ForexAlert.Alerts.Rules;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Alerts;

public sealed class HourlyMovementRuleTests
{
    private static readonly DateTimeOffset EvaluationTime =
        new(2026, 7, 10, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UsesCompletedCandlesCorrectOpeningAndClosingPrices()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        Candle completed = AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(1),
            100d,
            101.4d,
            high: 110d,
            low: 90d);
        Candle incomplete = AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(1),
            100d,
            120d);
        history.AddCompleted(completed);
        history.AddCompleted(incomplete);
        HourlyMovementRule rule = CreateRule(history);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(completed.StartUtc, alert.BaselineTimestampUtc);
        Assert.Equal(completed.EndUtc, alert.CurrentTimestampUtc);
        Assert.Equal(completed.Open, alert.BaselinePrice);
        Assert.Equal(completed.Close, alert.CurrentPrice);
        Assert.InRange(Math.Abs(alert.PercentageChange - 1.4d), 0d, 0.0000000001d);
    }

    [Fact]
    public async Task IgnoresHighLowExcursionWhenOpenToCloseIsBelowThreshold()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(1),
            100d,
            100.5d,
            high: 110d,
            low: 90d));
        HourlyMovementRule rule = CreateRule(history);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IncompleteCandleAlone_DoesNotTrigger()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(1),
            100d,
            120d));
        HourlyMovementRule rule = CreateRule(history);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    private static HourlyMovementRule CreateRule(IPriceHistoryStore history) =>
        new(
            history,
            new DeterministicAlertRuleMarketSchedule(),
            Options.Create(new AlertOptions { HourlyThresholdPercent = 1.4d }));
}
