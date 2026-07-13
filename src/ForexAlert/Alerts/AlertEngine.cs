using ForexAlert.Domain;
using ForexAlert.Notifications;
using ForexAlert.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAlert.Alerts;

public sealed class AlertEngine(
    IEnumerable<IAlertRule> rules,
    INotificationSender notificationSender,
    IAlertStateStore stateStore,
    IOptions<AlertOptions> options,
    ILogger<AlertEngine> logger) : IAlertEngine
{
    private readonly IReadOnlyList<IAlertRule> _rules = rules.ToArray();
    private readonly TimeSpan _cooldown = options.Value.Cooldown;
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);

    public async Task<IReadOnlyList<AlertCandidate>> EvaluateAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        await _evaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<AlertCandidate> sent = [];
            foreach (CurrencyPair pair in pairs)
            {
                foreach (IAlertRule rule in _rules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AlertCandidate? candidate = await rule
                        .EvaluateAsync(pair, evaluationTimeUtc, cancellationToken)
                        .ConfigureAwait(false);
                    if (candidate is null)
                    {
                        continue;
                    }

                    AlertKey key = new(candidate.RuleName, candidate.CanonicalSymbol);
                    AlertDeliveryState? previousDelivery = await stateStore.GetAsync(key, cancellationToken).ConfigureAwait(false);
                    if (previousDelivery?.CandidateFingerprint == candidate.DeliveryFingerprint)
                    {
                        logger.LogDebug(
                            "Suppressed duplicate alert candidate for {Rule} {Symbol}",
                            candidate.RuleName,
                            candidate.CanonicalSymbol);
                        continue;
                    }

                    if (previousDelivery is not null && evaluationTimeUtc.ToUniversalTime() < previousDelivery.LastSentUtc.Add(_cooldown))
                    {
                        logger.LogDebug(
                            "Suppressed alert during cooldown for {Rule} {Symbol}; last sent at {LastSentUtc}",
                            candidate.RuleName,
                            candidate.CanonicalSymbol,
                            previousDelivery.LastSentUtc);
                        continue;
                    }

                    await notificationSender.SendAsync(candidate, cancellationToken).ConfigureAwait(false);
                    await stateStore.SetAsync(
                        key,
                        new AlertDeliveryState(
                            evaluationTimeUtc.ToUniversalTime(),
                            candidate.DeliveryFingerprint),
                        cancellationToken).ConfigureAwait(false);
                    sent.Add(candidate);
                    logger.LogInformation(
                        "Alert delivered for {Rule} {Symbol}: {PercentageChange:F4}% against threshold {Threshold:F4}% at {EvaluationTimeUtc}",
                        candidate.RuleName,
                        candidate.CanonicalSymbol,
                        candidate.PercentageChange,
                        candidate.TriggerThresholdPercent,
                        candidate.EvaluationTimeUtc);
                }
            }

            return sent;
        }
        finally
        {
            _evaluationGate.Release();
        }
    }
}
