using System.Diagnostics;
using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Services;

public sealed class CommandProcessor(ISyncRequestStore requests, SqlStore sql, SyncEngine engine,
    SyncOptions options, ILogger<CommandProcessor> logger)
{
    private readonly string _workerOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public async Task<CommandRunResult> TryRun(CancellationToken ct)
    {
        var request = await requests.Claim(_workerOwner, ct);
        if (request is null) return new(false, null, "Idle");

        var timer = Stopwatch.StartNew();
        var batches = request.Batches;
        try
        {
            if (request.CutoffId is null)
            {
                var cutoff = await sql.MaxId(ct);
                var baseline = await sql.Summary(cutoff, ct);
                request = await requests.Initialize(request, _workerOwner, cutoff, baseline, ct);
            }
            var cutoffId = request.CutoffId!.Value;

            while (!ct.IsCancellationRequested)
            {
                var summary = await sql.Summary(cutoffId, ct);
                var delivered = Math.Max(0, summary.DeliveredRows - request.BaselineDelivered);
                var quarantined = Math.Max(0, summary.DeadLetterRows - request.BaselineDeadLetters);
                if (summary.EligiblePendingRows == 0)
                {
                    var status = summary.DeadLetterRows == 0
                        ? SyncRequestStatuses.Succeeded : SyncRequestStatuses.CompletedWithIssues;
                    await requests.Complete(request, _workerOwner, status, batches, delivered,
                        quarantined, summary, null, ct);
                    logger.LogInformation("Completed sync request {RequestId} at cutoff {CutoffId}", request.Id, cutoffId);
                    return new(true, request.Id, status == SyncRequestStatuses.Succeeded ? "Succeeded" : "CompletedWithIssues");
                }
                if (timer.Elapsed >= TimeSpan.FromMinutes(options.CommandMaxDurationMinutes))
                {
                    await requests.Requeue(request, _workerOwner, batches, delivered, quarantined, summary, ct);
                    logger.LogInformation("Requeued sync request {RequestId} after bounded execution window", request.Id);
                    return new(true, request.Id, "Requeued");
                }

                var batch = await engine.Run(ct, cutoffId);
                if (batch.Busy)
                {
                    await requests.Requeue(request, _workerOwner, batches, delivered, quarantined, summary, ct);
                    return new(true, request.Id, "RequeuedBusy");
                }
                if (batch.Read > 0) batches++;
                var after = await sql.Summary(cutoffId, ct);
                delivered = Math.Max(0, after.DeliveredRows - request.BaselineDelivered);
                quarantined = Math.Max(0, after.DeadLetterRows - request.BaselineDeadLetters);
                await requests.Progress(request, _workerOwner, batches, delivered, quarantined, after, ct);

                if (batch.Read == 0 && after.EligiblePendingRows > 0)
                    throw new InvalidOperationException("Eligible SQL rows remain but no batch was read.");
            }
            throw new OperationCanceledException(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Sync request {RequestId} failed ({ErrorType}); SQL receipts remain authoritative",
                request.Id, ex.GetType().Name);
            try
            {
                var cutoff = request.CutoffId ?? await sql.MaxId(CancellationToken.None);
                var summary = await sql.Summary(cutoff, CancellationToken.None);
                await requests.Complete(request, _workerOwner, SyncRequestStatuses.Failed, batches,
                    Math.Max(0, summary.DeliveredRows - request.BaselineDelivered),
                    Math.Max(0, summary.DeadLetterRows - request.BaselineDeadLetters), summary,
                    "Synchronization failed. Check worker logs, SQL, Dataverse schema and application permissions.",
                    CancellationToken.None);
            }
            catch (Exception updateEx)
            {
                logger.LogWarning("Could not publish failure state for request {RequestId} ({ErrorType})",
                    request.Id, updateEx.GetType().Name);
            }
            return new(true, request.Id, "Failed");
        }
    }
}

public sealed record CommandRunResult(bool Found, Guid? RequestId, string State);
