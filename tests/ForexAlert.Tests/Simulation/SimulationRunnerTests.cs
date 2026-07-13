using ForexAlert.Alerts;
using ForexAlert.Alerts.Rules;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Persistence;
using ForexAlert.Scheduling;
using ForexAlert.Simulation;
using ForexAlert.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Simulation;

public sealed class SimulationRunnerTests
{
    [Fact]
    public async Task RunAsync_BundledFixture_ExercisesPipelineAndRecordsAlertsWithoutNetwork()
    {
        IbkrOptions ibkrOptions = new() { QuoteStaleAfter = TimeSpan.FromSeconds(15) };
        AlertOptions alertOptions = new();
        PersistenceOptions persistenceOptions = new() { MaxCandlesPerInterval = 500 };
        PriceStateStore prices = new();
        PriceHistoryStore history = new(Options.Create(persistenceOptions));
        FxMarketSchedule schedule = new(Options.Create(new MarketScheduleOptions()));
        CandleAggregator aggregator = new([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]);
        MarketDataPipeline pipeline = new(prices, history, aggregator, Options.Create(ibkrOptions));
        CapturingNotificationSender sender = new();
        IAlertRule[] rules =
        [
            new DailyMovementRule(prices, history, schedule, Options.Create(alertOptions), Options.Create(ibkrOptions)),
            new HourlyMovementRule(history, schedule, Options.Create(alertOptions)),
            new WeeklyMovementRule(history, schedule, Options.Create(alertOptions)),
            new OneMinuteMovementRule(history, schedule, Options.Create(alertOptions)),
        ];
        AlertEngine engine = new(
            rules,
            sender,
            new InMemoryAlertStateStore(),
            Options.Create(alertOptions),
            NullLogger<AlertEngine>.Instance);
        ConfiguredCurrencyPairs pairs = new(Options.Create(new CurrencyPairOptions { Pairs = ["EUR/USD"] }));
        SimulationRunner runner = new(pipeline, engine, pairs, NullLogger<SimulationRunner>.Instance);

        int alertCount = await runner.RunAsync(FindBundledFixture(), CancellationToken.None);

        Assert.Equal(3, alertCount);
        AlertCandidate[] alerts = sender.Alerts.ToArray();
        Assert.Equal(3, alerts.Length);
        Assert.All(alerts, alert => Assert.Equal("EUR/USD", alert.CanonicalSymbol));
        AlertCandidate daily = Assert.Single(alerts, alert => alert.RuleName == DailyMovementRule.RuleName);
        Assert.Equal(1.06, daily.BaselinePrice, 12);
        Assert.Equal(1.08, daily.CurrentPrice, 12);
        AlertCandidate hourly = Assert.Single(alerts, alert => alert.RuleName == HourlyMovementRule.RuleName);
        Assert.Equal(1.06, hourly.BaselinePrice, 12);
        Assert.Equal(1.08, hourly.CurrentPrice, 12);
        AlertCandidate weekly = Assert.Single(alerts, alert => alert.RuleName == WeeklyMovementRule.RuleName);
        Assert.Equal(1.00, weekly.BaselinePrice, 12);
        Assert.Equal(1.06, weekly.CurrentPrice, 12);
    }

    private static string FindBundledFixture()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "ForexAlert",
                "Simulation",
                "Fixtures",
                "sample-stream.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The bundled sample-stream simulation fixture was not found.");
    }
}
