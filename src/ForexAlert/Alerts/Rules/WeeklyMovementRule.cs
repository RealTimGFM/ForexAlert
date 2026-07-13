using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Options;

namespace ForexAlert.Alerts.Rules;

public sealed class WeeklyMovementRule(
    IPriceHistoryStore history,
    IMarketSchedule marketSchedule,
    IOptions<AlertOptions> options) : AlertRuleBase(marketSchedule)
{
    public const string RuleName = "weekly-movement";
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private readonly double _threshold = options.Value.WeeklyThresholdPercent;

    public override string Name => RuleName;

    public override ValueTask<AlertCandidate?> EvaluateAsync(
        CurrencyPair pair,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Candle[] closes = history.GetCompletedCandles(pair, Interval, evaluationTimeUtc, 14)
            .Where(candle =>
            {
                DayOfWeek day = MarketSchedule.ToMarketTime(candle.EndUtc.AddTicks(-1)).DayOfWeek;
                return day is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
            })
            .GroupBy(candle => MarketSchedule.GetMarketDate(candle.EndUtc.AddTicks(-1)))
            .Select(static group => group.OrderBy(static candle => candle.EndUtc).Last())
            .OrderBy(static candle => candle.EndUtc)
            .TakeLast(5)
            .ToArray();
        if (closes.Length < 5)
        {
            return ValueTask.FromResult<AlertCandidate?>(null);
        }

        Candle baseline = closes[0];
        Candle current = closes[^1];
        double change = PriceMath.PercentageChange(baseline.Close, current.Close);
        return ValueTask.FromResult<AlertCandidate?>(Math.Abs(change) >= _threshold
            ? CreateCandidate(pair, baseline, baseline.EndUtc, baseline.Close, current.EndUtc, current.Close, change, _threshold, evaluationTimeUtc)
            : null);
    }
}
