using ForexAlert.Domain;
using Microsoft.Extensions.Logging;

namespace ForexAlert.Notifications;

public sealed class DryRunNotificationSender(ILogger<DryRunNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("DRY RUN - external notification suppressed. {AlertMessage}", AlertMessageFormatter.Format(alert));
        return Task.CompletedTask;
    }
}
