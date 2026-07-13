using ForexAlert.Domain;

namespace ForexAlert.Alerts;

public interface IAlertEngine
{
    Task<IReadOnlyList<AlertCandidate>> EvaluateAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken);
}
