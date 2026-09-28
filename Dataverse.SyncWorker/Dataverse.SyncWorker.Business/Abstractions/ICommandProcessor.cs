using Dataverse.SyncWorker.Business.Services;

namespace Dataverse.SyncWorker.Business.Abstractions;

public interface ICommandProcessor
{
    Task<CommandRunResult> TryRun(CancellationToken ct);
}
