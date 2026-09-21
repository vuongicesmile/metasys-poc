using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business.Abstractions;

/// <summary>
/// Kho bền vững của pipeline SPO.
///
/// Implementation hiện tại dùng Blob Storage cho raw file, manifest và receipt;
/// Queue chỉ đánh thức worker. Không dùng EF Core ở boundary này vì job state
/// cần Blob lease để claim/retry an toàn và không có relational database làm nguồn sự thật.
/// </summary>
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
