using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Notifications;
using ForexAlert.Persistence;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAlert.Alerts;

public sealed class AlertEvaluator(
    MarketDataState marketData,
    FxMarketSchedule schedule,
    INotificationSender notificationSender,
    CooldownStore cooldowns,
    IOptions<ForexAlertOptions> options,
    ILogger<AlertEvaluator> logger)
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private readonly ForexAlertOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<AlertCandidate>> EvaluateAndSendAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<AlertCandidate> sent = [];
            foreach (CurrencyPair pair in pairs)
            {
                foreach (AlertCandidate candidate in Evaluate(pair, evaluationTimeUtc))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await cooldowns.CanSendAsync(candidate, evaluationTimeUtc, _options.Cooldown, cancellationToken).ConfigureAwait(false)) continue;
                    await notificationSender.SendAsync(candidate, cancellationToken).ConfigureAwait(false);
                    await cooldowns.MarkSentAsync(candidate, evaluationTimeUtc, cancellationToken).ConfigureAwait(false);
                    sent.Add(candidate);
                    logger.LogInformation("Delivered {Rule} alert for {Symbol}: {Change:F4}%", candidate.RuleName, candidate.CanonicalSymbol, candidate.PercentageChange);
                }
            }
            return sent;
        }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<AlertCandidate> Evaluate(CurrencyPair pair, DateTimeOffset evaluationTimeUtc)
    {
        List<AlertCandidate> candidates = [];
        Add(candidates, CheckDaily(pair, evaluationTimeUtc));
        Add(candidates, CheckCompletedCandle(pair, evaluationTimeUtc, Hour, "hourly-movement", _options.HourlyThresholdPercent));
        Add(candidates, CheckWeekly(pair, evaluationTimeUtc));
        if (_options.OneMinuteEnabled == true) Add(candidates, CheckCompletedCandle(pair, evaluationTimeUtc, Minute, "one-minute-movement", _options.OneMinuteThresholdPercent));
        return candidates;
    }

    private AlertCandidate? CheckDaily(CurrencyPair pair, DateTimeOffset now)
    {
        if (!marketData.TryGetMidPrice(pair, now, out PricePoint? current) || current is null) return null;
        Candle? baseline = _options.DailyBaseline == DailyBaselineKind.PreviousClose
            ? marketData.GetCompletedCandles(pair, Day, now, 1).LastOrDefault()
            : marketData.GetCompletedCandles(pair, Minute, now, _options.MaxCandlesPerInterval).FirstOrDefault(c => c.StartUtc == schedule.GetTradingDayOpenUtc(now));
        if (baseline is null) return null;
        double baselinePrice = _options.DailyBaseline == DailyBaselineKind.PreviousClose ? baseline.Close : baseline.Open;
        DateTimeOffset baselineTime = _options.DailyBaseline == DailyBaselineKind.PreviousClose ? baseline.EndUtc : baseline.StartUtc;
        double change = PriceMath.PercentageChange(baselinePrice, current.Price);
        bool asleep = schedule.IsSleepWindow(now);
        double threshold = asleep ? _options.SleepWindowNegativeThresholdPercent : _options.DailyThresholdPercent;
        return (asleep ? change <= threshold : Math.Abs(change) >= threshold)
            ? Create("daily-movement", pair, baselineTime, baselinePrice, current.TimestampUtc, current.Price, change, threshold, now)
            : null;
    }

    private AlertCandidate? CheckCompletedCandle(CurrencyPair pair, DateTimeOffset now, TimeSpan interval, string rule, double threshold)
    {
        Candle? candle = marketData.GetCompletedCandles(pair, interval, now, 1).LastOrDefault();
        if (candle is null) return null;
        double change = PriceMath.PercentageChange(candle.Open, candle.Close);
        return Math.Abs(change) >= threshold ? Create(rule, pair, candle.StartUtc, candle.Open, candle.EndUtc, candle.Close, change, threshold, now) : null;
    }

    private AlertCandidate? CheckWeekly(CurrencyPair pair, DateTimeOffset now)
    {
        Candle[] closes = marketData.GetCompletedCandles(pair, Day, now, 14)
            .Where(c => schedule.ToMarketTime(c.EndUtc.AddTicks(-1)).DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .GroupBy(c => schedule.GetMarketDate(c.EndUtc.AddTicks(-1)))
            .Select(group => group.OrderBy(c => c.EndUtc).Last()).OrderBy(c => c.EndUtc).TakeLast(5).ToArray();
        if (closes.Length < 5) return null;
        Candle baseline = closes[0]; Candle current = closes[^1];
        double change = PriceMath.PercentageChange(baseline.Close, current.Close);
        return Math.Abs(change) >= _options.WeeklyThresholdPercent
            ? Create("weekly-movement", pair, baseline.EndUtc, baseline.Close, current.EndUtc, current.Close, change, _options.WeeklyThresholdPercent, now)
            : null;
    }

    private AlertCandidate Create(string rule, CurrencyPair pair, DateTimeOffset baselineTime, double baselinePrice, DateTimeOffset currentTime, double currentPrice, double change, double threshold, DateTimeOffset now) =>
        new(rule, pair, baselineTime, baselinePrice, currentTime, currentPrice, change, change >= 0 ? AlertDirection.Up : AlertDirection.Down, threshold, now.ToUniversalTime(), schedule.FormatMarketTime(now));

    private static void Add(List<AlertCandidate> list, AlertCandidate? candidate) { if (candidate is not null) list.Add(candidate); }
}
