using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Abstractions;

/// <summary>Runs one serialized delivery batch, optionally bounded by a command cutoff.</summary>
public interface ISyncEngine
{
    Task<BatchResult> Run(CancellationToken ct, long? cutoffId = null);
}
