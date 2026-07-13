using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAlert.Infrastructure.Ibkr;

public sealed class IbkrMarketDataClient : IIbkrMarketDataClient
{
    private readonly IIbkrTransport _transport;
    private readonly RequestIdRegistry _requests;
    private readonly IbkrOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _marketTimeZone;
    private readonly TimeSpan _tradingDayOpenTime;
    private readonly ILogger<IbkrMarketDataClient> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Channel<TransportEvent> _events = Channel.CreateBounded<TransportEvent>(
        new BoundedChannelOptions(8_192)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<int, HistoricalReadiness> _historicalReadiness = new();
    private readonly CancellationTokenSource _eventPumpCancellation = new();
    private readonly Task _eventPump;
    private int _healthy = 1;
    private bool _disposed;

    public IbkrMarketDataClient(
        IIbkrTransport transport,
        RequestIdRegistry requests,
        IOptions<IbkrOptions> options,
        IOptions<MarketScheduleOptions> marketScheduleOptions,
        TimeProvider timeProvider,
        ILogger<IbkrMarketDataClient> logger)
    {
        _transport = transport;
        _requests = requests;
        _options = options.Value;
        _timeProvider = timeProvider;
        MarketScheduleOptions marketSchedule = marketScheduleOptions.Value;
        _marketTimeZone = TimeZoneInfo.FindSystemTimeZoneById(marketSchedule.TimeZone);
        _tradingDayOpenTime = marketSchedule.TradingDayOpenTime;
        _logger = logger;

        _transport.TickReceived += OnTickReceived;
        _transport.HistoricalBarReceived += OnHistoricalBarReceived;
        _transport.HistoricalDataEnded += OnHistoricalDataEnded;
        _transport.ErrorReceived += OnErrorReceived;
        _transport.ConnectionClosed += OnConnectionClosed;
        _eventPump = ProcessEventsAsync(_eventPumpCancellation.Token);
    }

    public event Action<QuoteUpdate>? QuoteReceived;

    public event Action<Candle>? HistoricalCandleReceived;

    public event Action? ConnectionLost;

    public bool IsConnected => _transport.IsConnected && Volatile.Read(ref _healthy) == 1;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Volatile.Write(ref _healthy, 1);
        Task connection = _transport.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.ClientId,
            _options.ConnectionTimeout,
            cancellationToken);

        try
        {
            await connection
                .WaitAsync(_options.ConnectionTimeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await _transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "Connected to IBKR market-data endpoint {Host}:{Port} with client ID {ClientId}",
            _options.Host,
            _options.Port,
            _options.ClientId);
    }

    public async Task SubscribeAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Cannot subscribe before the IBKR connection is ready.");
            }

            List<Task> historicalDataTasks = [];
            foreach (CurrencyPair pair in pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SubscriptionDescriptor streaming = _requests.Register(
                    pair,
                    pair,
                    inverse: false,
                    MarketDataRequestKind.Streaming);
                _transport.SubscribeMarketData(streaming.RequestId, streaming.ContractPair);
                _logger.LogInformation(
                    "Subscribed to {Symbol} with request ID {RequestId}",
                    pair.CanonicalSymbol,
                    streaming.RequestId);

                historicalDataTasks.Add(RequestHistory(pair, TimeSpan.FromDays(1), "10 D", "1 day"));
                await DelayForPacingAsync(cancellationToken).ConfigureAwait(false);
                historicalDataTasks.Add(RequestHistory(pair, TimeSpan.FromHours(1), "3 D", "1 hour"));
                await DelayForPacingAsync(cancellationToken).ConfigureAwait(false);
                historicalDataTasks.Add(RequestHistory(pair, TimeSpan.FromMinutes(1), "1 D", "1 min"));
                await DelayForPacingAsync(cancellationToken).ConfigureAwait(false);
            }

            await Task.WhenAll(historicalDataTasks)
                .WaitAsync(_options.InitialDataTimeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Initial historical responses are ready for {PairCount} pair(s); alert evaluation will wait for fresh bid/ask midpoints",
                pairs.Count);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RefreshDailyHistoryAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Cannot refresh daily history without an active IBKR connection.");
            }

            List<Task> refreshTasks = [];
            int pairIndex = 0;
            foreach (CurrencyPair pair in pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                refreshTasks.Add(RequestHistory(pair, TimeSpan.FromDays(1), "10 D", "1 day"));
                pairIndex++;
                if (pairIndex < pairs.Count)
                {
                    await DelayForPacingAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await Task.WhenAll(refreshTasks)
                .WaitAsync(_options.InitialDataTimeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Refreshed completed daily history for {PairCount} pair(s)", pairs.Count);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (SubscriptionDescriptor descriptor in _requests.Snapshot())
            {
                TryCancel(descriptor);
                _requests.Remove(descriptor.RequestId, out _);
            }

            CancelPendingReadiness();
            await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Disconnected from IBKR market data");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "IBKR disconnect failed during disposal");
        }

        _transport.TickReceived -= OnTickReceived;
        _transport.HistoricalBarReceived -= OnHistoricalBarReceived;
        _transport.HistoricalDataEnded -= OnHistoricalDataEnded;
        _transport.ErrorReceived -= OnErrorReceived;
        _transport.ConnectionClosed -= OnConnectionClosed;
        _events.Writer.TryComplete();
        _eventPumpCancellation.Cancel();
        try
        {
            await _eventPump.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_eventPumpCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "IBKR callback pump stopped with an error during disposal");
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
        _eventPumpCancellation.Dispose();
        _operationGate.Dispose();
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TransportEvent transportEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    switch (transportEvent)
                    {
                        case TickTransportEvent tick:
                            ProcessTick(tick.Value);
                            break;
                        case HistoricalBarTransportEvent historical:
                            ProcessHistoricalBar(historical.Value);
                            break;
                        case HistoricalEndTransportEvent historicalEnd:
                            CompleteHistoricalRequest(historicalEnd.RequestId);
                            break;
                        case ErrorTransportEvent error:
                            await ProcessErrorAsync(error.Value, cancellationToken).ConfigureAwait(false);
                            break;
                        case ConnectionClosedTransportEvent:
                            MarkConnectionLost("IBKR market-data connection closed");
                            break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    HandleEventFailure(transportEvent, exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _healthy, 0);
            FailPendingReadiness(exception);
            _logger.LogCritical(exception, "IBKR callback pump terminated unexpectedly");
            ConnectionLost?.Invoke();
        }
    }

    private void ProcessTick(IbkrTick tick)
    {
        QuoteSide? rawSide = tick.Field switch
        {
            1 => QuoteSide.Bid,
            2 => QuoteSide.Ask,
            _ => null,
        };
        if (!rawSide.HasValue || !PriceMath.IsValid(tick.Price) ||
            !_requests.TryGet(tick.RequestId, out SubscriptionDescriptor? descriptor) ||
            descriptor is null || descriptor.Kind != MarketDataRequestKind.Streaming)
        {
            return;
        }

        (QuoteSide side, double price) = PriceNormalizer.Normalize(rawSide.Value, tick.Price, descriptor.IsInverse);
        QuoteReceived?.Invoke(new QuoteUpdate(
            descriptor.RequestedPair,
            side,
            price,
            tick.TimestampUtc.ToUniversalTime()));
    }

    private void ProcessHistoricalBar(IbkrHistoricalBar rawBar)
    {
        if (!_requests.TryGet(rawBar.RequestId, out SubscriptionDescriptor? descriptor) ||
            descriptor is null || descriptor.Kind != MarketDataRequestKind.Historical ||
            !descriptor.BarInterval.HasValue || !TryParseTimestamp(rawBar.Time, out DateTimeOffset startUtc) ||
            !new[] { rawBar.Open, rawBar.High, rawBar.Low, rawBar.Close }.All(PriceMath.IsValid))
        {
            return;
        }

        try
        {
            Candle rawCandle = new(
                descriptor.ContractPair,
                startUtc,
                descriptor.BarInterval.Value,
                rawBar.Open,
                rawBar.High,
                rawBar.Low,
                rawBar.Close);
            if (_historicalReadiness.TryGetValue(rawBar.RequestId, out HistoricalReadiness? readiness))
            {
                readiness.RecordResponseBar();
            }

            if (!IsHistoricalBarCompleted(startUtc, descriptor.BarInterval.Value, _timeProvider.GetUtcNow()))
            {
                _logger.LogDebug(
                    "Ignored incomplete {Interval} historical bar starting {StartUtc} for {Symbol}",
                    descriptor.BarInterval.Value,
                    startUtc,
                    descriptor.RequestedPair.CanonicalSymbol);
                return;
            }

            HistoricalCandleReceived?.Invoke(
                PriceNormalizer.Normalize(rawCandle, descriptor.RequestedPair, descriptor.IsInverse));
        }
        catch (ArgumentException)
        {
            _logger.LogWarning(
                "Rejected malformed historical bar for request ID {RequestId} and {Symbol}",
                rawBar.RequestId,
                descriptor.RequestedPair.CanonicalSymbol);
        }
    }

    private async Task ProcessErrorAsync(IbkrError error, CancellationToken cancellationToken)
    {
        if (IbkrErrorClassifier.IsConnectionLost(error.Code))
        {
            _logger.LogWarning("IBKR reported lost connectivity; closing the data connection so the host can reconnect");
            await _transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            MarkConnectionLost("IBKR connectivity was lost");
            return;
        }

        if (IbkrErrorClassifier.RequiresResubscription(error.Code))
        {
            await ResubscribeActiveRequestsAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IbkrErrorClassifier.IsFarmConnectivityWarning(error.Code))
        {
            _logger.LogWarning(
                "IBKR data-farm connectivity warning code {Code} for request ID {RequestId}",
                error.Code,
                error.RequestId);
            return;
        }

        if (IbkrErrorClassifier.IsInformational(error.Code))
        {
            _logger.LogInformation(
                "IBKR informational status code {Code} for request ID {RequestId}",
                error.Code,
                error.RequestId);
            return;
        }

        if (IbkrErrorClassifier.IsMissingContract(error.Code) &&
            _requests.Remove(error.RequestId, out SubscriptionDescriptor? descriptor) &&
            descriptor is not null)
        {
            _historicalReadiness.TryRemove(error.RequestId, out HistoricalReadiness? historicalReadiness);
            TryCancel(descriptor);
            if (descriptor.IsInverse)
            {
                InvalidOperationException failure = new(
                    $"IBKR could not resolve either orientation of {descriptor.RequestedPair.CanonicalSymbol}.");
                historicalReadiness?.Completion.TrySetException(failure);
                _logger.LogError(
                    "IBKR could not resolve either orientation of {Symbol}; request ID {RequestId}",
                    descriptor.RequestedPair.CanonicalSymbol,
                    descriptor.RequestId);
                return;
            }

            SubscriptionDescriptor replacement = _requests.Register(
                descriptor.RequestedPair,
                descriptor.RequestedPair.Inverse(),
                inverse: true,
                descriptor.Kind,
                descriptor.BarInterval);
            if (historicalReadiness is not null)
            {
                _historicalReadiness[replacement.RequestId] = historicalReadiness;
            }

            Issue(replacement);
            _logger.LogWarning(
                "Retrying {Symbol} through inverse contract {ContractSymbol}; old request ID {OldRequestId}, new request ID {NewRequestId}",
                descriptor.RequestedPair.CanonicalSymbol,
                replacement.ContractPair.CanonicalSymbol,
                descriptor.RequestId,
                replacement.RequestId);
            return;
        }

        if (_requests.Remove(error.RequestId, out SubscriptionDescriptor? failedDescriptor) && failedDescriptor is not null)
        {
            TryCancel(failedDescriptor);
            InvalidOperationException failure = new(
                $"IBKR request {error.RequestId} failed with code {error.Code}.");
            if (_historicalReadiness.TryRemove(error.RequestId, out HistoricalReadiness? readiness))
            {
                readiness.Completion.TrySetException(failure);
            }

            if (failedDescriptor.Kind == MarketDataRequestKind.Streaming)
            {
                MarkConnectionLost("An active IBKR streaming request failed");
            }
        }

        _logger.LogError(
            "IBKR failure code {Code} for request ID {RequestId}",
            error.Code,
            error.RequestId);
    }

    private async Task ResubscribeActiveRequestsAsync(CancellationToken cancellationToken)
    {
        SubscriptionDescriptor[] activeRequests = _requests.Snapshot().ToArray();
        bool issuedHistoricalRequest = false;
        foreach (SubscriptionDescriptor descriptor in activeRequests)
        {
            _historicalReadiness.TryRemove(descriptor.RequestId, out HistoricalReadiness? readiness);
            TryCancel(descriptor);
            _requests.Remove(descriptor.RequestId, out _);
            SubscriptionDescriptor replacement = _requests.Register(
                descriptor.RequestedPair,
                descriptor.ContractPair,
                descriptor.IsInverse,
                descriptor.Kind,
                descriptor.BarInterval);
            if (readiness is not null)
            {
                _historicalReadiness[replacement.RequestId] = readiness;
            }

            if (replacement.Kind == MarketDataRequestKind.Historical && issuedHistoricalRequest)
            {
                await DelayForPacingAsync(cancellationToken).ConfigureAwait(false);
            }

            Issue(replacement);
            issuedHistoricalRequest |= replacement.Kind == MarketDataRequestKind.Historical;
        }

        _logger.LogWarning(
            "IBKR restored connectivity with data loss; resubmitted {RequestCount} active request(s) with new IDs",
            activeRequests.Length);
    }

    private Task RequestHistory(CurrencyPair pair, TimeSpan interval, string duration, string barSize)
    {
        SubscriptionDescriptor descriptor = _requests.Register(
            pair,
            pair,
            inverse: false,
            MarketDataRequestKind.Historical,
            interval);
        HistoricalReadiness readiness = new();
        _historicalReadiness[descriptor.RequestId] = readiness;
        try
        {
            _transport.RequestHistoricalData(
                descriptor.RequestId,
                descriptor.ContractPair,
                new IbkrHistoricalRequest(interval, duration, barSize));
        }
        catch
        {
            _historicalReadiness.TryRemove(descriptor.RequestId, out _);
            _requests.Remove(descriptor.RequestId, out _);
            throw;
        }

        _logger.LogDebug(
            "Requested {Interval} history for {Symbol} with request ID {RequestId}",
            interval,
            pair.CanonicalSymbol,
            descriptor.RequestId);
        return readiness.Completion.Task;
    }

    private async Task DelayForPacingAsync(CancellationToken cancellationToken)
    {
        if (_options.HistoricalRequestSpacing > TimeSpan.Zero)
        {
            await Task.Delay(_options.HistoricalRequestSpacing, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Issue(SubscriptionDescriptor descriptor)
    {
        if (descriptor.Kind == MarketDataRequestKind.Streaming)
        {
            _transport.SubscribeMarketData(descriptor.RequestId, descriptor.ContractPair);
        }
        else
        {
            _transport.RequestHistoricalData(
                descriptor.RequestId,
                descriptor.ContractPair,
                CreateHistoricalRequest(descriptor.BarInterval!.Value));
        }
    }

    private void TryCancel(SubscriptionDescriptor descriptor)
    {
        try
        {
            if (descriptor.Kind == MarketDataRequestKind.Streaming)
            {
                _transport.CancelMarketData(descriptor.RequestId);
            }
            else
            {
                _transport.CancelHistoricalData(descriptor.RequestId);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to cancel IBKR request ID {RequestId} for {Symbol}",
                descriptor.RequestId,
                descriptor.RequestedPair.CanonicalSymbol);
        }
    }

    private static IbkrHistoricalRequest CreateHistoricalRequest(TimeSpan interval) =>
        interval == TimeSpan.FromDays(1)
            ? new IbkrHistoricalRequest(interval, "10 D", "1 day")
            : interval == TimeSpan.FromHours(1)
                ? new IbkrHistoricalRequest(interval, "3 D", "1 hour")
                : new IbkrHistoricalRequest(interval, "1 D", "1 min");

    private bool TryParseTimestamp(string value, out DateTimeOffset timestampUtc)
    {
        timestampUtc = default;
        if (value.Length >= 9 && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long epochSeconds))
        {
            try
            {
                timestampUtc = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        string timestampWithoutZone = value.Length > 17 ? value[..17] : value;
        string[] formats = ["yyyyMMdd", "yyyyMMdd HH:mm:ss", "yyyyMMdd-HH:mm:ss"];
        if (!DateTime.TryParseExact(
            timestampWithoutZone,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out DateTime parsed))
        {
            return false;
        }

        DateTime unspecified = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        if (_marketTimeZone.IsInvalidTime(unspecified))
        {
            return false;
        }

        timestampUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(unspecified, _marketTimeZone),
            TimeSpan.Zero);
        return true;
    }

    private bool IsHistoricalBarCompleted(
        DateTimeOffset startUtc,
        TimeSpan interval,
        DateTimeOffset nowUtc)
    {
        if (interval != TimeSpan.FromDays(1))
        {
            return startUtc.Add(interval) <= nowUtc.ToUniversalTime();
        }

        DateTimeOffset marketStart = TimeZoneInfo.ConvertTime(startUtc, _marketTimeZone);
        DateTime localEnd = DateTime.SpecifyKind(
            marketStart.Date.AddDays(1).Add(_tradingDayOpenTime),
            DateTimeKind.Unspecified);
        if (_marketTimeZone.IsInvalidTime(localEnd))
        {
            return false;
        }

        DateTimeOffset endUtc = new(
            TimeZoneInfo.ConvertTimeToUtc(localEnd, _marketTimeZone),
            TimeSpan.Zero);
        return endUtc <= nowUtc.ToUniversalTime();
    }

    private void CompleteHistoricalRequest(int requestId)
    {
        _requests.Remove(requestId, out _);
        if (_historicalReadiness.TryRemove(requestId, out HistoricalReadiness? readiness))
        {
            if (readiness.ResponseBarsReceived > 0)
            {
                readiness.Completion.TrySetResult();
            }
            else
            {
                readiness.Completion.TrySetException(new InvalidDataException(
                    $"IBKR historical request {requestId} completed without usable bars."));
            }
        }
    }

    private void HandleEventFailure(TransportEvent transportEvent, Exception exception)
    {
        Volatile.Write(ref _healthy, 0);
        int? requestId = transportEvent switch
        {
            HistoricalBarTransportEvent historical => historical.Value.RequestId,
            HistoricalEndTransportEvent historicalEnd => historicalEnd.RequestId,
            _ => null,
        };
        if (requestId.HasValue && _historicalReadiness.TryRemove(requestId.Value, out HistoricalReadiness? readiness))
        {
            readiness.Completion.TrySetException(exception);
        }

        _logger.LogError(exception, "Failed to process an IBKR callback; reconnect is required");
        ConnectionLost?.Invoke();
    }

    private void MarkConnectionLost(string reason)
    {
        Volatile.Write(ref _healthy, 0);
        FailPendingReadiness(new IOException(reason));
        _logger.LogWarning("{ConnectionLossReason}", reason);
        ConnectionLost?.Invoke();
    }

    private void CancelPendingReadiness()
    {
        FailPendingReadiness(new OperationCanceledException("IBKR initial-data wait was cancelled by disconnect."));
        _historicalReadiness.Clear();
    }

    private void FailPendingReadiness(Exception exception)
    {
        foreach (HistoricalReadiness readiness in _historicalReadiness.Values)
        {
            readiness.Completion.TrySetException(exception);
        }
    }

    private void OnTickReceived(IbkrTick value) => Queue(new TickTransportEvent(value));

    private void OnHistoricalBarReceived(IbkrHistoricalBar value) => Queue(new HistoricalBarTransportEvent(value));

    private void OnHistoricalDataEnded(int requestId) => Queue(new HistoricalEndTransportEvent(requestId));

    private void OnErrorReceived(IbkrError value) => Queue(new ErrorTransportEvent(value));

    private void OnConnectionClosed() => Queue(new ConnectionClosedTransportEvent());

    private void Queue(TransportEvent transportEvent)
    {
        if (!_events.Writer.TryWrite(transportEvent))
        {
            InvalidOperationException overflow = new("The bounded IBKR callback queue is full.");
            Volatile.Write(ref _healthy, 0);
            FailPendingReadiness(overflow);
            _logger.LogCritical(
                "Rejected an IBKR callback because the bounded callback queue is full; reconnect is required");
            ConnectionLost?.Invoke();
        }
    }

    private abstract record TransportEvent;

    private sealed record TickTransportEvent(IbkrTick Value) : TransportEvent;

    private sealed record HistoricalBarTransportEvent(IbkrHistoricalBar Value) : TransportEvent;

    private sealed record HistoricalEndTransportEvent(int RequestId) : TransportEvent;

    private sealed record ErrorTransportEvent(IbkrError Value) : TransportEvent;

    private sealed record ConnectionClosedTransportEvent : TransportEvent;

    private sealed class HistoricalReadiness
    {
        private int _responseBarsReceived;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ResponseBarsReceived => Volatile.Read(ref _responseBarsReceived);

        public void RecordResponseBar() => Interlocked.Increment(ref _responseBarsReceived);
    }
}
