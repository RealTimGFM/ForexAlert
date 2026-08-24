using System.Collections.Concurrent;
using ForexAlert.Domain;
using ForexAlert.Infrastructure.Ibkr;
using ForexAlert.MarketData;
using ForexAlert.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Infrastructure;

public sealed class IbkrMarketDataClientTests
{
    [Fact]
    public async Task ConnectAsync_WhenTransportStalls_TimesOutAndDisconnects()
    {
        ScriptedIbkrTransport transport = new() { CompleteConnections = false };
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromMilliseconds(50));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => client.ConnectAsync(CancellationToken.None));

            Assert.False(client.IsConnected);
            Assert.Equal(["Connect", "Disconnect"], transport.Operations.Select(operation => operation.Name));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConnectAsync_WhenCancelled_PropagatesCancellationAndDisconnects()
    {
        ScriptedIbkrTransport transport = new() { CompleteConnections = false };
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(10));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cancellation.Token));

            Assert.False(client.IsConnected);
            Assert.Equal(["Connect", "Disconnect"], transport.Operations.Select(operation => operation.Name));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task MissingContract_RetriesInverseAfterCancellation_AndNormalizesQuotes()
    {
        ScriptedIbkrTransport transport = new();
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1));
        ConcurrentQueue<QuoteUpdate> quotes = new();
        client.QuoteReceived += quotes.Enqueue;
        try
        {
            CurrencyPair requestedPair = CurrencyPair.Parse("EUR/USD");
            await client.ConnectAsync(CancellationToken.None);
            await client.SubscribeAsync([requestedPair], CancellationToken.None);
            quotes.Clear();
            transport.AutoCompleteInitialData = false;

            transport.EmitError(new IbkrError(1, 2104, "Market data farm connection is OK"));
            transport.EmitError(new IbkrError(1, 200, "No security definition was found"));
            await AsyncTestProbe.UntilAsync(() => transport.Operations.Any(operation =>
                operation.Name == "SubscribeMarketData" && operation.Symbol == "USD/EUR"));

            IReadOnlyList<IbkrTransportProbeOperation> operations = transport.Operations;
            int cancellationIndex = IndexOf(operations, "CancelMarketData", requestId: 1);
            int inverseIndex = IndexOf(operations, "SubscribeMarketData", symbol: "USD/EUR");
            Assert.True(cancellationIndex >= 0);
            Assert.Equal(cancellationIndex + 1, inverseIndex);
            IbkrTransportProbeOperation inverseSubscription = operations[inverseIndex];
            Assert.Equal(5, inverseSubscription.RequestId);
            Assert.Single(operations, operation => operation.Name == "CancelMarketData");

            DateTimeOffset timestamp = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
            transport.EmitTick(new IbkrTick(inverseSubscription.RequestId, 1, 0.80, timestamp));
            transport.EmitTick(new IbkrTick(inverseSubscription.RequestId, 2, 0.81, timestamp));
            await AsyncTestProbe.UntilAsync(() => quotes.Count == 2);

            QuoteUpdate[] received = quotes.ToArray();
            Assert.All(received, quote => Assert.Equal(requestedPair, quote.Pair));
            QuoteUpdate normalizedAsk = Assert.Single(received, quote => quote.Side == QuoteSide.Ask);
            QuoteUpdate normalizedBid = Assert.Single(received, quote => quote.Side == QuoteSide.Bid);
            Assert.Equal(1.25, normalizedAsk.Price, 12);
            Assert.Equal(1d / 0.81, normalizedBid.Price, 12);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_CancelsEveryRequestBeforeDisconnecting_AndIsIdempotent()
    {
        ScriptedIbkrTransport transport = new() { AutoCompleteInitialData = false };
        RequestIdRegistry registry = new();
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1), registry);
        await client.ConnectAsync(CancellationToken.None);
        using CancellationTokenSource subscriptionCancellation = new();
        Task subscription = client.SubscribeAsync([CurrencyPair.Parse("EUR/USD")], subscriptionCancellation.Token);
        await AsyncTestProbe.UntilAsync(() => transport.Operations.Count >= 4);
        subscriptionCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription);

        await client.DisposeAsync();
        await client.DisposeAsync();

        IReadOnlyList<IbkrTransportProbeOperation> operations = transport.Operations;
        int disconnectIndex = IndexOf(operations, "Disconnect");
        Assert.True(IndexOf(operations, "CancelMarketData", requestId: 1) < disconnectIndex);
        Assert.True(IndexOf(operations, "CancelHistoricalData", requestId: 2) < disconnectIndex);
        Assert.True(IndexOf(operations, "CancelHistoricalData", requestId: 3) < disconnectIndex);
        Assert.True(IndexOf(operations, "CancelHistoricalData", requestId: 4) < disconnectIndex);
        Assert.Empty(registry.Snapshot());
        Assert.False(transport.IsConnected);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal("Dispose", operations[^1].Name);
    }

    [Fact]
    public async Task RestoredConnectivityWithDataLoss_ResubmitsEveryActiveRequestWithNewIds()
    {
        ScriptedIbkrTransport transport = new();
        RequestIdRegistry registry = new();
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1), registry);
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            await client.SubscribeAsync([CurrencyPair.Parse("EUR/USD")], CancellationToken.None);

            transport.EmitError(new IbkrError(-1, 1101, "Connectivity restored - data lost"));
            await AsyncTestProbe.UntilAsync(() =>
                transport.Operations.Count(operation => operation.Name == "SubscribeMarketData") == 2);

            IReadOnlyList<IbkrTransportProbeOperation> operations = transport.Operations;
            Assert.Equal(2, operations.Count(operation => operation.Name == "SubscribeMarketData"));
            Assert.Equal(3, operations.Count(operation => operation.Name == "RequestHistoricalData"));
            Assert.Single(operations, operation => operation.Name == "CancelMarketData");
            Assert.DoesNotContain(operations, operation => operation.Name == "CancelHistoricalData");
            Assert.Equal([5], registry.Snapshot().Select(descriptor => descriptor.RequestId));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task LostConnectivity_ClosesTransportSoHostCanReconnect()
    {
        ScriptedIbkrTransport transport = new();
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(CancellationToken.None);

            transport.EmitError(new IbkrError(-1, 1100, "Connectivity lost"));
            await AsyncTestProbe.UntilAsync(() => !client.IsConnected);

            Assert.Contains(transport.Operations, operation => operation.Name == "Disconnect");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task SubscribeAsync_WaitsForEveryHistoricalEndButNotForBidAsk()
    {
        ScriptedIbkrTransport transport = new() { AutoCompleteInitialData = false };
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            Task subscription = client.SubscribeAsync([CurrencyPair.Parse("EUR/USD")], CancellationToken.None);
            await AsyncTestProbe.UntilAsync(() =>
                transport.Operations.Count(operation => operation.Name == "RequestHistoricalData") == 3);

            foreach (int requestId in new[] { 2, 3, 4 })
            {
                transport.EmitHistoricalBar(new IbkrHistoricalBar(
                    requestId,
                    "1782907200",
                    1.10d,
                    1.11d,
                    1.09d,
                    1.105d));
            }

            await Task.Yield();
            Assert.False(subscription.IsCompleted);

            transport.EmitHistoricalDataEnded(2);
            transport.EmitHistoricalDataEnded(3);
            Assert.False(subscription.IsCompleted);
            transport.EmitHistoricalDataEnded(4);
            await subscription.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task HistoricalServiceConnectionStatus_DoesNotFailOrCancelHistoricalRequest()
    {
        ScriptedIbkrTransport transport = new() { AutoCompleteInitialData = false };
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            Task subscription = client.SubscribeAsync(
                [CurrencyPair.Parse("EUR/USD")],
                CancellationToken.None);
            await AsyncTestProbe.UntilAsync(() =>
                transport.Operations.Count(operation => operation.Name == "RequestHistoricalData") == 3);

            int[] historicalRequestIds = transport.Operations
                .Where(operation => operation.Name == "RequestHistoricalData")
                .Select(operation => operation.RequestId)
                .ToArray();

            transport.EmitError(new IbkrError(
                historicalRequestIds[0],
                165,
                "Historical Market Data Service query message: HMDS SERVER CONNECTION WAS SUCCESSFUL."));
            foreach (int requestId in historicalRequestIds)
            {
                EmitSuccessfulHistoricalResponse(transport, requestId);
            }

            await subscription.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.DoesNotContain(
                transport.Operations,
                operation => operation.Name == "CancelHistoricalData");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task MarketDataFarmConnectingStatus_DoesNotFailOrRemoveRequests()
    {
        ScriptedIbkrTransport transport = new() { AutoCompleteInitialData = false };
        RequestIdRegistry registry = new();
        IbkrMarketDataClient client = CreateClient(
            transport,
            TimeSpan.FromSeconds(1),
            registry);
        ConcurrentQueue<QuoteUpdate> quotes = new();
        client.QuoteReceived += quotes.Enqueue;
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            Task subscription = client.SubscribeAsync(
                [CurrencyPair.Parse("EUR/USD")],
                CancellationToken.None);
            await AsyncTestProbe.UntilAsync(() =>
                transport.Operations.Count(operation => operation.Name == "RequestHistoricalData") == 3);

            int[] historicalRequestIds = transport.Operations
                .Where(operation => operation.Name == "RequestHistoricalData")
                .Select(operation => operation.RequestId)
                .ToArray();
            transport.EmitError(new IbkrError(
                historicalRequestIds[0],
                2119,
                "Market data farm is connecting:cashfarm"));
            transport.EmitTick(new IbkrTick(
                1,
                1,
                1.10d,
                new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero)));
            await AsyncTestProbe.UntilAsync(() => quotes.Count == 1);

            Assert.Equal(4, registry.Snapshot().Count);
            Assert.DoesNotContain(
                transport.Operations,
                operation => operation.Name.StartsWith("Cancel", StringComparison.Ordinal));

            foreach (int requestId in historicalRequestIds)
            {
                EmitSuccessfulHistoricalResponse(transport, requestId);
            }

            await subscription.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task HistoricalDataFailureWithCode165_StillFailsAndCancelsHistoricalRequest()
    {
        await AssertHistoricalRequestFailureAsync(
            165,
            "Historical Market Data Service query message: no historical data available.");
    }

    [Fact]
    public async Task HistoricalDataCancellationWithCode162_RemainsFatal()
    {
        await AssertHistoricalRequestFailureAsync(
            162,
            "API historical data query cancelled");
    }

    [Fact]
    public async Task StartupMidwayThroughMinute_DoesNotPublishCurrentHistoricalMinuteAsCompleted()
    {
        DateTimeOffset nowUtc = new(2026, 7, 1, 12, 0, 30, TimeSpan.Zero);
        await AssertIncompleteHistoricalBarIgnoredAsync(
            targetRequestId: 4,
            targetInterval: TimeSpan.FromMinutes(1),
            targetTime: nowUtc.AddSeconds(-30).ToUnixTimeSeconds().ToString(),
            nowUtc);
    }

    [Fact]
    public async Task StartupMidwayThroughHour_DoesNotPublishCurrentHistoricalHourAsCompleted()
    {
        DateTimeOffset nowUtc = new(2026, 7, 1, 12, 30, 0, TimeSpan.Zero);
        await AssertIncompleteHistoricalBarIgnoredAsync(
            targetRequestId: 3,
            targetInterval: TimeSpan.FromHours(1),
            targetTime: nowUtc.AddMinutes(-30).ToUnixTimeSeconds().ToString(),
            nowUtc);
    }

    [Fact]
    public async Task StartupMidwayThroughTradingDay_DoesNotPublishCurrentHistoricalDayAsCompleted()
    {
        DateTimeOffset nowUtc = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        await AssertIncompleteHistoricalBarIgnoredAsync(
            targetRequestId: 2,
            targetInterval: TimeSpan.FromDays(1),
            targetTime: "20260701",
            nowUtc);
    }

    [Fact]
    public async Task RefreshDailyHistoryAsync_RequestsAndPublishesNewCompletedDailyBars()
    {
        ScriptedIbkrTransport transport = new();
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1));
        ConcurrentQueue<Candle> candles = new();
        client.HistoricalCandleReceived += candles.Enqueue;
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            await client.SubscribeAsync([CurrencyPair.Parse("EUR/USD")], CancellationToken.None);
            int dailyBeforeRefresh = candles.Count(candle => candle.Interval == TimeSpan.FromDays(1));

            await client.RefreshDailyHistoryAsync(
                [CurrencyPair.Parse("EUR/USD")],
                CancellationToken.None);

            Assert.Equal(1, dailyBeforeRefresh);
            Assert.Equal(2, candles.Count(candle => candle.Interval == TimeSpan.FromDays(1)));
            Assert.Equal(
                4,
                transport.Operations.Count(operation => operation.Name == "RequestHistoricalData"));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task RestoredConnectivity_PacesResubmittedHistoricalRequests()
    {
        ScriptedIbkrTransport transport = new() { AutoCompleteInitialData = false };
        TimeSpan spacing = TimeSpan.FromMilliseconds(40);
        IbkrMarketDataClient client = CreateClient(
            transport,
            TimeSpan.FromSeconds(1),
            options: new IbkrOptions
            {
                Host = "127.0.0.1",
                PaperTrading = true,
                PaperPort = 4002,
                LivePort = 4001,
                ConnectionTimeout = TimeSpan.FromSeconds(1),
                InitialDataTimeout = TimeSpan.FromSeconds(2),
                HistoricalRequestSpacing = spacing,
            });
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            Task subscription = client.SubscribeAsync(
                [CurrencyPair.Parse("EUR/USD")],
                CancellationToken.None);
            await AsyncTestProbe.UntilAsync(() =>
                transport.Operations.Count(operation => operation.Name == "RequestHistoricalData") == 3);

            transport.EmitError(new IbkrError(-1, 1101, "Connectivity restored - data lost"));
            await AsyncTestProbe.UntilAsync(() =>
                transport.Operations.Count(operation => operation.Name == "RequestHistoricalData") == 6);

            IbkrTransportProbeOperation[] resubmitted = transport.Operations
                .Where(operation => operation.Name == "RequestHistoricalData")
                .TakeLast(3)
                .ToArray();
            Assert.All(
                resubmitted.Zip(resubmitted.Skip(1)),
                pair => Assert.True(
                    pair.Second.RecordedAtUtc - pair.First.RecordedAtUtc >= TimeSpan.FromMilliseconds(30),
                    $"Historical requests were only {pair.Second.RecordedAtUtc - pair.First.RecordedAtUtc} apart."));

            foreach (IbkrTransportProbeOperation operation in resubmitted)
            {
                transport.EmitHistoricalBar(new IbkrHistoricalBar(
                    operation.RequestId,
                    "1767225600",
                    1.10d,
                    1.11d,
                    1.09d,
                    1.105d));
                transport.EmitHistoricalDataEnded(operation.RequestId);
            }

            await subscription.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task LocalHistoricalDate_IsConvertedFromConfiguredMarketZoneToUtc()
    {
        ScriptedIbkrTransport transport = new() { HistoricalTimeValue = "20260701" };
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1));
        ConcurrentQueue<Candle> candles = new();
        client.HistoricalCandleReceived += candles.Enqueue;
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            await client.SubscribeAsync([CurrencyPair.Parse("EUR/USD")], CancellationToken.None);

            Assert.NotEmpty(candles);
            Assert.All(candles, candle => Assert.Equal(
                new DateTimeOffset(2026, 7, 1, 4, 0, 0, TimeSpan.Zero),
                candle.StartUtc));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static IbkrMarketDataClient CreateClient(
        ScriptedIbkrTransport transport,
        TimeSpan connectionTimeout,
        RequestIdRegistry? registry = null,
        IbkrOptions? options = null,
        TimeProvider? timeProvider = null) =>
        new(
            transport,
            registry ?? new RequestIdRegistry(),
            Options.Create(options ?? new IbkrOptions
            {
                Host = "127.0.0.1",
                PaperTrading = true,
                PaperPort = 4002,
                LivePort = 4001,
                ConnectionTimeout = connectionTimeout,
                InitialDataTimeout = TimeSpan.FromSeconds(1),
                HistoricalRequestSpacing = TimeSpan.Zero,
            }),
            Options.Create(new ForexAlertOptions { MarketTimeZone = "America/New_York" }),
            timeProvider ?? TimeProvider.System,
            NullLogger<IbkrMarketDataClient>.Instance);

    private static async Task AssertIncompleteHistoricalBarIgnoredAsync(
        int targetRequestId,
        TimeSpan targetInterval,
        string targetTime,
        DateTimeOffset nowUtc)
    {
        ScriptedIbkrTransport transport = new() { AutoCompleteInitialData = false };
        IbkrMarketDataClient client = CreateClient(
            transport,
            TimeSpan.FromSeconds(1),
            timeProvider: new FixedTimeProvider(nowUtc));
        ConcurrentQueue<Candle> candles = new();
        client.HistoricalCandleReceived += candles.Enqueue;
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            Task subscription = client.SubscribeAsync(
                [CurrencyPair.Parse("EUR/USD")],
                CancellationToken.None);
            await AsyncTestProbe.UntilAsync(() => transport.Operations.Count >= 4);

            Dictionary<int, string> completedBarTimes = new()
            {
                [2] = "20260630",
                [3] = nowUtc.AddHours(-2).ToUnixTimeSeconds().ToString(),
                [4] = nowUtc.AddMinutes(-2).ToUnixTimeSeconds().ToString(),
            };
            completedBarTimes[targetRequestId] = targetTime;
            foreach ((int requestId, string time) in completedBarTimes)
            {
                transport.EmitHistoricalBar(new IbkrHistoricalBar(
                    requestId,
                    time,
                    1.10d,
                    1.11d,
                    1.09d,
                    1.105d));
                transport.EmitHistoricalDataEnded(requestId);
            }

            await subscription.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.DoesNotContain(candles, candle => candle.Interval == targetInterval);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static async Task AssertHistoricalRequestFailureAsync(int code, string message)
    {
        ScriptedIbkrTransport transport = new() { AutoCompleteInitialData = false };
        IbkrMarketDataClient client = CreateClient(transport, TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(CancellationToken.None);
            Task subscription = client.SubscribeAsync(
                [CurrencyPair.Parse("EUR/USD")],
                CancellationToken.None);
            await AsyncTestProbe.UntilAsync(() =>
                transport.Operations.Count(operation => operation.Name == "RequestHistoricalData") == 3);

            int[] historicalRequestIds = transport.Operations
                .Where(operation => operation.Name == "RequestHistoricalData")
                .Select(operation => operation.RequestId)
                .ToArray();
            int failedRequestId = historicalRequestIds[0];
            IbkrError error = new(failedRequestId, code, message);
            transport.EmitError(error);
            foreach (int requestId in historicalRequestIds.Skip(1))
            {
                EmitSuccessfulHistoricalResponse(transport, requestId);
            }

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => subscription.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(
                $"IBKR request {error.RequestId} failed with code {error.Code}.",
                failure.Message);
            Assert.Contains(
                transport.Operations,
                operation => operation.Name == "CancelHistoricalData" &&
                    operation.RequestId == error.RequestId);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static void EmitSuccessfulHistoricalResponse(
        ScriptedIbkrTransport transport,
        int requestId)
    {
        transport.EmitHistoricalBar(new IbkrHistoricalBar(
            requestId,
            "1767225600",
            1.10d,
            1.11d,
            1.09d,
            1.105d));
        transport.EmitHistoricalDataEnded(requestId);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static int IndexOf(
        IReadOnlyList<IbkrTransportProbeOperation> operations,
        string name,
        int? requestId = null,
        string? symbol = null)
    {
        for (int index = 0; index < operations.Count; index++)
        {
            IbkrTransportProbeOperation operation = operations[index];
            if (operation.Name == name &&
                (!requestId.HasValue || operation.RequestId == requestId) &&
                (symbol is null || operation.Symbol == symbol))
            {
                return index;
            }
        }

        return -1;
    }
}
