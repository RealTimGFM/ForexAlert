using ForexAlert.Alerts.Rules;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Alerts;

public sealed class OneMinuteMovementRuleTests
{
    private static readonly DateTimeOffset EvaluationTime =
        new(2026, 7, 10, 12, 1, 30, TimeSpan.Zero);

    [Fact]
    public async Task DisabledRule_DoesNotEvaluateCompletedCandle()
    {
        PriceHistoryStore history = HistoryWithCompletedMovement();
        OneMinuteMovementRule rule = CreateRule(history, enabled: false);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task EnabledRule_UsesCompletedCandleOpenAndClose()
    {
        PriceHistoryStore history = HistoryWithCompletedMovement();
        OneMinuteMovementRule rule = CreateRule(history, enabled: true);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), alert.BaselineTimestampUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 12, 1, 0, TimeSpan.Zero), alert.CurrentTimestampUtc);
        Assert.Equal(100d, alert.BaselinePrice);
        Assert.Equal(101.4d, alert.CurrentPrice);
        Assert.Equal(OneMinuteMovementRule.RuleName, alert.RuleName);
    }

    [Fact]
    public async Task EnabledRule_IgnoresIncompleteCandle()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 10, 12, 1, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            100d,
            110d));
        OneMinuteMovementRule rule = CreateRule(history, enabled: true);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task EnabledRule_DoesNotTriggerBelowThreshold()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            100d,
            101.399d));
        OneMinuteMovementRule rule = CreateRule(history, enabled: true);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    private static PriceHistoryStore HistoryWithCompletedMovement()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            100d,
            101.4d));
        return history;
    }

    private static OneMinuteMovementRule CreateRule(IPriceHistoryStore history, bool enabled) =>
        new(
            history,
            new DeterministicAlertRuleMarketSchedule(),
            Options.Create(new AlertOptions
            {
                OneMinuteEnabled = enabled,
                OneMinuteThresholdPercent = 1.4d,
            }));
}
