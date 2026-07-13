using ForexAlert.Domain;

namespace ForexAlert.Alerts;

public interface IAlertRule
{
    string Name { get; }

    ValueTask<AlertCandidate?> EvaluateAsync(
        CurrencyPair pair,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken);
}
