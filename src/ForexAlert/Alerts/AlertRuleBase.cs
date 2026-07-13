using ForexAlert.Domain;
using ForexAlert.Scheduling;

namespace ForexAlert.Alerts;

public abstract class AlertRuleBase(IMarketSchedule marketSchedule) : IAlertRule
{
    protected IMarketSchedule MarketSchedule { get; } = marketSchedule;

    public abstract string Name { get; }

    public abstract ValueTask<AlertCandidate?> EvaluateAsync(
        CurrencyPair pair,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken);

    protected AlertCandidate CreateCandidate(
        CurrencyPair pair,
        Candle baseline,
        DateTimeOffset baselineTimestampUtc,
        double baselinePrice,
        DateTimeOffset currentTimestampUtc,
        double currentPrice,
        double percentageChange,
        double threshold,
        DateTimeOffset evaluationTimeUtc) =>
        new(
            Name,
            pair,
            baselineTimestampUtc,
            baselinePrice,
            currentTimestampUtc,
            currentPrice,
            percentageChange,
            percentageChange >= 0d ? AlertDirection.Up : AlertDirection.Down,
            threshold,
            evaluationTimeUtc.ToUniversalTime(),
            MarketSchedule.FormatMarketTime(evaluationTimeUtc));
}
