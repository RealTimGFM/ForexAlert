using ForexAlert.Alerts;
using ForexAlert.Domain;
using ForexAlert.Infrastructure.Ibkr;
using ForexAlert.MarketData;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAlert;

public sealed class ForexMonitor(
    IIbkrMarketDataClient ibkr,
    MarketDataState marketData,
    AlertEvaluator alerts,
    FxMarketSchedule schedule,
    IOptions<ForexAlertOptions> appOptions,
    IOptions<IbkrOptions> ibkrOptions,
    TimeProvider timeProvider,
    ILogger<ForexMonitor> logger) : BackgroundService
{
    private readonly ForexAlertOptions _app = appOptions.Value;
    private readonly IbkrOptions _ibkr = ibkrOptions.Value;
    private readonly CurrencyPair[] _pairs = appOptions.Value.CurrencyPairs.Select(CurrencyPair.Parse).ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ibkr.QuoteReceived += marketData.ProcessQuote;
        ibkr.HistoricalCandleReceived += marketData.AddHistoricalCandle;
        void ResetState() => marketData.ResetAfterDisconnect(timeProvider.GetUtcNow());
        ibkr.ConnectionLost += ResetState;
        try
        {
            int attempt = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    attempt++;
                    await ibkr.ConnectAsync(stoppingToken).ConfigureAwait(false);
                    ResetState();
                    await ibkr.SubscribeAsync(_pairs, stoppingToken).ConfigureAwait(false);
                    bool stable = await MonitorConnectionAsync(stoppingToken).ConfigureAwait(false);
                    if (stable) attempt = 0;
                    throw new IOException("The IBKR market-data connection closed.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) when (attempt < _ibkr.MaxRetryAttempts)
                {
                    TimeSpan delay = RetryDelay(attempt);
                    logger.LogWarning(exception, "IBKR attempt {Attempt}/{Maximum} failed; retrying in {Delay}", attempt, _ibkr.MaxRetryAttempts, delay);
                    await SafeDisconnectAsync().ConfigureAwait(false);
                    await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ibkr.QuoteReceived -= marketData.ProcessQuote;
            ibkr.HistoricalCandleReceived -= marketData.AddHistoricalCandle;
            ibkr.ConnectionLost -= ResetState;
            await SafeDisconnectAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> MonitorConnectionAsync(CancellationToken cancellationToken)
    {
        bool evaluated = false;
        DateTimeOffset nextHistoryRefresh = timeProvider.GetUtcNow().Add(_ibkr.DailyHistoryRefreshInterval);
        TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Lost() => disconnected.TrySetResult();
        ibkr.ConnectionLost += Lost;
        using PeriodicTimer timer = new(_app.EvaluationInterval, timeProvider);
        try
        {
            while (ibkr.IsConnected)
            {
                Task<bool> tick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
                Task completed = await Task.WhenAny(tick, disconnected.Task).ConfigureAwait(false);
                if (completed == disconnected.Task || !await tick.ConfigureAwait(false)) break;
                evaluated = true;
                DateTimeOffset now = timeProvider.GetUtcNow();
                if (now >= nextHistoryRefresh)
                {
                    try { await ibkr.RefreshDailyHistoryAsync(_pairs, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception exception) { logger.LogError(exception, "Daily history refresh failed; live monitoring continues"); }
                    do { nextHistoryRefresh = nextHistoryRefresh.Add(_ibkr.DailyHistoryRefreshInterval); } while (nextHistoryRefresh <= now);
                }
                await EvaluateAsync(now, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { ibkr.ConnectionLost -= Lost; }
        return evaluated;
    }

    private async Task EvaluateAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        _ = marketData.AdvanceTo(now);
        if (!schedule.IsOpen(now)) return;
        CurrencyPair[] ready = _pairs.Where(pair => marketData.HasFreshMidpoint(pair, now)).ToArray();
        if (ready.Length == 0) return;
        try { await alerts.EvaluateAndSendAsync(ready, now, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Alert evaluation failed; IBKR monitoring continues"); }
    }

    private TimeSpan RetryDelay(int attempt) => TimeSpan.FromTicks((long)Math.Min(
        _ibkr.InitialRetryDelay.Ticks * Math.Pow(2d, Math.Max(0, attempt - 1)), _ibkr.MaximumRetryDelay.Ticks));

    private async Task SafeDisconnectAsync()
    {
        using CancellationTokenSource timeout = new(_ibkr.DisconnectTimeout);
        try { await ibkr.DisconnectAsync(timeout.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or IOException)
        { logger.LogWarning(exception, "IBKR disconnect did not complete cleanly"); }
    }
}
