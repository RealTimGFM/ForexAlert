using ForexAlert.Domain;

namespace ForexAlert.Infrastructure.Ibkr;

public interface IIbkrTransport : IAsyncDisposable
{
    event Action? Ready;

    event Action<IbkrTick>? TickReceived;

    event Action<IbkrHistoricalBar>? HistoricalBarReceived;

    event Action<int>? HistoricalDataEnded;

    event Action<IbkrError>? ErrorReceived;

    event Action? ConnectionClosed;

    bool IsConnected { get; }

    Task ConnectAsync(
        string host,
        int port,
        int clientId,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    void SubscribeMarketData(int requestId, CurrencyPair contractPair);

    void CancelMarketData(int requestId);

    void RequestHistoricalData(int requestId, CurrencyPair contractPair, IbkrHistoricalRequest request);

    void CancelHistoricalData(int requestId);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

public sealed record IbkrTick(
    int RequestId,
    int Field,
    double Price,
    DateTimeOffset TimestampUtc);

public sealed record IbkrHistoricalBar(
    int RequestId,
    string Time,
    double Open,
    double High,
    double Low,
    double Close);

public sealed record IbkrError(int RequestId, int Code, string Message);

public sealed record IbkrHistoricalRequest(
    TimeSpan Interval,
    string Duration,
    string BarSize);
