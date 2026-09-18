using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Abstractions;

public interface ISyncRequestStore
{
    Task<SyncRequest?> Claim(string workerOwner, CancellationToken ct);
    Task<SyncRequest> Initialize(SyncRequest request, string workerOwner, long cutoffId, CutoffSummary baseline, CancellationToken ct);
    Task Progress(SyncRequest request, string workerOwner, int batches, long delivered, long quarantined, CutoffSummary summary, CancellationToken ct);
    Task Complete(SyncRequest request, string workerOwner, int status, int batches, long delivered, long quarantined, CutoffSummary summary, string? error, CancellationToken ct);
    Task Requeue(SyncRequest request, string workerOwner, int batches, long delivered, long quarantined, CutoffSummary summary, CancellationToken ct);
}
