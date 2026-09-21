using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Abstractions;

/// <summary>Exclusive batch session. Remote writes are not rolled back; acknowledgements remain per row.</summary>
public interface ISyncBatchUnitOfWork : IAsyncDisposable
{
    Task<List<BmsReading>> ReadBatch(CancellationToken ct, long? cutoffId = null);
    Task<BmsReading> Latest(string objectId, CancellationToken ct);
    Task Ack(BmsReading row, CancellationToken ct);
    Task Quarantine(BmsReading row, string reason, CancellationToken ct);
}

public interface ISyncBatchUnitOfWorkFactory
{
    Task<ISyncBatchUnitOfWork?> TryCreate(CancellationToken ct);
}
