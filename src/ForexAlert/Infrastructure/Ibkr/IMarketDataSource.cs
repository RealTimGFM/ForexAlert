using ForexAlert.Domain;

namespace ForexAlert.Infrastructure.Ibkr;

public interface IMarketDataSource
{
    event Action<QuoteUpdate>? QuoteReceived;

    event Action<Candle>? HistoricalCandleReceived;

    event Action? ConnectionLost;

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task SubscribeAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken);

    Task RefreshDailyHistoryAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

public interface IIbkrMarketDataClient : IMarketDataSource, IAsyncDisposable;
