using System.Collections.Concurrent;
using ForexAlert.Domain;
using ForexAlert.Notifications;

namespace ForexAlert.Tests.TestDoubles;

internal sealed class CapturingNotificationSender : INotificationSender
{
    private readonly ConcurrentQueue<AlertCandidate> _alerts = new();

    public IReadOnlyList<AlertCandidate> Alerts => _alerts.ToArray();

    public Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _alerts.Enqueue(alert);
        return Task.CompletedTask;
    }
}
