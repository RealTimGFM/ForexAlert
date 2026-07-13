using ForexAlert.Domain;
using ForexAlert.Infrastructure.Ibkr;

namespace ForexAlert.Tests.TestDoubles;

internal sealed class ControlledMarketDataClientProbe : IIbkrMarketDataClient
{
    private readonly TaskCompletionSource _subscriptionObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connectCount;
    private int _subscribeCount;
    private int _disconnectCount;
    private int _dailyHistoryRefreshCount;
    private int _disposeCount;

    public event Action<QuoteUpdate>? QuoteReceived;

    public event Action<Candle>? HistoricalCandleReceived;

    public event Action? ConnectionLost;

    public bool IsConnected { get; private set; }

    public int ConnectCount => Volatile.Read(ref _connectCount);

    public int SubscribeCount => Volatile.Read(ref _subscribeCount);

    public int DisconnectCount => Volatile.Read(ref _disconnectCount);

    public int DailyHistoryRefreshCount => Volatile.Read(ref _dailyHistoryRefreshCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public Task SubscriptionObserved => _subscriptionObserved.Task;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _connectCount);
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _subscribeCount);
        _subscriptionObserved.TrySetResult();
        return Task.CompletedTask;
    }

    public Task RefreshDailyHistoryAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _dailyHistoryRefreshCount);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _disconnectCount);
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public void EmitQuote(QuoteUpdate quote) => QuoteReceived?.Invoke(quote);

    public void EmitHistoricalCandle(Candle candle) => HistoricalCandleReceived?.Invoke(candle);

    public void EmitConnectionLost()
    {
        IsConnected = false;
        ConnectionLost?.Invoke();
    }
}
