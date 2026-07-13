using ForexAlert.Domain;
using Microsoft.Extensions.Logging;

namespace ForexAlert.Notifications;

public sealed class NullNotificationSender(ILogger<NullNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogDebug("Notification discarded by null provider for {Rule} {Symbol}", alert.RuleName, alert.CanonicalSymbol);
        return Task.CompletedTask;
    }
}
