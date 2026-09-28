using Dataverse.SyncWorker.Common.Configuration;

namespace Dataverse.SyncWorker.Business.Services;

public sealed class RuntimeState
{
    private RuntimeSnapshot _snapshot = new("Starting");
    public RuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public void Set(RuntimeSnapshot value) => Volatile.Write(ref _snapshot, value);
}
