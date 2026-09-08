using DataverseSyncWorker.Models;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace DataverseSyncWorker.Services;

public sealed record RuntimeSnapshot(string State, DateTime? LastRunAt = null, BatchResult? LastBatch = null,
    string? Error = null, Guid? RequestId = null);
public sealed class RuntimeState
{
    private RuntimeSnapshot _snapshot = new("Starting");
    public RuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public void Set(RuntimeSnapshot value) => Volatile.Write(ref _snapshot, value);
}

public sealed class SyncWorker(SyncEngine engine, CommandProcessor commands, SyncOptions options, RuntimeState status,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Enabled) { status.Set(new("Disabled")); return; }
        if (!options.HasCredentials)
        {
            status.Set(new("AwaitingCredentials", Error: "Configure the Dataverse application identity, then restart."));
            return;
        }
        if (options.ExecutionMode == "CommandDriven")
        {
            await RunCommandDriven(ct);
            return;
        }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await engine.Run(ct);
                status.Set(new(result.Busy ? "Busy" : result.Read == 0 ? "Idle" : "Syncing", DateTime.UtcNow, result));
                if (result.Read == options.BatchSize) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is FaultException<OrganizationServiceFault> && !DataverseWriter.IsTransient(ex))
            {
                status.Set(new("Blocked", DateTime.UtcNow, Error: "Configuration, authentication, schema or permanent Dataverse error. Correct the cause and restart; SQL delivery remains pending."));
                logger.LogWarning("Sync requires attention ({ErrorType}). No automatic retry for permanent failures", ex.GetType().Name);
                return;
            }
            catch (Exception ex)
            {
                // No exception text in public APIs: upstream faults can include request/credential details.
                status.Set(new("Failed", DateTime.UtcNow, Error: "Synchronization failed; delivery remains pending. Check schema, SQL and application permissions."));
                logger.LogWarning("Sync failed ({ErrorType}); delivery remains pending", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }

    private async Task RunCommandDriven(CancellationToken ct)
    {
        status.Set(new("CommandIdle"));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await commands.TryRun(ct);
                status.Set(new(result.State == "Idle" ? "CommandIdle" : result.State,
                    DateTime.UtcNow, RequestId: result.RequestId));
                if (result.Found && result.State is not ("Requeued" or "RequeuedBusy")) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                status.Set(new("CommandFailed", DateTime.UtcNow,
                    Error: "Command polling failed. Check Dataverse connectivity and worker logs."));
                logger.LogWarning("Command polling failed ({ErrorType})", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.CommandPollIntervalSeconds), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }
}
