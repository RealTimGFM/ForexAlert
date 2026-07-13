using ForexAlert.Alerts;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Persistence;
using ForexAlert.Scheduling;
using ForexAlert.Tests.TestDoubles;
using ForexAlert.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Infrastructure;

public sealed class MarketMonitoringServiceTests
{
    [Fact]
    public async Task Cancellation_AfterSubscription_DisconnectsAndCompletesCleanly()
    {
        ControlledMarketDataClientProbe marketData = new();
        IbkrOptions ibkrOptions = new();
        AlertOptions alertOptions = new() { EvaluationInterval = TimeSpan.FromHours(1) };
        PersistenceOptions persistenceOptions = new();
        PriceStateStore prices = new();
        PriceHistoryStore history = new(Options.Create(persistenceOptions));
        MarketDataPipeline pipeline = new(
            prices,
            history,
            new CandleAggregator([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]),
            Options.Create(ibkrOptions));
        FxMarketSchedule schedule = new(Options.Create(new MarketScheduleOptions()));
        CapturingNotificationSender sender = new();
        AlertEngine engine = new(
            [],
            sender,
            new InMemoryAlertStateStore(),
            Options.Create(alertOptions),
            NullLogger<AlertEngine>.Instance);
        ConfiguredCurrencyPairs pairs = new(Options.Create(new CurrencyPairOptions { Pairs = ["EUR/USD"] }));
        using HostApplicationLifetimeProbe lifetime = new();
        using MarketMonitoringService service = new(
            marketData,
            pipeline,
            engine,
            schedule,
            pairs,
            Options.Create(ibkrOptions),
            Options.Create(alertOptions),
            new CommandLineOptions(false, false, null, false, false),
            new ApplicationRunResult(),
            TimeProvider.System,
            lifetime,
            NullLogger<MarketMonitoringService>.Instance);
        using CancellationTokenSource stopping = new();

        await service.StartAsync(stopping.Token);
        await marketData.SubscriptionObserved.WaitAsync(TimeSpan.FromSeconds(2));
        stopping.Cancel();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, marketData.ConnectCount);
        Assert.Equal(1, marketData.SubscribeCount);
        Assert.Equal(1, marketData.DisconnectCount);
        Assert.False(marketData.IsConnected);
        Assert.Empty(sender.Alerts);
        Assert.Equal(0, lifetime.StopApplicationCount);
    }

    [Fact]
    public async Task ClosedMarketWithoutQuotes_RemainsRunningAndRefreshesDailyHistory()
    {
        ControlledMarketDataClientProbe marketData = new();
        IbkrOptions ibkrOptions = new()
        {
            DailyHistoryRefreshInterval = TimeSpan.FromMilliseconds(20),
        };
        AlertOptions alertOptions = new() { EvaluationInterval = TimeSpan.FromMilliseconds(5) };
        PersistenceOptions persistenceOptions = new();
        MarketDataPipeline pipeline = new(
            new PriceStateStore(),
            new PriceHistoryStore(Options.Create(persistenceOptions)),
            new CandleAggregator([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]),
            Options.Create(ibkrOptions));
        AlertEngineProbe engine = new();
        ConfiguredCurrencyPairs pairs = new(Options.Create(new CurrencyPairOptions { Pairs = ["EUR/USD"] }));
        using HostApplicationLifetimeProbe lifetime = new();
        using MarketMonitoringService service = new(
            marketData,
            pipeline,
            engine,
            new FixedMarketSchedule(isOpen: false),
            pairs,
            Options.Create(ibkrOptions),
            Options.Create(alertOptions),
            new CommandLineOptions(false, false, null, false, false),
            new ApplicationRunResult(),
            TimeProvider.System,
            lifetime,
            NullLogger<MarketMonitoringService>.Instance);
        using CancellationTokenSource stopping = new();

        await service.StartAsync(stopping.Token);
        await AsyncTestProbe.UntilAsync(() => marketData.DailyHistoryRefreshCount > 0);

        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask.IsCompleted);
        Assert.Equal(0, engine.EvaluationCount);
        Assert.Equal(0, lifetime.StopApplicationCount);

        stopping.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Evaluation_WaitsForFreshSynchronizedMidpoint()
    {
        ControlledMarketDataClientProbe marketData = new();
        IbkrOptions ibkrOptions = new()
        {
            QuoteStaleAfter = TimeSpan.FromSeconds(1),
            MaximumBidAskSkew = TimeSpan.FromMilliseconds(100),
            DailyHistoryRefreshInterval = TimeSpan.FromHours(1),
        };
        AlertOptions alertOptions = new() { EvaluationInterval = TimeSpan.FromMilliseconds(10) };
        MarketDataPipeline pipeline = new(
            new PriceStateStore(),
            new PriceHistoryStore(Options.Create(new PersistenceOptions())),
            new CandleAggregator([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]),
            Options.Create(ibkrOptions));
        AlertEngineProbe engine = new();
        ConfiguredCurrencyPairs pairs = new(Options.Create(new CurrencyPairOptions { Pairs = ["EUR/USD"] }));
        using HostApplicationLifetimeProbe lifetime = new();
        using MarketMonitoringService service = new(
            marketData,
            pipeline,
            engine,
            new FixedMarketSchedule(isOpen: true),
            pairs,
            Options.Create(ibkrOptions),
            Options.Create(alertOptions),
            new CommandLineOptions(false, false, null, false, false),
            new ApplicationRunResult(),
            TimeProvider.System,
            lifetime,
            NullLogger<MarketMonitoringService>.Instance);
        using CancellationTokenSource stopping = new();

        await service.StartAsync(stopping.Token);
        await marketData.SubscriptionObserved.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(40));
        Assert.Equal(0, engine.EvaluationCount);

        CurrencyPair pair = CurrencyPair.Parse("EUR/USD");
        DateTimeOffset quoteTimeUtc = DateTimeOffset.UtcNow;
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Bid, 1.10d, quoteTimeUtc));
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Ask, 1.1002d, quoteTimeUtc));
        await AsyncTestProbe.UntilAsync(() => engine.EvaluationCount > 0);

        stopping.Cancel();
        await service.StopAsync(CancellationToken.None);
        Assert.All(engine.EvaluatedPairs, evaluatedPair => Assert.Equal(pair, evaluatedPair));
    }

    [Fact]
    public async Task Reconnect_MidwayThroughMinuteAndHour_DiscardsPreDisconnectPrices()
    {
        ControlledMarketDataClientProbe marketData = new();
        IbkrOptions ibkrOptions = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10),
        };
        AlertOptions alertOptions = new() { EvaluationInterval = TimeSpan.FromHours(1) };
        CandleAggregator aggregator = new([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]);
        MarketDataPipeline pipeline = new(
            new PriceStateStore(),
            new PriceHistoryStore(Options.Create(new PersistenceOptions())),
            aggregator,
            Options.Create(ibkrOptions));
        ConfiguredCurrencyPairs pairs = new(Options.Create(new CurrencyPairOptions { Pairs = ["EUR/USD"] }));
        using HostApplicationLifetimeProbe lifetime = new();
        using MarketMonitoringService service = new(
            marketData,
            pipeline,
            new AlertEngineProbe(),
            new FixedMarketSchedule(isOpen: true),
            pairs,
            Options.Create(ibkrOptions),
            Options.Create(alertOptions),
            new CommandLineOptions(false, false, null, false, false),
            new ApplicationRunResult(),
            TimeProvider.System,
            lifetime,
            NullLogger<MarketMonitoringService>.Instance);
        using CancellationTokenSource stopping = new();
        CurrencyPair pair = CurrencyPair.Parse("EUR/USD");
        DateTimeOffset hourStartUtc = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        await service.StartAsync(stopping.Token);
        await marketData.SubscriptionObserved.WaitAsync(TimeSpan.FromSeconds(1));
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Bid, 1.0998d, hourStartUtc.AddSeconds(10)));
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Ask, 1.1002d, hourStartUtc.AddSeconds(10)));
        marketData.EmitConnectionLost();
        await AsyncTestProbe.UntilAsync(() => marketData.SubscribeCount >= 2);
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Bid, 1.1998d, hourStartUtc.AddSeconds(40)));
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Ask, 1.2002d, hourStartUtc.AddSeconds(40)));

        stopping.Cancel();
        await service.StopAsync(CancellationToken.None);
        IReadOnlyList<Candle> completed = pipeline.AdvanceTo(hourStartUtc.AddHours(1));

        Candle minute = Assert.Single(completed, candle => candle.Interval == TimeSpan.FromMinutes(1));
        Candle hour = Assert.Single(completed, candle => candle.Interval == TimeSpan.FromHours(1));
        Assert.Equal(1.2d, minute.Open, precision: 10);
        Assert.Equal(1.2d, hour.Open, precision: 10);
    }

    [Fact]
    public async Task RunOnce_WaitsForReadyMidpointThenEvaluatesAndReturnsSuccess()
    {
        ControlledMarketDataClientProbe marketData = new();
        IbkrOptions ibkrOptions = new()
        {
            InitialDataTimeout = TimeSpan.FromSeconds(1),
            QuoteStaleAfter = TimeSpan.FromSeconds(5),
        };
        AlertOptions alertOptions = new();
        MarketDataPipeline pipeline = new(
            new PriceStateStore(),
            new PriceHistoryStore(Options.Create(new PersistenceOptions())),
            new CandleAggregator([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]),
            Options.Create(ibkrOptions));
        AlertEngineProbe engine = new();
        ConfiguredCurrencyPairs pairs = new(Options.Create(new CurrencyPairOptions { Pairs = ["EUR/USD"] }));
        ApplicationRunResult result = new();
        using HostApplicationLifetimeProbe lifetime = new();
        using MarketMonitoringService service = new(
            marketData,
            pipeline,
            engine,
            new FixedMarketSchedule(isOpen: true),
            pairs,
            Options.Create(ibkrOptions),
            Options.Create(alertOptions),
            new CommandLineOptions(false, false, null, true, false),
            result,
            TimeProvider.System,
            lifetime,
            NullLogger<MarketMonitoringService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await marketData.SubscriptionObserved.WaitAsync(TimeSpan.FromSeconds(1));
        CurrencyPair pair = CurrencyPair.Parse("EUR/USD");
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Bid, 1.10d, nowUtc));
        marketData.EmitQuote(new QuoteUpdate(pair, QuoteSide.Ask, 1.1002d, nowUtc));
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, engine.EvaluationCount);
        Assert.Equal(1, lifetime.StopApplicationCount);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunOnce_NoReadyMidpoint_ReturnsDistinctFailure()
    {
        ControlledMarketDataClientProbe marketData = new();
        IbkrOptions ibkrOptions = new() { InitialDataTimeout = TimeSpan.FromMilliseconds(50) };
        AlertOptions alertOptions = new();
        MarketDataPipeline pipeline = new(
            new PriceStateStore(),
            new PriceHistoryStore(Options.Create(new PersistenceOptions())),
            new CandleAggregator([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]),
            Options.Create(ibkrOptions));
        AlertEngineProbe engine = new();
        ConfiguredCurrencyPairs pairs = new(Options.Create(new CurrencyPairOptions { Pairs = ["EUR/USD"] }));
        ApplicationRunResult result = new();
        using HostApplicationLifetimeProbe lifetime = new();
        using MarketMonitoringService service = new(
            marketData,
            pipeline,
            engine,
            new FixedMarketSchedule(isOpen: true),
            pairs,
            Options.Create(ibkrOptions),
            Options.Create(alertOptions),
            new CommandLineOptions(false, false, null, true, false),
            result,
            TimeProvider.System,
            lifetime,
            NullLogger<MarketMonitoringService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ApplicationRunResult.NoFreshQuoteExitCode, result.ExitCode);
        Assert.Equal(0, engine.EvaluationCount);
        Assert.Equal(1, lifetime.StopApplicationCount);
        await service.StopAsync(CancellationToken.None);
    }

    private sealed class AlertEngineProbe : IAlertEngine
    {
        private readonly List<CurrencyPair> _evaluatedPairs = [];
        private int _evaluationCount;

        public int EvaluationCount => Volatile.Read(ref _evaluationCount);

        public IReadOnlyList<CurrencyPair> EvaluatedPairs
        {
            get
            {
                lock (_evaluatedPairs)
                {
                    return _evaluatedPairs.ToArray();
                }
            }
        }

        public Task<IReadOnlyList<AlertCandidate>> EvaluateAsync(
            IReadOnlyCollection<CurrencyPair> pairs,
            DateTimeOffset evaluationTimeUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_evaluatedPairs)
            {
                _evaluatedPairs.AddRange(pairs);
            }

            Interlocked.Increment(ref _evaluationCount);
            return Task.FromResult<IReadOnlyList<AlertCandidate>>([]);
        }
    }

    private sealed class FixedMarketSchedule(bool isOpen) : IMarketSchedule
    {
        public bool IsOpen(DateTimeOffset timestampUtc) => isOpen;

        public bool IsSleepWindow(DateTimeOffset timestampUtc) => false;

        public DateTimeOffset ToMarketTime(DateTimeOffset timestampUtc) => timestampUtc.ToUniversalTime();

        public DateOnly GetMarketDate(DateTimeOffset timestampUtc) =>
            DateOnly.FromDateTime(timestampUtc.UtcDateTime);

        public DateTimeOffset GetTradingDayOpenUtc(DateTimeOffset timestampUtc) =>
            new(timestampUtc.UtcDateTime.Date, TimeSpan.Zero);

        public string FormatMarketTime(DateTimeOffset timestampUtc) => timestampUtc.ToString("O");
    }
}
