using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;

namespace DataverseSyncWorker.Hosting;

// Command dispatch is a composition boundary. Resolve only the services needed
// by the selected maintenance command; none of these paths start hosted workers.
public sealed class WorkerCommandDispatcher(IServiceProvider services, SyncOptions options)
{
    public async Task<bool> Execute(WorkerCommandLine commandLine)
    {
        var args = commandLine.CommandArgs;
        var relationCommand = commandLine.RelationCommand;
        if (args.Contains("--deploy-change-log"))
        {
            var path = args.SingleOrDefault(a => a.StartsWith("--plugin-path=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]
                ?? ResolveDefaultPluginPath();
            DataversePluginProvisioner.ValidateChangeLogAssembly(path);
            await services.GetRequiredService<DataverseProvisioner>().ProvisionChangeLog();
            await services.GetRequiredService<DataversePluginProvisioner>().RegisterChangeLog(path);
            await services.GetRequiredService<DataverseProvisioner>().PrintChangeLogStatus();
            return true;
        }
        if (args.Contains("--change-log-status"))
        {
            await services.GetRequiredService<DataverseProvisioner>().PrintChangeLogStatus();
            return true;
        }
        // Maintenance exits before app.RunAsync: it never starts the hosted sync worker.
        if (relationCommand is not null)
        {
            if (relationCommand.Mode == "--self-test-bms-relations")
            {
                await BmsRelationshipVerification.SelfTest();
                return true;
            }
            var manifest = BmsRelationManifest.Load(relationCommand.ManifestPath, options.SourceId);
            var connection = services.GetRequiredService<DataverseConnection>();
            var provisioner = services.GetRequiredService<DataverseProvisioner>();
            if (relationCommand.Mode == "--bms-relations-status")
                await provisioner.PrintBmsRelationStatus(manifest);
            else if (relationCommand.Mode == "--provision-bms-relations")
            {
                await provisioner.PrintBmsRelationStatus(manifest);
                await provisioner.ProvisionBmsRelations();
            }
            else
            {
                await provisioner.ReadBmsRelationMetadata(true);
                var seeder = services.GetRequiredService<BmsRelationshipSeeder>();
                if (relationCommand.Mode == "--verify-bms-relations")
                {
                    await provisioner.VerifyBmsRelationUi();
                    await Verification.VerifyRelationships(services);
                }
                else
                {
                    var plan = await seeder.Plan(manifest);
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(plan, BmsRelationManifest.Json));
                    if (plan.Errors.Length > 0) throw new InvalidOperationException("Seed preflight failed; no writes made.");
                    if (relationCommand.Apply)
                    {
                        var receiptPath = relationCommand.ReceiptPath ?? Path.Combine(Environment.CurrentDirectory,
                            ".artifacts", "bms-relations", "receipts", $"seed-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
                        var receipt = await seeder.Apply(manifest, receiptPath);
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(receipt, BmsRelationManifest.Json));
                    }
                    else Console.WriteLine("DRY RUN: no cloud writes. Use --apply to apply this manifest.");
                }
            }
            return true;
        }

        if (args.Contains("--self-test")) { await Verification.SelfTest(services); return true; }
        if (args.Contains("--spo-ingestion-status"))
        {
            await services.GetRequiredService<DataverseProvisioner>().PrintSpoIngestionStatus();
            return true;
        }
        if (args.Contains("--provision-spo-ingestion"))
        {
            await services.GetRequiredService<DataverseProvisioner>().ProvisionSpoIngestion();
            return true;
        }
        if (args.Contains("--verify-spo-ingestion"))
        {
            await services.GetRequiredService<DataverseProvisioner>().VerifySpoIngestion();
            return true;
        }
        if (args.Contains("--spo-change-status"))
        {
            await services.GetRequiredService<DataverseProvisioner>().PrintSpoChangeStatus();
            return true;
        }
        if (args.Contains("--provision-spo-changes"))
        {
            await services.GetRequiredService<DataverseProvisioner>().ProvisionSpoChanges();
            return true;
        }
        if (args.Contains("--verify-spo-changes"))
        {
            await services.GetRequiredService<DataverseProvisioner>().VerifySpoChanges();
            return true;
        }
        if (args.Contains("--provision-building-code"))
        {
            await services.GetRequiredService<DataverseProvisioner>().ProvisionBuildingCode();
            return true;
        }
        if (args.Contains("--provision")) { await services.GetRequiredService<DataverseProvisioner>().Run(); return true; }
        if (args.Contains("--register-plugin"))
        {
            var pluginPath = args.FirstOrDefault(a => a.StartsWith("--plugin-path=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]
                ?? ResolveDefaultPluginPath();
            await services.GetRequiredService<DataversePluginProvisioner>().Register(pluginPath);
            return true;
        }
        if (args.Contains("--verify")) { await Verification.Reconcile(services); return true; }
        if (args.Contains("--enqueue"))
        {
            if (!options.HasCredentials) throw new InvalidOperationException("Dataverse credentials required.");
            var queued = await services.GetRequiredService<SyncRequestStore>().Enqueue(Environment.UserName, CancellationToken.None);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { requestId = queued.Id, created = queued.Created }));
            return true;
        }
        if (args.Contains("--process-command-once"))
        {
            if (!options.HasCredentials) throw new InvalidOperationException("Dataverse credentials required.");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                await services.GetRequiredService<CommandProcessor>().TryRun(CancellationToken.None)));
            return true;
        }
        if (args.Contains("--run-once"))
        {
            if (!options.HasCredentials) throw new InvalidOperationException("Dataverse credentials required.");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await services.GetRequiredService<SyncEngine>().Run(CancellationToken.None)));
            return true;
        }
        return false;
    }

    private static string ResolveDefaultPluginPath()
    {
        var relative = Path.Combine("Dataverse.Plugin", "FMCentralBms.Plugins", "bin", "Release",
            "net48", "FMCentralBms.Plugins.dll");
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relative)),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", relative)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
