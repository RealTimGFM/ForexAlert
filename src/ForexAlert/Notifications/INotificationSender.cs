using ForexAlert.Domain;

namespace ForexAlert.Notifications;

public interface INotificationSender
{
    Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken);
}
