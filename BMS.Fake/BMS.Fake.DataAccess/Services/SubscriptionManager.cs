using System.Collections.Concurrent;
using BMS.Ingestion.Domain.Models;
using BMS.Fake.Business.Abstractions;
using BMS.Fake.Business.Models;

namespace BMS.Fake.DataAccess.Services;

/// <summary>In-memory subscription adapter backed by channels.</summary>
public sealed class SubscriptionManager : ISubscriptionManager
{
    private readonly ConcurrentDictionary<string, MetasysSubscription> _subscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private int _nextId;

    public SubscriptionResponse Create(IEnumerable<string> objectIds)
    {
        var id = $"SUB-{Interlocked.Increment(ref _nextId):D3}";
        var subscription = new MetasysSubscription
        {
            Id = id,
            ObjectIds = objectIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

        _subscriptions[id] = subscription;
        return new SubscriptionResponse { SubscriptionId = id };
    }

    public MetasysSubscription? Get(string id) =>
        _subscriptions.TryGetValue(id, out var subscription) ? subscription : null;

    public void Publish(CovEvent covEvent)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (subscription.ObjectIds.Contains(covEvent.ObjectId))
                subscription.Events.Writer.TryWrite(covEvent);
        }
    }
}
