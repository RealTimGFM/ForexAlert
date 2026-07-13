using ForexAlert.Domain;
using IBApi;

namespace ForexAlert.Infrastructure.Ibkr;

public sealed class IbkrSocketTransport(TimeProvider timeProvider) : IIbkrTransport
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private EReaderMonitorSignal? _signal;
    private EClientSocket? _socket;
    private EReader? _reader;
    private CancellationTokenSource? _messagePumpCancellation;
    private Task? _messagePump;
    private bool _disposed;

    public event Action? Ready;

    public event Action<IbkrTick>? TickReceived;

    public event Action<IbkrHistoricalBar>? HistoricalBarReceived;

    public event Action<int>? HistoricalDataEnded;

    public event Action<IbkrError>? ErrorReceived;

    public event Action? ConnectionClosed;

    public bool IsConnected => _socket?.IsConnected() == true;

    public async Task ConnectAsync(
        string host,
        int port,
        int clientId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                return;
            }

            IbkrCallbackAdapter callback = new(timeProvider);
            Wire(callback);
            _signal = new EReaderMonitorSignal();
            _socket = new EClientSocket(callback, _signal);

            TaskCompletionSource readiness = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void MarkReady() => readiness.TrySetResult();
            callback.Ready += MarkReady;
            try
            {
                await Task.Run(() => _socket.eConnect(host, port, clientId), CancellationToken.None)
                    .WaitAsync(timeout, timeProvider, cancellationToken)
                    .ConfigureAwait(false);
                if (!_socket.IsConnected())
                {
                    throw new InvalidOperationException("IBKR rejected the connection before protocol initialization completed.");
                }

                _reader = new EReader(_socket, _signal);
                _reader.Start();
                _messagePumpCancellation = new CancellationTokenSource();
                _messagePump = Task.Factory.StartNew(
                    () => PumpMessages(_messagePumpCancellation.Token),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                await readiness.Task
                    .WaitAsync(timeout, timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await DisconnectCoreAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                callback.Ready -= MarkReady;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void SubscribeMarketData(int requestId, CurrencyPair contractPair)
    {
        EClientSocket socket = GetConnectedSocket();
        socket.reqMktData(requestId, CreateContract(contractPair), string.Empty, false, false, null);
    }

    public void CancelMarketData(int requestId)
    {
        if (IsConnected)
        {
            _socket!.cancelMktData(requestId);
        }
    }

    public void RequestHistoricalData(int requestId, CurrencyPair contractPair, IbkrHistoricalRequest request)
    {
        EClientSocket socket = GetConnectedSocket();
        socket.reqHistoricalData(
            requestId,
            CreateContract(contractPair),
            string.Empty,
            request.Duration,
            request.BarSize,
            "MIDPOINT",
            0,
            2,
            false,
            null);
    }

    public void CancelHistoricalData(int requestId)
    {
        if (IsConnected)
        {
            _socket!.cancelHistoricalData(requestId);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private static Contract CreateContract(CurrencyPair pair) => new()
    {
        Symbol = pair.BaseCurrency,
        Currency = pair.QuoteCurrency,
        SecType = "CASH",
        Exchange = "IDEALPRO",
    };

    private EClientSocket GetConnectedSocket() =>
        IsConnected ? _socket! : throw new InvalidOperationException("IBKR is not connected.");

    private void PumpMessages(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                _signal!.waitForSignal();
                _reader.processMsgs();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ErrorReceived?.Invoke(new IbkrError(-1, 0, exception.GetType().Name));
                _socket?.eDisconnect();
            }
        }
    }

    private async Task DisconnectCoreAsync()
    {
        _messagePumpCancellation?.Cancel();
        _socket?.eDisconnect();
        _signal?.issueSignal();

        if (_messagePump is not null)
        {
            try
            {
                await _messagePump.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ErrorReceived?.Invoke(new IbkrError(-1, 0, "Message pump shutdown timeout"));
            }
        }

        _messagePumpCancellation?.Dispose();
        _messagePumpCancellation = null;
        _messagePump = null;
        _reader = null;
        _socket = null;
        _signal = null;
    }

    private void Wire(IbkrCallbackAdapter callback)
    {
        callback.Ready += () => Ready?.Invoke();
        callback.TickReceived += tick => TickReceived?.Invoke(tick);
        callback.HistoricalBarReceived += bar => HistoricalBarReceived?.Invoke(bar);
        callback.HistoricalDataEnded += requestId => HistoricalDataEnded?.Invoke(requestId);
        callback.ErrorReceived += error => ErrorReceived?.Invoke(error);
        callback.ConnectionClosed += () => ConnectionClosed?.Invoke();
    }
}
