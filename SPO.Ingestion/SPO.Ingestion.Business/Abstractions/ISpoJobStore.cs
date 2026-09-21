using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business.Abstractions;

public interface ISpoJobStore
{
    Task Initialize(CancellationToken cancellationToken);
    Task<SpoJobManifest> Read(string jobId, CancellationToken cancellationToken);
    Task<Stream> OpenRaw(SpoJobManifest job, CancellationToken cancellationToken);
    Task<JobLease?> TryLease(string jobId, CancellationToken cancellationToken);
    Task Save(SpoJobManifest manifest, CancellationToken cancellationToken);
    Task Save(SpoJobManifest manifest, JobLease lease, CancellationToken cancellationToken);
    Task SaveReceipts(string jobId, IReadOnlyList<RowReceipt> receipts, CancellationToken cancellationToken);
    Task SaveReceiptSegment(string jobId, string segment, IReadOnlyList<RowReceipt> receipts, CancellationToken cancellationToken);
    IAsyncEnumerable<SpoJobManifest> ReadyJobs(CancellationToken cancellationToken);
}

public interface JobLease : IAsyncDisposable
{
    string LeaseId { get; }
}
