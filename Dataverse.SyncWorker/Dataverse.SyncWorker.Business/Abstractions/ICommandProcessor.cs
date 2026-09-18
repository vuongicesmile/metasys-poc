using DataverseSyncWorker.Services;

namespace DataverseSyncWorker.Abstractions;

public interface ICommandProcessor
{
    Task<CommandRunResult> TryRun(CancellationToken ct);
}
