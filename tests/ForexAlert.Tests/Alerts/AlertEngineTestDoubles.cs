using System.Collections.Concurrent;
using ForexAlert.Alerts;
using ForexAlert.Domain;
using ForexAlert.Notifications;
using ForexAlert.Persistence;

namespace ForexAlert.Tests.Alerts;

internal sealed class AlwaysCandidateAlertRule(string name) : IAlertRule
{
    public string Name { get; } = name;

    public ValueTask<AlertCandidate?> EvaluateAsync(
        CurrencyPair pair,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset normalizedEvaluationTime = evaluationTimeUtc.ToUniversalTime();
        return ValueTask.FromResult<AlertCandidate?>(new AlertCandidate(
            Name,
            pair,
            normalizedEvaluationTime.Subtract(TimeSpan.FromHours(1)),
            1d,
            normalizedEvaluationTime,
            1.02d,
            2d,
            AlertDirection.Up,
            1d,
            normalizedEvaluationTime,
            $"{normalizedEvaluationTime:O} (Test/UTC)"));
    }
}

internal sealed class RecordingAlertNotificationSender(
    Func<AlertCandidate, CancellationToken, Task>? beforeRecord = null) : INotificationSender
{
    private readonly ConcurrentQueue<AlertCandidate> _alerts = new();

    public IReadOnlyCollection<AlertCandidate> Alerts => _alerts.ToArray();

    public async Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken)
    {
        if (beforeRecord is not null)
        {
            await beforeRecord(alert, cancellationToken);
        }

        _alerts.Enqueue(alert);
    }
}

internal sealed class InspectableAlertStateStore : IAlertStateStore
{
    private readonly ConcurrentDictionary<AlertKey, AlertDeliveryState> _state = new();
    private int _setCount;

    public int SetCount => Volatile.Read(ref _setCount);

    public void Seed(AlertKey key, DateTimeOffset timestampUtc, string fingerprint = "") =>
        _state[key] = new AlertDeliveryState(timestampUtc.ToUniversalTime(), fingerprint);

    public ValueTask<AlertDeliveryState?> GetAsync(
        AlertKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<AlertDeliveryState?>(
            _state.TryGetValue(key, out AlertDeliveryState? value) ? value : null);
    }

    public ValueTask SetAsync(
        AlertKey key,
        AlertDeliveryState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state[key] = state;
        Interlocked.Increment(ref _setCount);
        return ValueTask.CompletedTask;
    }
}
