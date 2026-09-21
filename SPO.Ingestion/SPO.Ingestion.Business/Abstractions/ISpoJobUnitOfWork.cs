using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business.Abstractions;

/// <summary>One leased job. Writes are durable checkpoints, not a cross-system transaction.</summary>
public interface ISpoJobUnitOfWork : IAsyncDisposable
{
    Task Save(SpoJobManifest manifest, CancellationToken ct);
    Task SaveReceipts(IReadOnlyList<RowReceipt> receipts, CancellationToken ct);
    Task SaveReceiptSegment(string segment, IReadOnlyList<RowReceipt> receipts, CancellationToken ct);
}

public interface ISpoJobUnitOfWorkFactory
{
    Task<ISpoJobUnitOfWork?> TryCreate(string jobId, CancellationToken ct);
}
