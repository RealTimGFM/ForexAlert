using System.Collections.Concurrent;
using ForexAlert.Domain;
using ForexAlert.MarketData;

namespace ForexAlert.Tests.MarketData;

public sealed class RequestIdRegistryTests
{
    private static readonly CurrencyPair EurUsd = CurrencyPair.Parse("EUR/USD");
    private static readonly CurrencyPair UsdEur = CurrencyPair.Parse("USD/EUR");

    [Fact]
    public void Register_MapsRequestIdToCompleteSubscriptionDescriptor()
    {
        RequestIdRegistry registry = new();
        TimeSpan interval = TimeSpan.FromMinutes(5);

        SubscriptionDescriptor registered = registry.Register(
            EurUsd,
            UsdEur,
            inverse: true,
            MarketDataRequestKind.Historical,
            interval);

        Assert.True(registered.RequestId > 0);
        Assert.Equal(EurUsd, registered.RequestedPair);
        Assert.Equal(UsdEur, registered.ContractPair);
        Assert.True(registered.IsInverse);
        Assert.Equal(MarketDataRequestKind.Historical, registered.Kind);
        Assert.Equal(interval, registered.BarInterval);

        Assert.True(registry.TryGet(registered.RequestId, out SubscriptionDescriptor? result));
        Assert.Same(registered, result);
    }

    [Fact]
    public void Register_AllocatesUniqueIdsConcurrently()
    {
        const int requestCount = 256;
        RequestIdRegistry registry = new();
        ConcurrentBag<SubscriptionDescriptor> registrations = [];

        Parallel.For(
            0,
            requestCount,
            _ => registrations.Add(registry.Register(
                EurUsd,
                EurUsd,
                inverse: false,
                MarketDataRequestKind.Streaming)));

        SubscriptionDescriptor[] registered = registrations.ToArray();
        Assert.Equal(requestCount, registered.Length);
        Assert.Equal(requestCount, registered.Select(static item => item.RequestId).Distinct().Count());

        SubscriptionDescriptor[] snapshot = registry.Snapshot().ToArray();
        Assert.Equal(requestCount, snapshot.Length);
        Assert.Equal(
            snapshot.Select(static item => item.RequestId).Order().ToArray(),
            snapshot.Select(static item => item.RequestId).ToArray());

        foreach (SubscriptionDescriptor descriptor in registered)
        {
            Assert.True(registry.TryGet(descriptor.RequestId, out SubscriptionDescriptor? mapped));
            Assert.Same(descriptor, mapped);
        }
    }

    [Fact]
    public void Remove_ReturnsMappingAndMakesIdUnavailable()
    {
        RequestIdRegistry registry = new();
        SubscriptionDescriptor registered = registry.Register(
            EurUsd,
            EurUsd,
            inverse: false,
            MarketDataRequestKind.Streaming);

        Assert.True(registry.Remove(registered.RequestId, out SubscriptionDescriptor? removed));
        Assert.Same(registered, removed);
        Assert.False(registry.TryGet(registered.RequestId, out SubscriptionDescriptor? missing));
        Assert.Null(missing);
        Assert.False(registry.Remove(registered.RequestId, out SubscriptionDescriptor? removedAgain));
        Assert.Null(removedAgain);
    }

    [Fact]
    public void Snapshot_IsRequestIdOrderedAndClearRemovesMappingsWithoutReusingIds()
    {
        RequestIdRegistry registry = new();
        SubscriptionDescriptor first = registry.Register(
            EurUsd,
            EurUsd,
            inverse: false,
            MarketDataRequestKind.Streaming);
        SubscriptionDescriptor second = registry.Register(
            EurUsd,
            UsdEur,
            inverse: true,
            MarketDataRequestKind.Historical,
            TimeSpan.FromHours(1));

        Assert.Equal(
            new[] { first.RequestId, second.RequestId },
            registry.Snapshot().Select(static item => item.RequestId).ToArray());

        registry.Clear();

        Assert.Empty(registry.Snapshot());
        Assert.False(registry.TryGet(first.RequestId, out SubscriptionDescriptor? cleared));
        Assert.Null(cleared);

        SubscriptionDescriptor afterClear = registry.Register(
            EurUsd,
            EurUsd,
            inverse: false,
            MarketDataRequestKind.Streaming);
        Assert.True(afterClear.RequestId > second.RequestId);
    }

    [Fact]
    public void LookupAndRemove_ReturnFalseForUnknownId()
    {
        RequestIdRegistry registry = new();

        Assert.False(registry.TryGet(42, out SubscriptionDescriptor? found));
        Assert.Null(found);
        Assert.False(registry.Remove(42, out SubscriptionDescriptor? removed));
        Assert.Null(removed);
    }
}
