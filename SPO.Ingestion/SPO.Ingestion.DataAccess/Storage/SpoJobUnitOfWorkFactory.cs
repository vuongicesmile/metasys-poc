using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.DataAccess;

public sealed class SpoJobUnitOfWorkFactory(ISpoJobStore store) : ISpoJobUnitOfWorkFactory
{
    public async Task<ISpoJobUnitOfWork?> TryCreate(string jobId, CancellationToken ct)
    {
        var lease = await store.TryLease(jobId, ct);
        return lease is null ? null : new Session(store, jobId, lease);
    }

    private sealed class Session(ISpoJobStore store, string jobId, JobLease lease) : ISpoJobUnitOfWork
    {
        public Task Save(SpoJobManifest manifest, CancellationToken ct)
        {
            if (manifest.JobId != jobId) throw new InvalidOperationException("Manifest does not belong to this leased job.");
            return store.Save(manifest, lease, ct);
        }
        public Task SaveReceipts(IReadOnlyList<RowReceipt> receipts, CancellationToken ct) => store.SaveReceipts(jobId, receipts, ct);
        public Task SaveReceiptSegment(string segment, IReadOnlyList<RowReceipt> receipts, CancellationToken ct) => store.SaveReceiptSegment(jobId, segment, receipts, ct);
        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}
