using ForexAlert.Alerts.Rules;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Alerts;

public sealed class DailyMovementRuleTests
{
    private static readonly DateTimeOffset EvaluationTime =
        new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(101.4d, AlertDirection.Up, 1.4d)]
    [InlineData(98.6d, AlertDirection.Down, -1.4d)]
    public async Task PreviousClose_TriggersAtNormalThresholdInEitherDirection(
        double currentPrice,
        AlertDirection expectedDirection,
        double expectedChange)
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        Candle older = AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromDays(1),
            90d,
            90d);
        Candle previousClose = AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromDays(1),
            99d,
            100d);
        history.AddCompleted(older);
        history.AddCompleted(previousClose);
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(prices, AlertRuleTestData.EurUsd, currentPrice, EvaluationTime);
        DailyMovementRule rule = CreateRule(prices, history, new DeterministicAlertRuleMarketSchedule());

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(DailyMovementRule.RuleName, alert.RuleName);
        Assert.Equal(previousClose.EndUtc, alert.BaselineTimestampUtc);
        Assert.Equal(100d, alert.BaselinePrice);
        Assert.Equal(EvaluationTime, alert.CurrentTimestampUtc);
        Assert.Equal(currentPrice, alert.CurrentPrice);
        Assert.Equal(expectedDirection, alert.Direction);
        Assert.Equal(1.4d, alert.TriggerThresholdPercent);
        Assert.InRange(Math.Abs(alert.PercentageChange - expectedChange), 0d, 0.0000000001d);
    }

    [Theory]
    [InlineData(101.399d)]
    [InlineData(98.601d)]
    public async Task PreviousClose_DoesNotTriggerInsideNormalThreshold(double currentPrice)
    {
        PriceHistoryStore history = HistoryWithPreviousClose(100d);
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(prices, AlertRuleTestData.EurUsd, currentPrice, EvaluationTime);
        DailyMovementRule rule = CreateRule(prices, history, new DeterministicAlertRuleMarketSchedule());

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SleepWindow_TriggersOnlyNegativeMovementAtConfiguredThreshold()
    {
        PriceHistoryStore history = HistoryWithPreviousClose(100d);
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(prices, AlertRuleTestData.EurUsd, 97.6d, EvaluationTime);
        DailyMovementRule rule = CreateRule(
            prices,
            history,
            new DeterministicAlertRuleMarketSchedule(isSleepWindow: true));

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(AlertDirection.Down, alert.Direction);
        Assert.Equal(-2.4d, alert.TriggerThresholdPercent);
        Assert.InRange(Math.Abs(alert.PercentageChange - (-2.4d)), 0d, 0.0000000001d);
    }

    [Fact]
    public async Task SleepWindow_DoesNotTriggerForPositiveMovementOfGreaterMagnitude()
    {
        PriceHistoryStore history = HistoryWithPreviousClose(100d);
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(prices, AlertRuleTestData.EurUsd, 103d, EvaluationTime);
        DailyMovementRule rule = CreateRule(
            prices,
            history,
            new DeterministicAlertRuleMarketSchedule(isSleepWindow: true));

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task StaleMidPrice_DoesNotTrigger()
    {
        PriceHistoryStore history = HistoryWithPreviousClose(100d);
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(
            prices,
            AlertRuleTestData.EurUsd,
            105d,
            EvaluationTime.Subtract(TimeSpan.FromSeconds(16)));
        DailyMovementRule rule = CreateRule(prices, history, new DeterministicAlertRuleMarketSchedule());

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task MissingPreviousClose_DoesNotTrigger()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(prices, AlertRuleTestData.EurUsd, 105d, EvaluationTime);
        DailyMovementRule rule = CreateRule(prices, history, new DeterministicAlertRuleMarketSchedule());

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IncompleteCurrentDailyCandle_DoesNotReplacePreviousCompletedClose()
    {
        PriceHistoryStore history = HistoryWithPreviousClose(100d);
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromDays(1),
            100d,
            50d));
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(prices, AlertRuleTestData.EurUsd, 101.4d, EvaluationTime);
        DailyMovementRule rule = CreateRule(prices, history, new DeterministicAlertRuleMarketSchedule());

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero), alert.BaselineTimestampUtc);
        Assert.Equal(100d, alert.BaselinePrice);
    }

    [Fact]
    public async Task TradingDayOpen_UsesEarliestCompletedCandleFromCurrentMarketDate()
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 8, 23, 58, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            80d,
            80d));
        Candle marketOpen = AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 9, 9, 30, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            100d,
            100.1d);
        history.AddCompleted(marketOpen);
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            105d,
            105d));
        PriceStateStore prices = new();
        AlertRuleTestData.SetMidPrice(prices, AlertRuleTestData.EurUsd, 101.4d, EvaluationTime);
        DailyMovementRule rule = CreateRule(
            prices,
            history,
            new DeterministicAlertRuleMarketSchedule(tradingDayOpenUtc: marketOpen.StartUtc),
            DailyBaselineKind.TradingDayOpen);

        AlertCandidate? result = await rule.EvaluateAsync(
            AlertRuleTestData.EurUsd,
            EvaluationTime,
            CancellationToken.None);

        AlertCandidate alert = Assert.IsType<AlertCandidate>(result);
        Assert.Equal(marketOpen.StartUtc, alert.BaselineTimestampUtc);
        Assert.Equal(marketOpen.Open, alert.BaselinePrice);
        Assert.InRange(Math.Abs(alert.PercentageChange - 1.4d), 0d, 0.0000000001d);
    }

    private static PriceHistoryStore HistoryWithPreviousClose(double close)
    {
        PriceHistoryStore history = AlertRuleTestData.CreateHistoryStore();
        history.AddCompleted(AlertRuleTestData.Candle(
            new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromDays(1),
            close,
            close));
        return history;
    }

    private static DailyMovementRule CreateRule(
        PriceStateStore prices,
        PriceHistoryStore history,
        DeterministicAlertRuleMarketSchedule schedule,
        DailyBaselineKind baseline = DailyBaselineKind.PreviousClose) =>
        new(
            prices,
            history,
            schedule,
            Options.Create(new AlertOptions
            {
                DailyBaseline = baseline,
                DailyNormalThresholdPercent = 1.4d,
                SleepWindowNegativeThresholdPercent = -2.4d,
            }),
            Options.Create(new IbkrOptions { QuoteStaleAfter = TimeSpan.FromSeconds(15) }));
}
