using ForexAlert.Alerts;
using ForexAlert.Domain;
using ForexAlert.Infrastructure.Ibkr;
using ForexAlert.MarketData;
using ForexAlert.Persistence;
using ForexAlert.Scheduling;
using ForexAlert.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests;

public sealed class ForexMonitorTests
{
    [Fact]
    public async Task ConnectsAndSubscribesOnStartup()
    {
        FakeIbkrMarketDataClient client = new();
        ForexMonitor monitor = CreateMonitor(client, out _, out _);

        await monitor.StartAsync(CancellationToken.None);
        await AsyncTestProbe.UntilAsync(() => client.SubscribeCalls == 1);
        await monitor.StopAsync(CancellationToken.None);

        Assert.Equal(1, client.ConnectCalls);
        Assert.Equal([CurrencyPair.Parse("EUR/USD")], client.SubscribedPairs);
    }

    [Fact]
    public async Task ReconnectsAndResubscribesAfterConnectionLoss()
    {
        FakeIbkrMarketDataClient client = new();
        ForexMonitor monitor = CreateMonitor(client, out _, out _);

        await monitor.StartAsync(CancellationToken.None);
        await AsyncTestProbe.UntilAsync(() => client.SubscribeCalls == 1);
        client.RaiseConnectionLost();
        await AsyncTestProbe.UntilAsync(() => client.SubscribeCalls == 2);
        await monitor.StopAsync(CancellationToken.None);

        Assert.Equal(2, client.ConnectCalls);
        Assert.True(client.DisconnectCalls >= 2);
    }

    [Fact]
    public async Task DoesNotSendAlertsWithoutFreshQuotes()
    {
        FakeIbkrMarketDataClient client = new();
        ForexMonitor monitor = CreateMonitor(client, out CapturingNotificationSender notifications, out MarketDataState marketData);
        CurrencyPair pair = CurrencyPair.Parse("EUR/USD");
        DateTimeOffset start = new(DateTime.UtcNow.AddDays(-1).Date, TimeSpan.Zero);
        marketData.AddHistoricalCandle(new Candle(
            pair,
            start,
            TimeSpan.FromDays(1),
            1d,
            1d,
            1d,
            1d));

        await monitor.StartAsync(CancellationToken.None);
        await AsyncTestProbe.UntilAsync(() => client.RefreshCalls > 0);
        await monitor.StopAsync(CancellationToken.None);

        Assert.Empty(notifications.Alerts);
    }

    [Fact]
    public async Task StopsAndDisconnectsCleanly()
    {
        FakeIbkrMarketDataClient client = new();
        ForexMonitor monitor = CreateMonitor(client, out _, out _);

        await monitor.StartAsync(CancellationToken.None);
        await AsyncTestProbe.UntilAsync(() => client.SubscribeCalls == 1);
        await monitor.StopAsync(CancellationToken.None);

        Assert.False(client.IsConnected);
        Assert.Equal(1, client.DisconnectCalls);
    }

    private static ForexMonitor CreateMonitor(
        FakeIbkrMarketDataClient client,
        out CapturingNotificationSender notifications,
        out MarketDataState marketData)
    {
        ForexAlertOptions app = new()
        {
            CurrencyPairs = ["EUR/USD"],
            EvaluationInterval = TimeSpan.FromMilliseconds(5),
            MarketTimeZone = "America/New_York",
            CooldownStatePath = Path.Combine(Path.GetTempPath(), $"forexalert-monitor-{Guid.NewGuid():N}.json"),
        };
        IbkrOptions ibkr = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(1),
            DailyHistoryRefreshInterval = TimeSpan.FromMilliseconds(15),
        };
        IOptions<ForexAlertOptions> appOptions = Options.Create(app);
        IOptions<IbkrOptions> ibkrOptions = Options.Create(ibkr);
        TimeProvider timeProvider = TimeProvider.System;
        marketData = new MarketDataState(appOptions, ibkrOptions, timeProvider);
        FxMarketSchedule schedule = new(appOptions);
        notifications = new CapturingNotificationSender();
        AlertEvaluator evaluator = new(
            marketData,
            schedule,
            notifications,
            new CooldownStore(appOptions),
            appOptions,
            NullLogger<AlertEvaluator>.Instance);
        return new ForexMonitor(
            client,
            marketData,
            evaluator,
            schedule,
            appOptions,
            ibkrOptions,
            timeProvider,
            NullLogger<ForexMonitor>.Instance);
    }

    private sealed class FakeIbkrMarketDataClient : IIbkrMarketDataClient
    {
        private int _connectCalls;
        private int _subscribeCalls;
        private int _refreshCalls;
        private int _disconnectCalls;
        private int _connected;

        public event Action<QuoteUpdate>? QuoteReceived;

        public event Action<Candle>? HistoricalCandleReceived;

        public event Action? ConnectionLost;

        public bool IsConnected => Volatile.Read(ref _connected) == 1;

        public int ConnectCalls => Volatile.Read(ref _connectCalls);

        public int SubscribeCalls => Volatile.Read(ref _subscribeCalls);

        public int RefreshCalls => Volatile.Read(ref _refreshCalls);

        public int DisconnectCalls => Volatile.Read(ref _disconnectCalls);

        public IReadOnlyList<CurrencyPair> SubscribedPairs { get; private set; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _connectCalls);
            Volatile.Write(ref _connected, 1);
            return Task.CompletedTask;
        }

        public Task SubscribeAsync(IReadOnlyCollection<CurrencyPair> pairs, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubscribedPairs = pairs.ToArray();
            Interlocked.Increment(ref _subscribeCalls);
            return Task.CompletedTask;
        }

        public Task RefreshDailyHistoryAsync(IReadOnlyCollection<CurrencyPair> pairs, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _refreshCalls);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _disconnectCalls);
            Volatile.Write(ref _connected, 0);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseConnectionLost()
        {
            Volatile.Write(ref _connected, 0);
            ConnectionLost?.Invoke();
        }

        public void RaiseQuote(QuoteUpdate update) => QuoteReceived?.Invoke(update);

        public void RaiseHistoricalCandle(Candle candle) => HistoricalCandleReceived?.Invoke(candle);
    }
}
