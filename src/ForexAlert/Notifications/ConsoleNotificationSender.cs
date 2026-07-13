using ForexAlert.Domain;
using Microsoft.Extensions.Logging;

namespace ForexAlert.Notifications;

public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("{AlertMessage}", AlertMessageFormatter.Format(alert));
        return Task.CompletedTask;
    }
}
