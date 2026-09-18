using System.Globalization;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;
using System.Text.Json;
using DataverseSyncWorker.Models;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public interface IBmsRelationStore
{
    Task<Entity?> Find(string table, string key, string value, string[] columns);
    Task<(Entity Record, string Outcome)> EnsureCatalog(Entity desired, string key);
    Task UpdatePointLookup(Entity point, Guid equipmentId);
}

public sealed class DataverseBmsRelationStore(ServiceClient client) : IBmsRelationStore
{
    public async Task<Entity?> Find(string table, string key, string value, string[] columns)
    {
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), TopCount = 2 };
        query.Criteria.AddCondition(key, ConditionOperator.Equal, value);
        var rows = (await client.RetrieveMultipleAsync(query)).Entities;
        if (rows.Count > 1) throw new InvalidOperationException($"Ambiguous identity: {table}.{key}={value}.");
        var row = rows.SingleOrDefault();
        if (row is not null && string.IsNullOrWhiteSpace(row.RowVersion) && row.Contains("versionnumber"))
            row.RowVersion = Convert.ToString(row["versionnumber"], CultureInfo.InvariantCulture);
        return row;
    }

    public async Task<(Entity Record, string Outcome)> EnsureCatalog(Entity desired, string key)
    {
        var columns = desired.Attributes.Keys.Append("statecode").ToArray();
        var value = desired.GetAttributeValue<string>(key);
        var existing = await Find(desired.LogicalName, key, value, columns);
        if (existing is not null)
        {
            BmsRelationshipSeeder.RequireCatalogMatch(existing, desired);
            return (existing, "Unchanged");
        }
        // Active alternate keys enforce uniqueness. Create then re-read on uncertain/duplicate
        // outcomes avoids overwriting a concurrently created catalog row with a blind upsert.
        string outcome;
        try { desired.Id = await client.CreateAsync(desired); outcome = "Created"; }
        catch (Exception ex) when (ex is FaultException<OrganizationServiceFault> or TimeoutException or HttpRequestException)
        {
            existing = await Find(desired.LogicalName, key, value, columns);
            if (existing is null) throw;
            BmsRelationshipSeeder.RequireCatalogMatch(existing, desired);
            return (existing, "ReusedAfterUncertainCreate");
        }
        existing = await Find(desired.LogicalName, key, value, columns)
            ?? throw new InvalidOperationException($"Created catalog record cannot be read: {value}.");
        BmsRelationshipSeeder.RequireCatalogMatch(existing, desired);
        return (existing, outcome);
    }

    public async Task UpdatePointLookup(Entity point, Guid equipmentId)
    {
        if (string.IsNullOrWhiteSpace(point.RowVersion))
            throw new InvalidOperationException($"Missing row version for point {point.Id}; refusing an unguarded update.");
        await client.ExecuteAsync(new UpdateRequest
        {
            Target = new Entity(DataverseProvisioner.PointTable, point.Id)
            {
                RowVersion = point.RowVersion,
                ["fmc_equipmentid"] = new EntityReference(DataverseProvisioner.EquipmentTable, equipmentId)
            },
            ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
        });
    }
}

public sealed record BmsSeedAction(string Table, string Key, string Action, Guid? ExistingId, Guid? BeforeLookup, string? TargetCode);
public sealed record BmsSeedPlan(string Version, BmsSeedAction[] Actions, string[] Errors);
public sealed record BmsPointBefore(Guid Id, string ObjectId, Guid? EquipmentId, string? Building,
    decimal? CurrentValue, string? LastSqlId, DateTime? LastReadingTime, string? Unit);
public sealed record BmsSeedReceiptEntry(string Table, string Key, string Outcome, Guid? Id,
    Guid? BeforeLookup = null, Guid? AfterLookup = null, BmsPointBefore? PointBefore = null);
public sealed class BmsSeedReceipt
{
    public string Version { get; init; } = "";
    public string ManifestSha256 { get; init; } = "";
    public Guid OrganizationId { get; init; }
    public string SourceId { get; init; } = "";
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    public string Status { get; set; } = "Running";
    public string? Error { get; set; }
    public List<BmsSeedReceiptEntry> Entries { get; } = [];
}

public sealed class BmsRelationshipSeeder(IBmsRelationStore store, SyncOptions options)
{
    internal static readonly string[] PointColumns = ["fmc_objectid", "fmc_building", "fmc_equipmentid", "fmc_currentvalue", "fmc_lastsqlid", "fmc_lastreadingtime", "fmc_unit", "versionnumber"];
    private static readonly string[] BuildingColumns = ["fmc_buildingcode", "fmc_name", "fmc_sourcebuilding", "fmc_description", "statecode"];
    private static readonly string[] EquipmentColumns = ["fmc_equipmentcode", "fmc_name", "fmc_equipmenttype", "fmc_buildingid", "fmc_description", "statecode"];

    internal static Entity BuildingRecord(BuildingSeed b) => new(DataverseProvisioner.BuildingTable)
    {
        ["fmc_buildingcode"] = b.Code, ["fmc_name"] = b.Name,
        ["fmc_sourcebuilding"] = b.SourceBuilding, ["fmc_description"] = b.Description
    };
    internal static Entity EquipmentRecord(EquipmentSeed e, Guid buildingId) => new(DataverseProvisioner.EquipmentTable)
    {
        ["fmc_equipmentcode"] = e.Code, ["fmc_name"] = e.Name,
        ["fmc_equipmenttype"] = new OptionSetValue(BmsRelationManifest.TypeValue(e.Type)),
        ["fmc_buildingid"] = new EntityReference(DataverseProvisioner.BuildingTable, buildingId),
        ["fmc_description"] = e.Description
    };

    internal static void RequireCatalogMatch(Entity actual, Entity desired)
    {
        if (actual.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new InvalidOperationException($"Catalog record is not Active: {actual.LogicalName}/{actual.Id}.");
        foreach (var (key, value) in desired.Attributes)
        {
            actual.Attributes.TryGetValue(key, out var current);
            var equal = (current, value) switch
            {
                (EntityReference a, EntityReference b) => a.LogicalName == b.LogicalName && a.Id == b.Id,
                (OptionSetValue a, OptionSetValue b) => a.Value == b.Value,
                _ => Equals(current, value)
            };
            if (!equal) throw new InvalidOperationException($"Catalog conflict: {actual.LogicalName}/{actual.Id}/{key}. Existing data is preserved; review the manifest.");
        }
    }

    private void RequirePointMatch(Entity? point, PointMappingSeed mapping, BuildingSeed building, Guid? equipmentId)
    {
        if (point is null) throw new InvalidOperationException($"Missing existing BMS Point: {mapping.ObjectId}; seed does not create or sync points.");
        if (point.Id != new ReadingMapper(options).PointId(mapping.ObjectId) ||
            point.GetAttributeValue<string>("fmc_objectid") != mapping.ObjectId)
            throw new InvalidOperationException($"Point identity conflict: {mapping.ObjectId}.");
        if (point.GetAttributeValue<string>("fmc_building") != building.SourceBuilding)
            throw new InvalidOperationException($"Building mismatch for {mapping.ObjectId}: expected source building {building.SourceBuilding}.");
        var current = point.GetAttributeValue<EntityReference>("fmc_equipmentid");
        if (current is not null && (current.LogicalName != DataverseProvisioner.EquipmentTable || current.Id != equipmentId))
            throw new InvalidOperationException($"Point already assigned to another equipment: {mapping.ObjectId}. Explicit reassignment is outside this seed command.");
        if (current is null && string.IsNullOrWhiteSpace(point.RowVersion))
            throw new InvalidOperationException($"Point row version unavailable: {mapping.ObjectId}.");
    }

    public async Task<BmsSeedPlan> Plan(BmsRelationManifest manifest)
    {
        manifest.Validate(options.SourceId);
        var actions = new List<BmsSeedAction>();
        var errors = new List<string>();
        var buildings = new Dictionary<string, Entity?>();
        var equipment = new Dictionary<string, Entity?>();
        foreach (var b in manifest.Buildings)
        {
            var record = await store.Find(DataverseProvisioner.BuildingTable, "fmc_buildingcode", b.Code, BuildingColumns);
            buildings.Add(b.Code, record);
            try { if (record is not null) RequireCatalogMatch(record, BuildingRecord(b)); }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
            actions.Add(new(DataverseProvisioner.BuildingTable, b.Code, record is null ? "Create" : "Unchanged", record?.Id, null, null));
        }
        foreach (var e in manifest.Equipment)
        {
            var record = await store.Find(DataverseProvisioner.EquipmentTable, "fmc_equipmentcode", e.Code, EquipmentColumns);
            equipment.Add(e.Code, record);
            try { if (record is not null) RequireCatalogMatch(record, EquipmentRecord(e, buildings[e.BuildingCode]?.Id ?? Guid.Empty)); }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
            actions.Add(new(DataverseProvisioner.EquipmentTable, e.Code, record is null ? "Create" : "Unchanged", record?.Id,
                record?.GetAttributeValue<EntityReference>("fmc_buildingid")?.Id, e.BuildingCode));
        }
        foreach (var p in manifest.PointMappings)
        {
            var eq = manifest.Equipment.Single(e => e.Code == p.EquipmentCode);
            var building = manifest.Buildings.Single(b => b.Code == eq.BuildingCode);
            var point = await store.Find(DataverseProvisioner.PointTable, "fmc_objectid", p.ObjectId, PointColumns);
            try { RequirePointMatch(point, p, building, equipment[eq.Code]?.Id); }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
            var current = point?.GetAttributeValue<EntityReference>("fmc_equipmentid")?.Id;
            actions.Add(new(DataverseProvisioner.PointTable, p.ObjectId,
                current is not null && current == equipment[eq.Code]?.Id ? "Unchanged" : "SetLookup", point?.Id, current, eq.Code));
        }
        return new(manifest.Version, actions.ToArray(), errors.ToArray());
    }

    public async Task<BmsSeedReceipt> Apply(BmsRelationManifest manifest, string receiptPath)
    {
        var plan = await Plan(manifest);
        if (plan.Errors.Length > 0) throw new InvalidOperationException("Seed preflight failed; no writes made:\n" + string.Join("\n", plan.Errors));
        var receipt = new BmsSeedReceipt
        {
            Version = manifest.Version, OrganizationId = options.ExpectedOrganizationId, SourceId = options.SourceId,
            ManifestSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, BmsRelationManifest.Json))))
        };
        receiptPath = Path.GetFullPath(receiptPath);
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        // Reserve a new receipt before any cloud write. Never overwrite another run's receipt.
        await using (var output = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write))
            await JsonSerializer.SerializeAsync(output, receipt, BmsRelationManifest.Json);
        async Task Save()
        {
            var temporary = receiptPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                await JsonSerializer.SerializeAsync(stream, receipt, BmsRelationManifest.Json);
                stream.Flush(flushToDisk: true);
            }
            // Both files belong to this run in the same directory. Retain the previous
            // receipt until its complete replacement is ready (including Pending intent).
            File.Move(temporary, receiptPath, overwrite: true);
        }
        try
        {
            var buildingIds = new Dictionary<string, Guid>();
            var equipmentIds = new Dictionary<string, Guid>();
            foreach (var b in manifest.Buildings)
            {
                receipt.Entries.Add(new(DataverseProvisioner.BuildingTable, b.Code, "Pending", null));
                await Save();
                var result = await store.EnsureCatalog(BuildingRecord(b), "fmc_buildingcode");
                buildingIds.Add(b.Code, result.Record.Id);
                receipt.Entries[^1] = new(DataverseProvisioner.BuildingTable, b.Code, result.Outcome, result.Record.Id);
                await Save();
            }
            foreach (var e in manifest.Equipment)
            {
                receipt.Entries.Add(new(DataverseProvisioner.EquipmentTable, e.Code, "Pending", null, AfterLookup: buildingIds[e.BuildingCode]));
                await Save();
                var result = await store.EnsureCatalog(EquipmentRecord(e, buildingIds[e.BuildingCode]), "fmc_equipmentcode");
                equipmentIds.Add(e.Code, result.Record.Id);
                receipt.Entries[^1] = new(DataverseProvisioner.EquipmentTable, e.Code, result.Outcome, result.Record.Id,
                    AfterLookup: buildingIds[e.BuildingCode]);
                await Save();
            }
            foreach (var p in manifest.PointMappings)
            {
                var eqSeed = manifest.Equipment.Single(e => e.Code == p.EquipmentCode);
                var buildingSeed = manifest.Buildings.Single(b => b.Code == eqSeed.BuildingCode);
                // Recheck both parents and the point immediately before a guarded patch.
                var building = await store.Find(DataverseProvisioner.BuildingTable, "fmc_buildingcode", buildingSeed.Code, BuildingColumns)
                    ?? throw new InvalidOperationException("Building disappeared during seed.");
                RequireCatalogMatch(building, BuildingRecord(buildingSeed));
                var equipment = await store.Find(DataverseProvisioner.EquipmentTable, "fmc_equipmentcode", eqSeed.Code, EquipmentColumns)
                    ?? throw new InvalidOperationException("Equipment disappeared during seed.");
                RequireCatalogMatch(equipment, EquipmentRecord(eqSeed, building.Id));
                if (equipment.Id != equipmentIds[eqSeed.Code]) throw new InvalidOperationException("Equipment identity changed during seed.");
                var point = await store.Find(DataverseProvisioner.PointTable, "fmc_objectid", p.ObjectId, PointColumns);
                RequirePointMatch(point, p, buildingSeed, equipment.Id);
                var previous = point!.GetAttributeValue<EntityReference>("fmc_equipmentid")?.Id;
                var snapshot = new BmsPointBefore(point.Id, p.ObjectId, previous, point.GetAttributeValue<string>("fmc_building"),
                    point.GetAttributeValue<decimal?>("fmc_currentvalue"), point.GetAttributeValue<string>("fmc_lastsqlid"),
                    point.GetAttributeValue<DateTime?>("fmc_lastreadingtime"), point.GetAttributeValue<string>("fmc_unit"));
                receipt.Entries.Add(new(DataverseProvisioner.PointTable, p.ObjectId, previous == equipment.Id ? "Unchanged" : "Pending",
                    point.Id, previous, equipment.Id, snapshot));
                await Save();
                if (previous != equipment.Id)
                {
                    await store.UpdatePointLookup(point, equipment.Id);
                    receipt.Entries[^1] = receipt.Entries[^1] with { Outcome = "LookupSet" };
                    await Save();
                }
            }
            var after = await Plan(manifest);
            if (after.Errors.Length > 0 || after.Actions.Any(a => a.Action != "Unchanged"))
                throw new InvalidOperationException("Seed post-verification failed: " + string.Join("; ", after.Errors));
            receipt.Status = "Succeeded";
        }
        catch (Exception ex)
        {
            receipt.Status = "Failed"; receipt.Error = ex.Message;
            throw;
        }
        finally
        {
            receipt.FinishedAtUtc = DateTime.UtcNow;
            await Save();
            Console.WriteLine($"Seed receipt: {receiptPath} ({receipt.Status}).");
        }
        return receipt;
    }
}
