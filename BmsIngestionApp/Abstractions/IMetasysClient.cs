using BmsIngestionApp.Models;

namespace BmsIngestionApp.Abstractions;

public interface IMetasysClient
{
    Task<MetasysCatalog> ReadCatalogAsync(CancellationToken ct);
    Task<SubscriptionResponse> SubscribeAsync(IEnumerable<string> objectIds, CancellationToken ct);
    IAsyncEnumerable<CovEvent> ReadEventsAsync(string subscriptionId, CancellationToken ct);
}
