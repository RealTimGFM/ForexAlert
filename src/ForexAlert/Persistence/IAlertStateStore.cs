using ForexAlert.Domain;

namespace ForexAlert.Persistence;

public interface IAlertStateStore
{
    ValueTask<AlertDeliveryState?> GetAsync(AlertKey key, CancellationToken cancellationToken);

    ValueTask SetAsync(AlertKey key, AlertDeliveryState state, CancellationToken cancellationToken);
}
