using ForexAlert.Domain;

namespace ForexAlert.Infrastructure.Ibkr;

public sealed class UnavailableIbkrTransport : IIbkrTransport
{
    public const string ErrorMessage =
        "Live IBKR connectivity is unavailable in the public-safe build. " +
        "Obtain the official TWS API directly from Interactive Brokers, then build with " +
        "-p:IBApiAssemblyPath=<absolute-path-to-CSharpAPI.dll>.";

    public event Action? Ready
    {
        add { }
        remove { }
    }

    public event Action<IbkrTick>? TickReceived
    {
        add { }
        remove { }
    }

    public event Action<IbkrHistoricalBar>? HistoricalBarReceived
    {
        add { }
        remove { }
    }

    public event Action<int>? HistoricalDataEnded
    {
        add { }
        remove { }
    }

    public event Action<IbkrError>? ErrorReceived
    {
        add { }
        remove { }
    }

    public event Action? ConnectionClosed
    {
        add { }
        remove { }
    }

    public bool IsConnected => false;

    public Task ConnectAsync(
        string host,
        int port,
        int clientId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(ErrorMessage);
    }

    public void SubscribeMarketData(int requestId, CurrencyPair contractPair) =>
        throw new InvalidOperationException(ErrorMessage);

    public void CancelMarketData(int requestId)
    {
    }

    public void RequestHistoricalData(
        int requestId,
        CurrencyPair contractPair,
        IbkrHistoricalRequest request) =>
        throw new InvalidOperationException(ErrorMessage);

    public void CancelHistoricalData(int requestId)
    {
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
