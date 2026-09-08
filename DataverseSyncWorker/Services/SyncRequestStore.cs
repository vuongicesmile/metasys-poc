using System.ServiceModel;
using DataverseSyncWorker.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public interface ISyncRequestStore
{
    Task<SyncRequest?> Claim(string workerOwner, CancellationToken ct);
    Task<SyncRequest> Initialize(SyncRequest request, string workerOwner, long cutoffId, CutoffSummary baseline, CancellationToken ct);
    Task Progress(SyncRequest request, string workerOwner, int batches, long delivered, long quarantined, CutoffSummary summary, CancellationToken ct);
    Task Complete(SyncRequest request, string workerOwner, int status, int batches, long delivered, long quarantined, CutoffSummary summary, string? error, CancellationToken ct);
    Task Requeue(SyncRequest request, string workerOwner, int batches, long delivered, long quarantined, CutoffSummary summary, CancellationToken ct);
}

public sealed class SyncRequestStore(DataverseConnection connection, SyncOptions options,
    ILogger<SyncRequestStore> logger)
    : ISyncRequestStore
{
    private static readonly ColumnSet Columns = new(
        "fmc_correlationid", "fmc_requestedcutoffid", "fmc_baselinedelivered",
        "fmc_baselinedeadletters", "fmc_batches", "fmc_status", "fmc_workerowner",
        "fmc_leaseexpiresat", "fmc_startedat");

    public async Task<(Guid Id, bool Created)> Enqueue(string requestedBy, CancellationToken ct)
    {
        var query = new QueryExpression("fmc_syncrequest")
        {
            ColumnSet = new ColumnSet("fmc_syncrequestid"), TopCount = 1
        };
        query.Criteria.AddCondition("fmc_pipeline", ConditionOperator.Equal, options.Pipeline);
        query.Criteria.AddCondition("fmc_status", ConditionOperator.In,
            SyncRequestStatuses.Queued, SyncRequestStatuses.Running);
        query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
        var active = (await connection.Get().RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault();
        if (active is not null) return (active.Id, false);

        var correlation = Guid.NewGuid().ToString("D");
        var id = await connection.Get().CreateAsync(new Entity("fmc_syncrequest")
        {
            ["fmc_name"] = $"SQL Sync {DateTime.UtcNow:O}",
            ["fmc_command"] = "DrainPending",
            ["fmc_pipeline"] = options.Pipeline,
            ["fmc_correlationid"] = correlation,
            ["fmc_requestedby"] = requestedBy,
            ["fmc_status"] = new OptionSetValue(SyncRequestStatuses.Queued)
        }, ct);
        return (id, true);
    }

    public async Task<SyncRequest?> Claim(string workerOwner, CancellationToken ct)
    {
        var active = new FilterExpression(LogicalOperator.Or);
        active.AddCondition("fmc_status", ConditionOperator.Equal, SyncRequestStatuses.Queued);
        var expired = new FilterExpression(LogicalOperator.And);
        expired.AddCondition("fmc_status", ConditionOperator.Equal, SyncRequestStatuses.Running);
        expired.AddCondition("fmc_leaseexpiresat", ConditionOperator.OnOrBefore, DateTime.UtcNow);
        active.AddFilter(expired);

        var query = new QueryExpression("fmc_syncrequest")
        {
            ColumnSet = Columns,
            TopCount = 10
        };
        query.Criteria.AddCondition("fmc_pipeline", ConditionOperator.Equal, options.Pipeline);
        query.Criteria.AddFilter(active);
        query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));

        var candidates = await connection.Get().RetrieveMultipleAsync(query, ct);
        foreach (var candidate in candidates.Entities)
        {
            if (string.IsNullOrWhiteSpace(candidate.RowVersion))
                throw new InvalidOperationException("Sync request optimistic concurrency is unavailable.");
            var update = new Entity("fmc_syncrequest", candidate.Id) { RowVersion = candidate.RowVersion };
            update["fmc_status"] = new OptionSetValue(SyncRequestStatuses.Running);
            update["fmc_workerowner"] = workerOwner;
            update["fmc_heartbeat"] = DateTime.UtcNow;
            update["fmc_leaseexpiresat"] = DateTime.UtcNow.AddSeconds(options.CommandLeaseSeconds);
            update["fmc_attempts"] = candidate.GetAttributeValue<int?>("fmc_attempts").GetValueOrDefault() + 1;
            if (!candidate.Contains("fmc_startedat")) update["fmc_startedat"] = DateTime.UtcNow;
            try
            {
                await connection.Get().ExecuteAsync(new UpdateRequest
                {
                    Target = update,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
                }, ct);
                logger.LogInformation("Claimed sync request {RequestId}", candidate.Id);
                return await Read(candidate.Id, ct);
            }
            catch (FaultException<OrganizationServiceFault> ex) when (IsConcurrencyConflict(ex))
            {
                logger.LogDebug("Sync request {RequestId} was claimed by another worker", candidate.Id);
            }
        }
        return null;
    }

    public async Task<SyncRequest> Initialize(SyncRequest request, string workerOwner,
        long cutoffId, CutoffSummary baseline, CancellationToken ct)
    {
        var values = new Dictionary<string, object?>
        {
            ["fmc_requestedcutoffid"] = cutoffId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fmc_baselinedelivered"] = baseline.DeliveredRows,
            ["fmc_baselinedeadletters"] = baseline.DeadLetterRows,
            ["fmc_pendingafter"] = baseline.PendingRows,
            ["fmc_deadletterafter"] = baseline.DeadLetterRows
        };
        await UpdateOwned(request.Id, workerOwner, values, renewLease: true, ct);
        return await Read(request.Id, ct);
    }

    public Task Progress(SyncRequest request, string workerOwner, int batches,
        long delivered, long quarantined, CutoffSummary summary, CancellationToken ct) =>
        UpdateOwned(request.Id, workerOwner, new Dictionary<string, object?>
        {
            ["fmc_batches"] = batches,
            ["fmc_deliveredrows"] = delivered,
            ["fmc_quarantinedrows"] = quarantined,
            ["fmc_pendingafter"] = summary.PendingRows,
            ["fmc_deadletterafter"] = summary.DeadLetterRows
        }, renewLease: true, ct);

    public Task Complete(SyncRequest request, string workerOwner, int status, int batches,
        long delivered, long quarantined, CutoffSummary summary, string? error, CancellationToken ct) =>
        UpdateOwned(request.Id, workerOwner, new Dictionary<string, object?>
        {
            ["fmc_status"] = new OptionSetValue(status),
            ["fmc_batches"] = batches,
            ["fmc_deliveredrows"] = delivered,
            ["fmc_quarantinedrows"] = quarantined,
            ["fmc_pendingafter"] = summary.PendingRows,
            ["fmc_deadletterafter"] = summary.DeadLetterRows,
            ["fmc_completedat"] = DateTime.UtcNow,
            ["fmc_errormessage"] = error,
            ["fmc_workerowner"] = null,
            ["fmc_leaseexpiresat"] = null
        }, renewLease: false, ct);

    public Task Requeue(SyncRequest request, string workerOwner, int batches,
        long delivered, long quarantined, CutoffSummary summary, CancellationToken ct) =>
        UpdateOwned(request.Id, workerOwner, new Dictionary<string, object?>
        {
            ["fmc_status"] = new OptionSetValue(SyncRequestStatuses.Queued),
            ["fmc_batches"] = batches,
            ["fmc_deliveredrows"] = delivered,
            ["fmc_quarantinedrows"] = quarantined,
            ["fmc_pendingafter"] = summary.PendingRows,
            ["fmc_deadletterafter"] = summary.DeadLetterRows,
            ["fmc_workerowner"] = null,
            ["fmc_leaseexpiresat"] = null
        }, renewLease: false, ct);

    private async Task UpdateOwned(Guid id, string workerOwner,
        IReadOnlyDictionary<string, object?> values, bool renewLease, CancellationToken ct)
    {
        var current = await connection.Get().RetrieveAsync("fmc_syncrequest", id,
            new ColumnSet("fmc_status", "fmc_workerowner"), ct);
        if (current.GetAttributeValue<OptionSetValue>("fmc_status")?.Value != SyncRequestStatuses.Running ||
            !string.Equals(current.GetAttributeValue<string>("fmc_workerowner"), workerOwner, StringComparison.Ordinal))
            throw new InvalidOperationException($"Worker no longer owns sync request {id}.");
        if (string.IsNullOrWhiteSpace(current.RowVersion))
            throw new InvalidOperationException("Sync request optimistic concurrency is unavailable.");

        var update = new Entity("fmc_syncrequest", id) { RowVersion = current.RowVersion };
        foreach (var (name, value) in values) update[name] = value;
        if (renewLease)
        {
            update["fmc_heartbeat"] = DateTime.UtcNow;
            update["fmc_leaseexpiresat"] = DateTime.UtcNow.AddSeconds(options.CommandLeaseSeconds);
        }
        await connection.Get().ExecuteAsync(new UpdateRequest
        {
            Target = update,
            ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
        }, ct);
    }

    private async Task<SyncRequest> Read(Guid id, CancellationToken ct)
    {
        var entity = await connection.Get().RetrieveAsync("fmc_syncrequest", id, Columns, ct);
        long? cutoff = long.TryParse(entity.GetAttributeValue<string>("fmc_requestedcutoffid"), out var parsed)
            ? parsed : null;
        return new SyncRequest(entity.Id,
            entity.GetAttributeValue<string>("fmc_correlationid") ?? entity.Id.ToString("D"),
            cutoff,
            entity.GetAttributeValue<long?>("fmc_baselinedelivered").GetValueOrDefault(),
            entity.GetAttributeValue<long?>("fmc_baselinedeadletters").GetValueOrDefault(),
            entity.GetAttributeValue<int?>("fmc_batches").GetValueOrDefault(),
            entity.RowVersion);
    }

    private static bool IsConcurrencyConflict(FaultException<OrganizationServiceFault> ex) =>
        ex.Detail.ErrorCode is -2147088254 or -2147088253 or -2147088243;
}
