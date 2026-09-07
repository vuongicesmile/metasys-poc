using System.Collections.Concurrent;
using System.Threading.Channels;
using FakeMetasysApi.Models;

namespace FakeMetasysApi.Services;

public sealed class MetasysSubscription
{
    public required string Id { get; init; }
    public required HashSet<string> ObjectIds { get; init; }
    public Channel<CovEvent> Events { get; } = Channel.CreateUnbounded<CovEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
}

public sealed class SubscriptionManager
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
            {
                subscription.Events.Writer.TryWrite(covEvent);
            }
        }
    }
}
