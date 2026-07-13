using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Options;

namespace ForexAlert.Alerts.Rules;

public sealed class HourlyMovementRule(
    IPriceHistoryStore history,
    IMarketSchedule marketSchedule,
    IOptions<AlertOptions> options) : AlertRuleBase(marketSchedule)
{
    public const string RuleName = "hourly-movement";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly double _threshold = options.Value.HourlyThresholdPercent;

    public override string Name => RuleName;

    public override ValueTask<AlertCandidate?> EvaluateAsync(
        CurrencyPair pair,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Candle? candle = history.GetCompletedCandles(pair, Interval, evaluationTimeUtc, 1).LastOrDefault();
        if (candle is null)
        {
            return ValueTask.FromResult<AlertCandidate?>(null);
        }

        double change = PriceMath.PercentageChange(candle.Open, candle.Close);
        return ValueTask.FromResult<AlertCandidate?>(Math.Abs(change) >= _threshold
            ? CreateCandidate(pair, candle, candle.StartUtc, candle.Open, candle.EndUtc, candle.Close, change, _threshold, evaluationTimeUtc)
            : null);
    }
}
