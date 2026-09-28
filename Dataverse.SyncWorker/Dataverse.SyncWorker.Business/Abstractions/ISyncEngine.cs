using Dataverse.SyncWorker.Common.Configuration;

namespace Dataverse.SyncWorker.Business.Abstractions;

/// <summary>Runs one serialized delivery batch, optionally bounded by a command cutoff.</summary>
public interface ISyncEngine
{
    Task<BatchResult> Run(CancellationToken ct, long? cutoffId = null);
}
