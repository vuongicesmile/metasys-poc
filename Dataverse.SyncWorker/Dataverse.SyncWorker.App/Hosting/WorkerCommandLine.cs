using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Hosting;

public sealed record WorkerCommandLine(string[] CommandArgs, string[] HostArgs, BmsRelationCommand? RelationCommand)
{
    public static WorkerCommandLine Parse(string[] args)
    {
        var (relationCommand, remainingArgs) = BmsRelationCommand.Parse(args);
        args = remainingArgs;
        var commands = new[]
        {
            "--provision", "--register-plugin", "--run-once", "--self-test", "--verify", "--enqueue", "--process-command-once",
            "--spo-ingestion-status", "--provision-spo-ingestion", "--verify-spo-ingestion"
        };
        var hostArgs = args.Where(a => !commands.Contains(a) &&
            !a.StartsWith("--plugin-path=", StringComparison.OrdinalIgnoreCase)).ToArray();
        return new(args, hostArgs, relationCommand);
    }
}
