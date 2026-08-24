using ForexAlert.Alerts;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Notifications;
using ForexAlert.Persistence;
using ForexAlert.Scheduling;
using ForexAlert.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Alerts;

public sealed class AlertEvaluatorTests
{
    private static readonly CurrencyPair Pair = CurrencyPair.Parse("EUR/USD");
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 16, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1.0100, true)]
    [InlineData(0.9900, true)]
    [InlineData(1.0099, false)]
    [InlineData(0.9901, false)]
    public void Hourly_UsesAbsoluteChangeAndInclusiveThreshold(double close, bool expected)
    {
        TestContext context = CreateContext();
        context.State.AddHistoricalCandle(new(Pair, Now.AddHours(-1), TimeSpan.FromHours(1), 1.0, Math.Max(1.0, close), Math.Min(1.0, close), close));

        bool triggered = context.Evaluator.Evaluate(Pair, Now).Any(alert => alert.RuleName == "hourly-movement");

        Assert.Equal(expected, triggered);
    }

    [Fact]
    public void Daily_UsesFreshMidpointAndPreviousCompletedClose()
    {
        TestContext context = CreateContext();
        context.State.AddHistoricalCandle(new(Pair, Now.AddDays(-1), TimeSpan.FromDays(1), 1, 1, 1, 1));
        context.State.ProcessQuote(new(Pair, QuoteSide.Bid, 1.019, Now));
        context.State.ProcessQuote(new(Pair, QuoteSide.Ask, 1.021, Now));

        AlertCandidate alert = Assert.Single(context.Evaluator.Evaluate(Pair, Now), candidate => candidate.RuleName == "daily-movement");
        Assert.Equal(2.0, alert.PercentageChange, 8);
    }

    [Fact]
    public void Daily_SleepWindowTriggersOnlyConfiguredNegativeMove()
    {
        DateTimeOffset asleep = new(2026, 7, 8, 8, 0, 0, TimeSpan.Zero); // 04:00 New York
        TestContext context = CreateContext(options => options.SleepWindowNegativeThresholdPercent = -2);
        context.State.AddHistoricalCandle(new(Pair, asleep.AddDays(-1), TimeSpan.FromDays(1), 1, 1, 1, 1));
        context.State.ProcessQuote(new(Pair, QuoteSide.Bid, .979, asleep));
        context.State.ProcessQuote(new(Pair, QuoteSide.Ask, .981, asleep));

        Assert.Contains(context.Evaluator.Evaluate(Pair, asleep), candidate => candidate.RuleName == "daily-movement" && candidate.Direction == AlertDirection.Down);
    }

    [Fact]
    public void Daily_TradingDayOpen_UsesConfiguredMinuteCandleLimit()
    {
        TestContext context = CreateContext(
            options =>
            {
                options.DailyBaseline = DailyBaselineKind.TradingDayOpen;
                options.TradingDayOpenTime = new TimeSpan(11, 54, 0);
                options.MaxCandlesPerInterval = 5;
            },
            stateCapacity: 6);
        DateTimeOffset opening = Now.AddMinutes(-6);
        for (int index = 0; index < 6; index++)
        {
            DateTimeOffset start = opening.AddMinutes(index);
            context.State.AddHistoricalCandle(new(Pair, start, TimeSpan.FromMinutes(1), 1, 1, 1, 1));
        }
        context.State.ProcessQuote(new(Pair, QuoteSide.Bid, 1.019, Now));
        context.State.ProcessQuote(new(Pair, QuoteSide.Ask, 1.021, Now));

        Assert.Equal(6, context.State.GetCompletedCandles(Pair, TimeSpan.FromMinutes(1), Now, 10).Count);
        Assert.DoesNotContain(context.Evaluator.Evaluate(Pair, Now), candidate => candidate.RuleName == "daily-movement");
    }

    [Fact]
    public void Weekly_UsesFiveCompletedWeekdayCloses()
    {
        TestContext context = CreateContext(options => options.WeeklyThresholdPercent = 4);
        DateTimeOffset start = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
        double[] closes = [1.00, 1.01, 1.02, 1.03, 1.05];
        for (int index = 0; index < closes.Length; index++)
            context.State.AddHistoricalCandle(new(Pair, start.AddDays(index), TimeSpan.FromDays(1), closes[index], closes[index], closes[index], closes[index]));

        Assert.Contains(context.Evaluator.Evaluate(Pair, Now), candidate => candidate.RuleName == "weekly-movement");
    }

    [Fact]
    public void OneMinute_IsOptionalAndUsesCompletedCandle()
    {
        TestContext disabled = CreateContext();
        disabled.State.AddHistoricalCandle(new(Pair, Now.AddMinutes(-1), TimeSpan.FromMinutes(1), 1, 1.02, 1, 1.02));
        Assert.DoesNotContain(disabled.Evaluator.Evaluate(Pair, Now), candidate => candidate.RuleName == "one-minute-movement");

        TestContext enabled = CreateContext(options => options.OneMinuteEnabled = true);
        enabled.State.AddHistoricalCandle(new(Pair, Now.AddMinutes(-1), TimeSpan.FromMinutes(1), 1, 1.02, 1, 1.02));
        Assert.Contains(enabled.Evaluator.Evaluate(Pair, Now), candidate => candidate.RuleName == "one-minute-movement");
    }

    [Fact]
    public async Task FailedDelivery_DoesNotAdvanceCooldown()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ForexAlert-{Guid.NewGuid():N}");
        try
        {
            TestContext failing = CreateContext(sender: new ThrowingSender(), cooldownPath: Path.Combine(directory, "state.json"));
            failing.State.AddHistoricalCandle(new(Pair, Now.AddHours(-1), TimeSpan.FromHours(1), 1, 1.02, 1, 1.02));
            await Assert.ThrowsAsync<HttpRequestException>(() => failing.Evaluator.EvaluateAndSendAsync([Pair], Now, CancellationToken.None));

            CapturingNotificationSender sender = new();
            TestContext retry = CreateContext(sender: sender, cooldownPath: Path.Combine(directory, "state.json"));
            retry.State.AddHistoricalCandle(new(Pair, Now.AddHours(-1), TimeSpan.FromHours(1), 1, 1.02, 1, 1.02));
            await retry.Evaluator.EvaluateAndSendAsync([Pair], Now, CancellationToken.None);
            Assert.Single(sender.Alerts);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static TestContext CreateContext(
        Action<ForexAlertOptions>? configure = null,
        INotificationSender? sender = null,
        string? cooldownPath = null,
        int? stateCapacity = null)
    {
        ForexAlertOptions app = new()
        {
            CurrencyPairs = ["EUR/USD"],
            DailyThresholdPercent = 1,
            SleepWindowNegativeThresholdPercent = -2.4,
            HourlyThresholdPercent = 1,
            WeeklyThresholdPercent = 5,
            OneMinuteEnabled = false,
            OneMinuteThresholdPercent = 1,
            DailyBaseline = DailyBaselineKind.PreviousClose,
            Cooldown = TimeSpan.FromHours(24),
            MarketTimeZone = "America/New_York",
            FridayCloseTime = TimeSpan.FromHours(17),
            SundayOpenTime = TimeSpan.FromHours(17),
            SleepWindowStart = new TimeSpan(23, 30, 0),
            SleepWindowEnd = TimeSpan.FromHours(5),
            MaxCandlesPerInterval = 2_000,
            CooldownStatePath = cooldownPath ?? Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.json"),
        };
        configure?.Invoke(app);
        ForexAlertOptions stateOptions = stateCapacity.HasValue
            ? new ForexAlertOptions { MaxCandlesPerInterval = stateCapacity.Value }
            : app;
        MarketDataState state = new(Options.Create(stateOptions), Options.Create(new IbkrOptions { QuoteStaleAfter = TimeSpan.FromMinutes(1), MaximumBidAskSkew = TimeSpan.FromSeconds(2) }), TimeProvider.System);
        FxMarketSchedule schedule = new(Options.Create(app));
        AlertEvaluator evaluator = new(state, schedule, sender ?? new CapturingNotificationSender(), new CooldownStore(app.CooldownStatePath, AppContext.BaseDirectory), Options.Create(app), NullLogger<AlertEvaluator>.Instance);
        return new(state, evaluator);
    }

    private sealed record TestContext(MarketDataState State, AlertEvaluator Evaluator);
    private sealed class ThrowingSender : INotificationSender
    { public Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken) => throw new HttpRequestException("failed"); }
}
