using System.Collections.Concurrent;
using ForexAlert.Domain;

namespace ForexAlert.Persistence;

public sealed class InMemoryAlertStateStore : IAlertStateStore
{
    private readonly ConcurrentDictionary<AlertKey, AlertDeliveryState> _state = new();

    public ValueTask<AlertDeliveryState?> GetAsync(AlertKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<AlertDeliveryState?>(_state.TryGetValue(key, out AlertDeliveryState? value) ? value : null);
    }

    public ValueTask SetAsync(
        AlertKey key,
        AlertDeliveryState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state[key] = state with { LastSentUtc = state.LastSentUtc.ToUniversalTime() };
        return ValueTask.CompletedTask;
    }
}
