using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.Common;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.DataAccess;

public sealed class DataverseBronzeWriter(ServiceClient client, SpoIngestionOptions options) : ISpoBronzeWriter
{
    private const int DuplicateRecord = unchecked((int)0x80040237);
    public async Task<BronzeWriteResult> Write(IReadOnlyList<BronzeRecord> records, CancellationToken cancellationToken)
    {
        if (!client.IsReady) throw new InvalidOperationException("Dataverse client is not ready: " + client.LastError);
        var receipts = new List<RowReceipt>();
        foreach (var item in records.Where(x => x.Kind == BronzeRecordKind.Building))
            receipts.Add(await UpsertStandard(item, "fmc_buildingcode", cancellationToken));
        foreach (var item in records.Where(x => x.Kind == BronzeRecordKind.Equipment))
        {
            if (item.ParentIdentity is not null)
                item.Entity["fmc_buildingid"] = new EntityReference("fmc_bmsbuilding",
                    await ResolveRequired("fmc_bmsbuilding", "fmc_buildingcode", item.ParentIdentity, cancellationToken));
            receipts.Add(await UpsertStandard(item, "fmc_equipmentcode", cancellationToken));
        }

        // One file can contain many readings for a point. Only its latest event is a current-state write.
        var latestPoints = records.Where(x => x.Kind == BronzeRecordKind.Point)
            .GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.EventTimeUtc).ThenByDescending(x => x.SourceOrdinal).First()).ToArray();
        var equipmentIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in latestPoints)
        {
            if (point.ParentIdentity is not null)
            {
                if (!equipmentIds.TryGetValue(point.ParentIdentity, out var equipmentId))
                {
                    equipmentId = await ResolveRequired("fmc_bmsequipment", "fmc_equipmentcode",
                        point.ParentIdentity, cancellationToken);
                    equipmentIds.Add(point.ParentIdentity, equipmentId);
                }
                point.Entity["fmc_equipmentid"] = new EntityReference("fmc_bmsequipment", equipmentId);
            }
            receipts.Add(await WritePoint(point, cancellationToken));
        }

        var history = records.Where(x => x.Kind == BronzeRecordKind.History).ToArray();
        foreach (var batch in history.Chunk(options.BatchSize))
        {
            await Execute(new OrganizationRequest("UpsertMultiple")
            {
                ["Targets"] = new EntityCollection(batch.Select(x => x.Entity).ToList()) { EntityName = "fmc_bmsreading" }
            }, cancellationToken);
            receipts.AddRange(batch.Select(x => new RowReceipt(x.SourceOrdinal, x.Identity, "fmc_bmsreading", "Delivered",
                $"id={x.Entity.Id};partition={x.PartitionId}")));
        }
        return new(receipts.Count(x => x.Outcome == "Delivered"), receipts.Count(x => x.Outcome != "Delivered"), receipts);
    }

    private async Task<RowReceipt> UpsertStandard(BronzeRecord record, string keyColumn, CancellationToken ct)
    {
        var keyValue = record.Entity[keyColumn];
        var query = new QueryExpression(record.Entity.LogicalName)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 2
        };
        query.Criteria.AddCondition(keyColumn, ConditionOperator.Equal, keyValue);
        var matches = (await client.RetrieveMultipleAsync(query, ct)).Entities;
        if (matches.Count > 1) throw new InvalidOperationException($"Duplicate alternate key {record.Entity.LogicalName}.{keyColumn}={keyValue}.");
        if (matches.Count == 1) record.Entity.Id = matches[0].Id;
        await Execute(new UpsertRequest { Target = record.Entity }, ct);
        return new(record.SourceOrdinal, record.Identity, record.Entity.LogicalName, "Delivered", record.Entity.Id.ToString());
    }

    private async Task<Guid> ResolveRequired(string table, string keyColumn, string keyValue, CancellationToken ct)
    {
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false), TopCount = 2 };
        query.Criteria.AddCondition(keyColumn, ConditionOperator.Equal, keyValue);
        var matches = (await client.RetrieveMultipleAsync(query, ct)).Entities;
        if (matches.Count > 1) throw new InvalidOperationException($"Duplicate alternate key {table}.{keyColumn}={keyValue}.");
        if (matches.Count == 0)
            throw new SpoDependencyException($"WaitingDependency: {table}.{keyColumn}={keyValue} does not exist yet.");
        return matches[0].Id;
    }

    private async Task<RowReceipt> WritePoint(BronzeRecord record, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var query = new QueryExpression("fmc_bmspoint")
            {
                ColumnSet = new ColumnSet("fmc_lastreadingtime", "fmc_sourcesystem", "versionnumber"),
                TopCount = 2
            };
            query.Criteria.AddCondition("fmc_objectid", ConditionOperator.Equal, record.Identity);
            var matches = (await client.RetrieveMultipleAsync(query, ct)).Entities;
            if (matches.Count > 1) throw new InvalidOperationException($"Duplicate point Object ID: {record.Identity}.");
            if (matches.Count == 1)
            {
                var currentSource = matches[0].GetAttributeValue<string>("fmc_sourcesystem");
                if (!string.Equals(currentSource, "SharePoint", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"SourceOwnershipConflict: point '{record.Identity}' is owned by '{currentSource ?? "unknown"}'.");
                var current = matches[0].GetAttributeValue<DateTime?>("fmc_lastreadingtime");
                if (current is not null && current.Value.ToUniversalTime() > record.EventTimeUtc!.Value)
                    return new(record.SourceOrdinal, record.Identity, "fmc_bmspoint", "SkippedOlderCurrent");
                record.Entity.Id = matches[0].Id;
                record.Entity.RowVersion = matches[0].RowVersion;
                try
                {
                    await Execute(new UpdateRequest { Target = record.Entity, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }, ct);
                    return new(record.SourceOrdinal, record.Identity, "fmc_bmspoint", "Delivered", record.Entity.Id.ToString());
                }
                catch (FaultException<OrganizationServiceFault> fault) when (fault.Detail.ErrorCode == -2147088254 && attempt < 4) { }
            }
            else
            {
                try
                {
                    await Execute(new CreateRequest { Target = record.Entity }, ct);
                    return new(record.SourceOrdinal, record.Identity, "fmc_bmspoint", "Delivered", record.Entity.Id.ToString());
                }
                catch (FaultException<OrganizationServiceFault> fault) when (fault.Detail.ErrorCode == DuplicateRecord && attempt < 4) { }
            }
        }
        throw new InvalidOperationException($"Point concurrency retries exhausted: {record.Identity}.");
    }

    private async Task Execute(OrganizationRequest request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await client.ExecuteAsync(request, ct); return; }
            catch (Exception ex) when (attempt < 4 && IsTransient(ex))
            {
                var delay = ex is FaultException<OrganizationServiceFault> fault &&
                    fault.Detail.ErrorDetails.TryGetValue("Retry-After", out var retry) && retry is TimeSpan serverDelay
                    ? serverDelay : TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                await Task.Delay(delay, ct);
            }
        }
    }

    internal static bool IsTransient(Exception ex) => ex is HttpRequestException or TimeoutException ||
        ex is FaultException<OrganizationServiceFault> fault &&
        (fault.Detail.ErrorDetails.Contains("Retry-After") || fault.Detail.ErrorCode is -2147015902 or -2147015903 or -2147015898);
}
