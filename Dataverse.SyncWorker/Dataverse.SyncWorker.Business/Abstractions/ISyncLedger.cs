using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Abstractions;

/// <summary>The authoritative SQL receipt view used by command orchestration.</summary>
public interface ISyncLedger
{
    Task<long> MaxId(CancellationToken ct);
    Task<CutoffSummary> Summary(long cutoffId, CancellationToken ct);
}
