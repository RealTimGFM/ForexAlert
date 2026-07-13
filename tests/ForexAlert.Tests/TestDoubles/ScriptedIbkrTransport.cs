using ForexAlert.Domain;
using ForexAlert.Infrastructure.Ibkr;

namespace ForexAlert.Tests.TestDoubles;

internal sealed class ScriptedIbkrTransport : IIbkrTransport
{
    private readonly object _operationGate = new();
    private readonly List<IbkrTransportProbeOperation> _operations = [];
    private readonly TaskCompletionSource _stalledConnection =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action? Ready;

    public event Action<IbkrTick>? TickReceived;

    public event Action<IbkrHistoricalBar>? HistoricalBarReceived;

    public event Action<int>? HistoricalDataEnded;

    public event Action<IbkrError>? ErrorReceived;

    public event Action? ConnectionClosed;

    public bool CompleteConnections { get; set; } = true;

    public bool AutoCompleteInitialData { get; set; } = true;

    public string HistoricalTimeValue { get; set; } = "1767225600";

    public bool IsConnected { get; private set; }

    public int DisposeCount { get; private set; }

    public IReadOnlyList<IbkrTransportProbeOperation> Operations
    {
        get
        {
            lock (_operationGate)
            {
                return _operations.ToArray();
            }
        }
    }

    public Task ConnectAsync(
        string host,
        int port,
        int clientId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Record(new IbkrTransportProbeOperation("Connect", 0, $"{host}:{port}/{clientId}"));
        if (!CompleteConnections)
        {
            return _stalledConnection.Task;
        }

        IsConnected = true;
        Ready?.Invoke();
        return Task.CompletedTask;
    }

    public void SubscribeMarketData(int requestId, CurrencyPair contractPair)
    {
        Record(new IbkrTransportProbeOperation("SubscribeMarketData", requestId, contractPair.CanonicalSymbol));
        if (AutoCompleteInitialData)
        {
            DateTimeOffset timestampUtc = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
            TickReceived?.Invoke(new IbkrTick(requestId, 1, 1.10d, timestampUtc));
            TickReceived?.Invoke(new IbkrTick(requestId, 2, 1.1002d, timestampUtc));
        }
    }

    public void CancelMarketData(int requestId) =>
        Record(new IbkrTransportProbeOperation("CancelMarketData", requestId, null));

    public void RequestHistoricalData(
        int requestId,
        CurrencyPair contractPair,
        IbkrHistoricalRequest request)
    {
        Record(new IbkrTransportProbeOperation("RequestHistoricalData", requestId, contractPair.CanonicalSymbol));
        if (AutoCompleteInitialData)
        {
            HistoricalBarReceived?.Invoke(new IbkrHistoricalBar(
                requestId,
                HistoricalTimeValue,
                1.10d,
                1.11d,
                1.09d,
                1.105d));
            HistoricalDataEnded?.Invoke(requestId);
        }
    }

    public void CancelHistoricalData(int requestId) =>
        Record(new IbkrTransportProbeOperation("CancelHistoricalData", requestId, null));

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Record(new IbkrTransportProbeOperation("Disconnect", 0, null));
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        Record(new IbkrTransportProbeOperation("Dispose", 0, null));
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public void EmitTick(IbkrTick tick) => TickReceived?.Invoke(tick);

    public void EmitHistoricalBar(IbkrHistoricalBar bar) => HistoricalBarReceived?.Invoke(bar);

    public void EmitHistoricalDataEnded(int requestId) => HistoricalDataEnded?.Invoke(requestId);

    public void EmitError(IbkrError error) => ErrorReceived?.Invoke(error);

    public void EmitConnectionClosed() => ConnectionClosed?.Invoke();

    private void Record(IbkrTransportProbeOperation operation)
    {
        lock (_operationGate)
        {
            _operations.Add(operation with { RecordedAtUtc = DateTimeOffset.UtcNow });
        }
    }
}

internal sealed record IbkrTransportProbeOperation(string Name, int RequestId, string? Symbol)
{
    public DateTimeOffset RecordedAtUtc { get; init; }
}
