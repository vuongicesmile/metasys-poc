using BMS.Ingestion.Domain.Models;

namespace BMS.Ingestion.Business.Abstractions;

/// <summary>Business-facing port for the BMS source; HTTP and SSE details stay in DataAccess.</summary>
public interface IMetasysClient
{
    Task<MetasysCatalog> ReadCatalogAsync(CancellationToken ct);
    Task<SubscriptionResponse> SubscribeAsync(IEnumerable<string> objectIds, CancellationToken ct);
    IAsyncEnumerable<CovEvent> ReadEventsAsync(string subscriptionId, CancellationToken ct);
}
