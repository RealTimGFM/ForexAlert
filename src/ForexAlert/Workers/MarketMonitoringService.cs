using ForexAlert.Alerts;
using ForexAlert.Domain;
using ForexAlert.Infrastructure.Ibkr;
using ForexAlert.MarketData;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAlert.Workers;

public sealed class MarketMonitoringService(
    IIbkrMarketDataClient marketDataSource,
    IMarketDataPipeline pipeline,
    IAlertEngine alertEngine,
    IMarketSchedule marketSchedule,
    ConfiguredCurrencyPairs configuredPairs,
    IOptions<IbkrOptions> ibkrOptions,
    IOptions<AlertOptions> alertOptions,
    CommandLineOptions commandLine,
    ApplicationRunResult runResult,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<MarketMonitoringService> logger) : BackgroundService
{
    private readonly IbkrOptions _ibkr = ibkrOptions.Value;
    private readonly AlertOptions _alerts = alertOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        marketDataSource.QuoteReceived += pipeline.ProcessQuote;
        marketDataSource.HistoricalCandleReceived += pipeline.ProcessHistoricalCandle;
        Action connectionLostHandler = () =>
            pipeline.DiscardIncompleteLiveCandles(timeProvider.GetUtcNow());

        marketDataSource.ConnectionLost += connectionLostHandler;
        try
        {
            int attempt = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    attempt++;
                    await marketDataSource.ConnectAsync(stoppingToken).ConfigureAwait(false);
                    pipeline.DiscardIncompleteLiveCandles(timeProvider.GetUtcNow());
                    await marketDataSource.SubscribeAsync(configuredPairs.Values, stoppingToken).ConfigureAwait(false);
                    if (commandLine.RunOnce)
                    {
                        CurrencyPair[] readyPairs = await WaitForReadyPairsAsync(stoppingToken).ConfigureAwait(false);
                        if (readyPairs.Length == 0)
                        {
                            runResult.SetNoFreshQuote();
                            logger.LogError(
                                "No configured pair produced a fresh, synchronized bid/ask midpoint within {Timeout}; --once cannot evaluate alerts.",
                                _ibkr.InitialDataTimeout);
                        }
                        else
                        {
                            await EvaluateOnceAsync(readyPairs, stoppingToken).ConfigureAwait(false);
                        }

                        applicationLifetime.StopApplication();
                        return;
                    }

                    bool stableConnection = await MonitorConnectionAsync(stoppingToken).ConfigureAwait(false);
                    if (stableConnection)
                    {
                        attempt = 0;
                    }

                    throw new IOException("The IBKR market-data connection closed.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (attempt < _ibkr.MaxRetryAttempts)
                {
                    TimeSpan delay = GetRetryDelay(attempt);
                    logger.LogWarning(
                        exception,
                        "IBKR connection attempt {Attempt}/{MaximumAttempts} failed; retrying in {Delay}",
                        attempt,
                        _ibkr.MaxRetryAttempts,
                        delay);
                    await SafeDisconnectAsync().ConfigureAwait(false);
                    await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogCritical(
                        exception,
                        "IBKR connection failed after {MaximumAttempts} attempts",
                        _ibkr.MaxRetryAttempts);
                    throw;
                }
            }
        }
        finally
        {
            marketDataSource.QuoteReceived -= pipeline.ProcessQuote;
            marketDataSource.HistoricalCandleReceived -= pipeline.ProcessHistoricalCandle;
            marketDataSource.ConnectionLost -= connectionLostHandler;
            await SafeDisconnectAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> MonitorConnectionAsync(CancellationToken cancellationToken)
    {
        bool observedEvaluationInterval = false;
        DateTimeOffset nextDailyHistoryRefreshUtc =
            timeProvider.GetUtcNow().Add(_ibkr.DailyHistoryRefreshInterval);
        TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnectionLost()
        {
            disconnected.TrySetResult();
        }
        marketDataSource.ConnectionLost += OnConnectionLost;
        using PeriodicTimer timer = new(_alerts.EvaluationInterval, timeProvider);
        try
        {
            while (marketDataSource.IsConnected)
            {
                Task<bool> timerTick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
                Task completed = await Task.WhenAny(timerTick, disconnected.Task).ConfigureAwait(false);
                if (completed == disconnected.Task || !await timerTick.ConfigureAwait(false))
                {
                    break;
                }

                observedEvaluationInterval = true;
                DateTimeOffset nowUtc = timeProvider.GetUtcNow();
                if (nowUtc >= nextDailyHistoryRefreshUtc)
                {
                    try
                    {
                        await marketDataSource
                            .RefreshDailyHistoryAsync(configuredPairs.Values, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(
                            exception,
                            "Daily historical-data refresh failed; live monitoring remains active");
                    }

                    nextDailyHistoryRefreshUtc = GetNextDailyHistoryRefreshUtc(
                        nextDailyHistoryRefreshUtc,
                        nowUtc);
                }

                try
                {
                    await EvaluateOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Alert evaluation failed; the IBKR market-data connection remains active");
                }
            }
        }
        finally
        {
            marketDataSource.ConnectionLost -= OnConnectionLost;
        }

        return observedEvaluationInterval;
    }

    private async Task EvaluateOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        IReadOnlyList<Candle> completed = pipeline.AdvanceTo(nowUtc);
        if (completed.Count > 0)
        {
            logger.LogDebug("Completed {CandleCount} candle(s) through {WatermarkUtc}", completed.Count, nowUtc);
        }

        if (!marketSchedule.IsOpen(nowUtc))
        {
            logger.LogDebug("FX market is closed at {EvaluationTimeUtc}; alert evaluation skipped", nowUtc);
            return;
        }

        CurrencyPair[] readyPairs = configuredPairs.Values
            .Where(pair => pipeline.HasFreshMidpoint(pair, nowUtc))
            .ToArray();
        if (readyPairs.Length == 0)
        {
            logger.LogDebug(
                "No configured pair has a fresh, synchronized bid/ask midpoint at {EvaluationTimeUtc}; alert evaluation skipped",
                nowUtc);
            return;
        }

        await alertEngine.EvaluateAsync(readyPairs, nowUtc, cancellationToken).ConfigureAwait(false);
    }

    private async Task EvaluateOnceAsync(
        IReadOnlyCollection<CurrencyPair> readyPairs,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        IReadOnlyList<Candle> completed = pipeline.AdvanceTo(nowUtc);
        if (completed.Count > 0)
        {
            logger.LogDebug("Completed {CandleCount} candle(s) through {WatermarkUtc}", completed.Count, nowUtc);
        }

        if (!marketSchedule.IsOpen(nowUtc))
        {
            logger.LogDebug("FX market is closed at {EvaluationTimeUtc}; alert evaluation skipped", nowUtc);
            return;
        }

        await alertEngine.EvaluateAsync(readyPairs, nowUtc, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CurrencyPair[]> WaitForReadyPairsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset deadlineUtc = timeProvider.GetUtcNow().Add(_ibkr.InitialDataTimeout);
        while (true)
        {
            DateTimeOffset nowUtc = timeProvider.GetUtcNow();
            CurrencyPair[] readyPairs = configuredPairs.Values
                .Where(pair => pipeline.HasFreshMidpoint(pair, nowUtc))
                .ToArray();
            if (readyPairs.Length > 0 || nowUtc >= deadlineUtc)
            {
                return readyPairs;
            }

            TimeSpan remaining = deadlineUtc - nowUtc;
            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50),
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private DateTimeOffset GetNextDailyHistoryRefreshUtc(
        DateTimeOffset scheduledRefreshUtc,
        DateTimeOffset nowUtc)
    {
        long elapsedIntervals = Math.Max(
            1L,
            ((nowUtc - scheduledRefreshUtc).Ticks / _ibkr.DailyHistoryRefreshInterval.Ticks) + 1L);
        return scheduledRefreshUtc.AddTicks(
            checked(elapsedIntervals * _ibkr.DailyHistoryRefreshInterval.Ticks));
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        double multiplier = Math.Pow(2d, Math.Max(0, attempt - 1));
        double ticks = Math.Min(_ibkr.InitialRetryDelay.Ticks * multiplier, _ibkr.MaximumRetryDelay.Ticks);
        return TimeSpan.FromTicks((long)ticks);
    }

    private async Task SafeDisconnectAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        try
        {
            await marketDataSource.DisconnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or IOException)
        {
            logger.LogWarning(exception, "IBKR disconnect did not complete cleanly");
        }
    }
}
