using System.Collections.Concurrent;
using ForexAlert.Domain;

namespace ForexAlert.MarketData;

public sealed class RequestIdRegistry
{
    private readonly ConcurrentDictionary<int, SubscriptionDescriptor> _requests = new();
    private int _nextRequestId;

    public SubscriptionDescriptor Register(
        CurrencyPair requestedPair,
        CurrencyPair contractPair,
        bool inverse,
        MarketDataRequestKind kind,
        TimeSpan? barInterval = null)
    {
        int requestId = Interlocked.Increment(ref _nextRequestId);
        SubscriptionDescriptor descriptor = new(
            requestId,
            requestedPair,
            contractPair,
            inverse,
            kind,
            barInterval);
        if (!_requests.TryAdd(requestId, descriptor))
        {
            throw new InvalidOperationException("The request-ID allocator produced a duplicate identifier.");
        }

        return descriptor;
    }

    public bool TryGet(int requestId, out SubscriptionDescriptor? descriptor) =>
        _requests.TryGetValue(requestId, out descriptor);

    public bool Remove(int requestId, out SubscriptionDescriptor? descriptor) =>
        _requests.TryRemove(requestId, out descriptor);

    public IReadOnlyList<SubscriptionDescriptor> Snapshot() => _requests.Values.OrderBy(static item => item.RequestId).ToArray();

    public void Clear() => _requests.Clear();
}
