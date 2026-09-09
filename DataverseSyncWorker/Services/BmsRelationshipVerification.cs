using System.Text.Json;
using System.Xml.Linq;
using DataverseSyncWorker.Models;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public static class BmsRelationshipVerification
{
    public static async Task Verify(ServiceClient client, BmsRelationshipSeeder seeder, BmsRelationManifest manifest)
    {
        var plan = await seeder.Plan(manifest);
        if (plan.Errors.Length > 0 || plan.Actions.Any(a => a.Action != "Unchanged"))
            throw new InvalidOperationException("Relationship seed verification failed:\n" + JsonSerializer.Serialize(plan, BmsRelationManifest.Json));
        var q = new QueryExpression(DataverseProvisioner.PointTable)
            { ColumnSet = new ColumnSet("fmc_objectid", "fmc_building", "fmc_equipmentid", "fmc_currentvalue", "fmc_lastsqlid") };
        q.Criteria.AddCondition("fmc_objectid", ConditionOperator.In, manifest.PointMappings.Select(p => (object)p.ObjectId).ToArray());
        var equipment = q.AddLink(DataverseProvisioner.EquipmentTable, "fmc_equipmentid", "fmc_bmsequipmentid");
        equipment.EntityAlias = "equipment";
        equipment.Columns = new ColumnSet("fmc_equipmentcode");
        var building = equipment.AddLink(DataverseProvisioner.BuildingTable, "fmc_buildingid", "fmc_bmsbuildingid");
        building.EntityAlias = "building";
        building.Columns = new ColumnSet("fmc_buildingcode");
        var rows = (await client.RetrieveMultipleAsync(q)).Entities;
        if (rows.Count != manifest.PointMappings.Length) throw new InvalidOperationException("Joined query did not return all mapped points.");
        foreach (var row in rows)
        {
            var objectId = row.GetAttributeValue<string>("fmc_objectid");
            var expected = manifest.PointMappings.Single(p => p.ObjectId == objectId);
            var expectedEquipment = manifest.Equipment.Single(e => e.Code == expected.EquipmentCode);
            var equipmentCode = (string)row.GetAttributeValue<AliasedValue>("equipment.fmc_equipmentcode").Value;
            var buildingCode = (string)row.GetAttributeValue<AliasedValue>("building.fmc_buildingcode").Value;
            if (equipmentCode != expected.EquipmentCode || buildingCode != expectedEquipment.BuildingCode)
                throw new InvalidOperationException($"Joined relationship mismatch: {objectId}.");
            Console.WriteLine($"PASS: {objectId} -> {equipmentCode} -> {buildingCode} (point {row.Id}).");
        }
        var inventory = new QueryExpression(DataverseProvisioner.PointTable)
        {
            ColumnSet = new ColumnSet("fmc_equipmentid"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        long total = 0, unassigned = 0;
        while (true)
        {
            var page = await client.RetrieveMultipleAsync(inventory);
            total += page.Entities.Count;
            unassigned += page.Entities.Count(e => !e.Contains("fmc_equipmentid"));
            if (!page.MoreRecords) break;
            inventory.PageInfo.PageNumber++;
            inventory.PageInfo.PagingCookie = page.PagingCookie;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            observedAtUtc = DateTime.UtcNow, manifest.Version,
            fixtureBuildings = manifest.Buildings.Length, fixtureEquipment = manifest.Equipment.Length,
            mappedFixturePoints = rows.Count, totalPoints = total, unassignedPoints = unassigned,
            verification = "Catalog values, five-point identity/mapping, joined standard-table query and paged point inventory. No SQL/history writes."
        }, BmsRelationManifest.Json));
    }

    public static async Task SelfTest()
    {
        static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("BMS RELATIONS TEST FAILED: " + name);
            Console.WriteLine("PASS: " + name);
        }
        static void Reject(Action action, string name)
        {
            try { action(); }
            catch (InvalidOperationException) { Console.WriteLine("PASS: " + name); return; }
            throw new InvalidOperationException("BMS RELATIONS TEST FAILED: " + name);
        }
        var options = new SyncOptions();
        var manifest = BmsRelationManifest.Load(Path.Combine(AppContext.BaseDirectory, "config", "bms-relations.seed.json"), options.SourceId);
        Reject(() => (manifest with { PointMappings = [manifest.PointMappings[0], manifest.PointMappings[0]] }).Validate(options.SourceId), "duplicate Object IDs rejected");
        Reject(() => (manifest with { Equipment = [manifest.Equipment[0] with { BuildingCode = "MISSING" }] }).Validate(options.SourceId), "missing manifest parent rejected");
        Reject(() => (manifest with { SourceId = "OTHER" }).Validate(options.SourceId), "SourceId mismatch rejected");
        Reject(() => BmsRelationCommand.Parse(["--apply"]), "apply without a maintenance command cannot start the host");
        Reject(() => BmsRelationCommand.Parse(["--seed-bms-relations", "--run-once"]), "seed cannot be combined with sync");
        Reject(() => BmsRelationCommand.Parse(["--seed-bms-relations", "--apply", "--dry-run"]), "conflicting write flags rejected");
        Reject(() => BmsRelationCommand.Parse(["--seed-bms-relations", "--aply"]), "misspelled apply flag rejected");
        Check(BmsRelationCommand.Parse(["--seed-bms-relations"]).Command is { Apply: false }, "seed defaults to dry-run");
        var testRoot = Path.Combine(Environment.CurrentDirectory, ".artifacts", "bms-relations", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        string Receipt(string name) => Path.Combine(testRoot, name + ".json");
        var fake = new MemoryStore(manifest, options);
        var seeder = new BmsRelationshipSeeder(fake, options);
        var preview = await seeder.Plan(manifest);
        Check(preview.Errors.Length == 0 && preview.Actions.Count(a => a.Action == "Create") == 7 && fake.Writes == 0,
            "dry-run plans 3 buildings, 4 equipment and 5 lookups without writes");
        var before = fake.Points.ToDictionary(p => p.Id, p => p.GetAttributeValue<decimal>("fmc_currentvalue"));
        var receipt = await seeder.Apply(manifest, Receipt("first"));
        Check(receipt.Status == "Succeeded" && fake.PointUpdates == 5 && fake.Rows.Count == 12, "first seed creates catalog and maps existing points only");
        Check(fake.Points.All(p => p.GetAttributeValue<decimal>("fmc_currentvalue") == before[p.Id] &&
            p.GetAttributeValue<string>("fmc_lastsqlid") == "9007199254740993"), "lookup patches preserve reading values and bigint SQL ID text");
        var writes = fake.Writes;
        var second = await seeder.Apply(manifest, Receipt("second"));
        Check(fake.Writes == writes && second.Entries.All(e => e.Outcome == "Unchanged"), "repeat seed performs zero writes");
        var rig = fake.Rows.Single(e => e.LogicalName == DataverseProvisioner.EquipmentTable && e.GetAttributeValue<string>("fmc_equipmentcode") == "EQ-TEST-RIG-001");
        Check(fake.Points.Count(p => p.GetAttributeValue<EntityReference>("fmc_equipmentid")?.Id == rig.Id) == 2, "two existing points share one TestRig");

        foreach (var invalid in new[] { "missing", "building", "identity", "assigned" })
        {
            var invalidStore = new MemoryStore(manifest, options);
            var last = invalidStore.Points.Last();
            if (invalid == "missing") invalidStore.Rows.Remove(last);
            if (invalid == "building") last["fmc_building"] = "Different Building";
            if (invalid == "identity") last.Id = Guid.NewGuid();
            if (invalid == "assigned") last["fmc_equipmentid"] = new EntityReference(DataverseProvisioner.EquipmentTable, Guid.NewGuid());
            try { await new BmsRelationshipSeeder(invalidStore, options).Apply(manifest, Receipt("invalid-" + invalid)); }
            catch (InvalidOperationException) { }
            Check(invalidStore.Writes == 0 && !File.Exists(Receipt("invalid-" + invalid)), $"{invalid} point aborts the whole seed before writes");
        }
        var partial = new MemoryStore(manifest, options) { FailAfterPointUpdates = 2 };
        try { await new BmsRelationshipSeeder(partial, options).Apply(manifest, Receipt("partial")); }
        catch (TimeoutException) { }
        Check(partial.PointUpdates == 2 && File.ReadAllText(Receipt("partial")).Contains("Failed", StringComparison.Ordinal), "partial failure retains completed mappings and a failed receipt");
        partial.FailAfterPointUpdates = null;
        await new BmsRelationshipSeeder(partial, options).Apply(manifest, Receipt("recovery"));
        Check(partial.Rows.Count == 12 && partial.PointUpdates == 5, "retry after partial failure completes without duplicates");
        var catalogFailure = new MemoryStore(manifest, options) { FailAfterCatalogCreates = 2 };
        try { await new BmsRelationshipSeeder(catalogFailure, options).Apply(manifest, Receipt("catalog-partial")); }
        catch (TimeoutException) { }
        Check(catalogFailure.Rows.Count == 7 && catalogFailure.PointUpdates == 0, "catalog failure leaves existing points untouched");
        catalogFailure.FailAfterCatalogCreates = null;
        await new BmsRelationshipSeeder(catalogFailure, options).Apply(manifest, Receipt("catalog-recovery"));
        Check(catalogFailure.Rows.Count == 12 && catalogFailure.PointUpdates == 5, "retry recovers a partially created building catalog");
        var conflict = fake.Rows.Single(r => r.Id == rig.Id);
        conflict["fmc_name"] = "User renamed this equipment";
        var conflicted = await seeder.Plan(manifest);
        Check(conflicted.Errors.Length > 0 && fake.Writes == writes, "catalog edits are reported instead of overwritten");
        var concurrent = new MemoryStore(manifest, options) { SimulateConcurrentEdit = true };
        try { await new BmsRelationshipSeeder(concurrent, options).Apply(manifest, Receipt("concurrent")); }
        catch (InvalidOperationException) { }
        Check(concurrent.PointUpdates == 0 && concurrent.Points.First().GetAttributeValue<decimal>("fmc_currentvalue") == 99m,
            "concurrent point edit rejects stale lookup patch and preserves the other writer");
        var originalForm = "<form><tabs><tab name='existing'><columns><column><sections><section><rows><row><cell><control id='fmc_name' datafieldname='fmc_name'/></cell></row></rows></section></sections></column></columns></tab></tabs></form>";
        var formId = Guid.NewGuid(); var viewId = Guid.NewGuid();
        var form = DataverseProvisioner.BuildRelationForm(originalForm, DataverseProvisioner.EquipmentTable, formId, viewId);
        var repeated = DataverseProvisioner.BuildRelationForm(form, DataverseProvisioner.EquipmentTable, formId, viewId);
        Check(XNode.DeepEquals(XElement.Parse(form), XElement.Parse(repeated)) &&
            XElement.Parse(form).Descendants("control").Count(c => (string?)c.Attribute("datafieldname") == "fmc_name") == 1,
            "form customization preserves existing controls and is idempotent");
        Check(XElement.Parse(form).Descendants("RelationshipName").Single().Value == DataverseProvisioner.EquipmentRelationship,
            "equipment subgrid targets the actual proposed point relationship");
        var mappedPoint = new ReadingMapper(options).Point(new BmsReading(1, "WATER-001", "Water", "WaterConsumption", "Building A", "EQ-A-WM-001", DateTime.UtcNow, 1.1234m, "m3", "Fake Metasys COV", null));
        Check(mappedPoint.GetAttributeValue<EntityReference>("fmc_equipmentid")?.Id == new ReadingMapper(options).EquipmentId("EQ-A-WM-001"),
            "ordinary sync payload carries the deterministic equipment lookup");
        Console.WriteLine($"BMS relationship self-test completed. Receipts: {testRoot}. No SQL or Dataverse connection was made.");
    }

    private sealed class MemoryStore : IBmsRelationStore
    {
        public List<Entity> Rows { get; } = [];
        public IEnumerable<Entity> Points => Rows.Where(r => r.LogicalName == DataverseProvisioner.PointTable);
        public int Writes { get; private set; }
        public int PointUpdates { get; private set; }
        public int? FailAfterPointUpdates { get; set; }
        public int? FailAfterCatalogCreates { get; set; }
        public bool SimulateConcurrentEdit { get; set; }

        public MemoryStore(BmsRelationManifest manifest, SyncOptions options)
        {
            foreach (var p in manifest.PointMappings)
            {
                var equipment = manifest.Equipment.Single(e => e.Code == p.EquipmentCode);
                var building = manifest.Buildings.Single(b => b.Code == equipment.BuildingCode);
                Rows.Add(new Entity(DataverseProvisioner.PointTable, new ReadingMapper(options).PointId(p.ObjectId))
                {
                    RowVersion = "1", ["fmc_objectid"] = p.ObjectId, ["fmc_building"] = building.SourceBuilding,
                    ["fmc_currentvalue"] = 25.1234m, ["fmc_lastsqlid"] = "9007199254740993",
                    ["fmc_lastreadingtime"] = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc), ["fmc_unit"] = "C"
                });
            }
        }
        private static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
            foreach (var (key, value) in row.Attributes) copy[key] = value;
            return copy;
        }
        public Task<Entity?> Find(string table, string key, string value, string[] columns)
        {
            var row = Rows.SingleOrDefault(r => r.LogicalName == table && r.GetAttributeValue<string>(key) == value);
            return Task.FromResult(row is null ? null : Copy(row));
        }
        public Task<(Entity Record, string Outcome)> EnsureCatalog(Entity desired, string key)
        {
            var row = Rows.SingleOrDefault(r => r.LogicalName == desired.LogicalName && r.GetAttributeValue<string>(key) == desired.GetAttributeValue<string>(key));
            if (row is not null)
            {
                BmsRelationshipSeeder.RequireCatalogMatch(row, desired);
                return Task.FromResult((Copy(row), "Unchanged"));
            }
            if (FailAfterCatalogCreates is { } limit && Writes == limit) throw new TimeoutException("Simulated catalog create failure.");
            row = Copy(desired); row.Id = Guid.NewGuid(); row["statecode"] = new OptionSetValue(0);
            Rows.Add(row); Writes++;
            return Task.FromResult((Copy(row), "Created"));
        }
        public Task UpdatePointLookup(Entity point, Guid equipmentId)
        {
            if (FailAfterPointUpdates is { } limit && PointUpdates == limit) throw new TimeoutException("Simulated partial failure.");
            var row = Rows.Single(r => r.Id == point.Id);
            if (SimulateConcurrentEdit)
            {
                row.RowVersion = "2"; row["fmc_currentvalue"] = 99m;
                SimulateConcurrentEdit = false;
            }
            if (row.RowVersion != point.RowVersion) throw new InvalidOperationException("Simulated row-version conflict.");
            row["fmc_equipmentid"] = new EntityReference(DataverseProvisioner.EquipmentTable, equipmentId);
            row.RowVersion = (int.Parse(row.RowVersion!) + 1).ToString();
            Writes++; PointUpdates++;
            return Task.CompletedTask;
        }
    }
}
