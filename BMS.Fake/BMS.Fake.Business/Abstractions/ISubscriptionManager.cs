using BMS.Ingestion.Domain.Models;
using BMS.Fake.Business.Models;

namespace BMS.Fake.Business.Abstractions;

/// <summary>Business-facing port for publishing COV events to subscribers.</summary>
public interface ISubscriptionManager
{
    SubscriptionResponse Create(IEnumerable<string> objectIds);
    MetasysSubscription? Get(string id);
    void Publish(CovEvent covEvent);
}
