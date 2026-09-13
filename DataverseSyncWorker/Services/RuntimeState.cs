using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Services;

public sealed record RuntimeSnapshot(string State, DateTime? LastRunAt = null, BatchResult? LastBatch = null,
    string? Error = null, Guid? RequestId = null);
public sealed class RuntimeState
{
    private RuntimeSnapshot _snapshot = new("Starting");
    public RuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public void Set(RuntimeSnapshot value) => Volatile.Write(ref _snapshot, value);
}
