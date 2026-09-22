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
            "--spo-ingestion-status", "--provision-spo-ingestion", "--verify-spo-ingestion",
            "--deploy-change-log", "--change-log-status"
        };
        if (args.Contains("--deploy-change-log") || args.Contains("--change-log-status"))
        {
            if (relationCommand is not null || args.Count(a => commands.Contains(a)) != 1 ||
                args.Any(a => !commands.Contains(a) && !a.StartsWith("--plugin-path=", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Change log commands must run alone, optionally with --plugin-path=<signed DLL>.");
        }
        var hostArgs = args.Where(a => !commands.Contains(a) &&
            !a.StartsWith("--plugin-path=", StringComparison.OrdinalIgnoreCase)).ToArray();
        return new(args, hostArgs, relationCommand);
    }
}
