using DataverseSyncWorker.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public static class Verification
{
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + name);
        Console.WriteLine("PASS: " + name);
    }

    public static async Task SelfTest(IServiceProvider services)
    {
        await BmsRelationshipVerification.SelfTest();
        var options = new SyncOptions();
        var mapper = new ReadingMapper(options);
        var row = new BmsReading(1, "WATER-001", "Water", "WaterConsumption", "A", "EQ-A-WM-001", DateTime.UtcNow,
            350.1234m, "m3", "Fake Metasys COV", new DateTime(2026, 9, 7, 14, 0, 0));
        var mapped = mapper.History(row, row.ReadingTime)!;
        Assert(mapper.ReadingId(1) == mapper.ReadingId(1) && mapper.ReadingId(1) != mapper.ReadingId(2), "deterministic distinct IDs");
        Assert((decimal)mapped["fmc_readingvalue"] == 350.1234m, "decimal precision preserved");
        Assert((DateTime)mapped["fmc_sqlingestedat"] == new DateTime(2026,9,7,7,0,0,DateTimeKind.Utc), "SQL local time converted to UTC");
        Assert(mapper.History(row with { ReadingTime = DateTime.UtcNow.AddDays(-31) }, DateTime.UtcNow) is null, "expired history is not reintroduced");
        Assert((int)mapper.History(row, row.ReadingTime.AddSeconds(100))!["ttlinseconds"] == options.HistoryTtlSeconds - 100, "replay does not extend retention");
        Assert(mapper.Validate(row with { ReadingValue = 100000000001m }) is not null, "Dataverse decimal overflow quarantined");

        // Isolated disposable database; no test writes to FM_Central.raw.bms_reading.
        var original = services.GetRequiredService<IConfiguration>().GetConnectionString("Sql")!;
        var dbName = "FM_Central_DataverseTests_" + Guid.NewGuid().ToString("N");
        var cs = new SqlConnectionStringBuilder(original) { InitialCatalog = "master", Pooling = false };
        await using var admin = new SqlConnection(cs.ConnectionString); await admin.OpenAsync();
        using (var create = new SqlCommand($"CREATE DATABASE [{dbName}]", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            cs.InitialCatalog = dbName;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:Sql"] = cs.ConnectionString }).Build();
            var sql = new SqlStore(config, options);
            await using (var c = await sql.Open(CancellationToken.None))
            {
                using var schema = new SqlCommand("""
                    EXEC('CREATE SCHEMA raw');
                    EXEC('CREATE SCHEMA integration');
                    CREATE TABLE raw.bms_building(building_code varchar(50) PRIMARY KEY,name nvarchar(200) NOT NULL,source_building varchar(100) NOT NULL,description nvarchar(2000),source_updated_at datetime2 NOT NULL,ingested_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
                    CREATE TABLE raw.bms_equipment(equipment_code varchar(100) PRIMARY KEY,name nvarchar(200) NOT NULL,equipment_type varchar(50) NOT NULL,building_code varchar(50) NOT NULL,description nvarchar(2000),source_updated_at datetime2 NOT NULL,ingested_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_test_equipment_building FOREIGN KEY(building_code) REFERENCES raw.bms_building(building_code));
                    CREATE TABLE raw.bms_reading(id bigint PRIMARY KEY,object_id varchar(100) NOT NULL,object_name varchar(200),object_type varchar(100),building varchar(100),equipment_code varchar(100),reading_time datetime2 NOT NULL,reading_value decimal(18,4),unit varchar(50),source_system varchar(100) NOT NULL,ingested_at datetime2);
                    INSERT raw.bms_building(building_code,name,source_building,source_updated_at) VALUES('BLDG-A','Building A','Building A',SYSUTCDATETIME());
                    INSERT raw.bms_equipment(equipment_code,name,equipment_type,building_code,source_updated_at) VALUES('EQ-A-WM-001','Main Water Meter A','WaterMeter','BLDG-A',SYSUTCDATETIME());
                    """, c);
                await schema.ExecuteNonQueryAsync();
                var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "sql", "create-dataverse-sync-tables.sql"));
                foreach (var batch in System.Text.RegularExpressions.Regex.Split(migration, @"(?im)^GO\s*$"))
                {
                    var statement = batch.Replace("USE FM_Central;", "");
                    if (string.IsNullOrWhiteSpace(statement)) continue;
                    using var cmd = new SqlCommand(statement, c); await cmd.ExecuteNonQueryAsync();
                }
            }
            var sink = new TestWriter();
            using var logs = LoggerFactory.Create(_ => { });
            var engine = new SyncEngine(sql, mapper, sink, options, logs.CreateLogger<SyncEngine>());
            async Task Exec(string command)
            {
                await using var c = await sql.Open(CancellationToken.None);
                using var cmd = new SqlCommand(command, c); await cmd.ExecuteNonQueryAsync();
            }
            await Exec("INSERT raw.bms_reading VALUES(20,'WATER-001','Water','WaterConsumption','A','EQ-A-WM-001',SYSUTCDATETIME(),350,'m3','Fake Metasys COV',GETDATE());");
            sink.FailAfterHistory = true;
            try { await engine.Run(CancellationToken.None); throw new InvalidOperationException("Expected transport failure"); }
            catch (TimeoutException) { }
            Assert(sink.Buildings.Count == 1 && sink.Equipment.Count == 1,
                "one sync run upserts SQL building and equipment catalogs before readings");
            Assert(((EntityReference)sink.Equipment.Values.Single()["fmc_buildingid"]).Id == sink.Buildings.Keys.Single(),
                "equipment lookup targets the deterministic building ID");
            Assert(((EntityReference)sink.Points.Values.Single()["fmc_equipmentid"]).Id == sink.Equipment.Keys.Single(),
                "point lookup targets the deterministic equipment ID");
            Assert((await sql.Summary(CancellationToken.None)).DeliveredRows == 0, "partial Dataverse failure does not advance SQL delivery");
            sink.FailAfterHistory = false;
            await engine.Run(CancellationToken.None);
            Assert(sink.History.Count == 1 && (await sql.Summary(CancellationToken.None)).DeliveredRows == 1, "replay after partial failure is idempotent");
            Assert((await engine.Run(CancellationToken.None)).Read == 0, "acknowledged row is not resent");
            await Exec("INSERT raw.bms_reading VALUES(10,'WATER-001','Water','WaterConsumption','A','EQ-A-WM-001',DATEADD(minute,-1,SYSUTCDATETIME()),340,'m3','Fake Metasys COV',GETDATE());");
            await engine.Run(CancellationToken.None);
            Assert(sink.History.Count == 2, "late commit below the high watermark is delivered");
            Assert((decimal)sink.Points.Values.Single()["fmc_currentvalue"] == 350m, "old replay cannot regress current value");
            await Exec("INSERT raw.bms_reading VALUES(21,'WATER-002','Water','WaterConsumption','B',NULL,SYSUTCDATETIME(),100000000001,'m3','Fake Metasys COV',GETDATE());");
            var bad = await engine.Run(CancellationToken.None);
            Assert(bad.Quarantined == 1 && (await sql.Summary(CancellationToken.None)).DeadLetterRows == 1, "invalid row recorded in dead-letter");
            await Exec("UPDATE raw.bms_reading SET reading_value=12 WHERE id=21;");
            await sql.Replay(21,CancellationToken.None);
            await engine.Run(CancellationToken.None);
            Assert((await sql.Summary(CancellationToken.None)).PendingRows == 0 && sink.History.Count == 3, "corrected dead-letter can be replayed");
            await using (var first = await sql.Open(CancellationToken.None))
            await using (var second = await sql.Open(CancellationToken.None))
            {
            Assert(await sql.Lock(first,CancellationToken.None) && !await sql.Lock(second,CancellationToken.None), "SQL lock prevents concurrent pipeline writers");
                await sql.Unlock(first);
            }
            await Exec("INSERT raw.bms_reading VALUES(30,'WATER-030','Water','WaterConsumption','C',NULL,SYSUTCDATETIME(),30,'m3','Fake Metasys COV',GETDATE()),(31,'WATER-031','Water','WaterConsumption','C',NULL,SYSUTCDATETIME(),31,'m3','Fake Metasys COV',GETDATE());");
            options.BatchSize = 1;
            var requestSink = new TestRequestStore();
            var command = new CommandProcessor(requestSink, sql, engine, options, logs.CreateLogger<CommandProcessor>());
            var commandResult = await command.TryRun(CancellationToken.None);
            Assert(commandResult.State == "Succeeded" && requestSink.FinalStatus == SyncRequestStatuses.Succeeded,
                $"command request reaches a terminal success state (state={commandResult.State}, final={requestSink.FinalStatus})");
            Assert(requestSink.Delivered == 2 && requestSink.Batches == 2,
                "command request drains multiple batches and publishes deterministic progress");
            var emptySink = new TestRequestStore();
            await new CommandProcessor(emptySink, sql, engine, options, logs.CreateLogger<CommandProcessor>()).TryRun(CancellationToken.None);
            Assert(emptySink.FinalStatus == SyncRequestStatuses.Succeeded && emptySink.Delivered == 0,
                "empty command request succeeds without duplicate delivery");
            await Exec("INSERT raw.bms_reading VALUES(32,'WATER-032','Water','WaterConsumption','C',NULL,SYSUTCDATETIME(),32,'m3','Fake Metasys COV',GETDATE());");
            var cutoff = await sql.MaxId(CancellationToken.None);
            await Exec("INSERT raw.bms_reading VALUES(33,'WATER-033','Water','WaterConsumption','C',NULL,SYSUTCDATETIME(),33,'m3','Fake Metasys COV',GETDATE());");
            await engine.Run(CancellationToken.None, cutoff);
            Assert((await sql.Summary(cutoff, CancellationToken.None)).EligiblePendingRows == 0 &&
                   (await sql.Summary(CancellationToken.None)).PendingRows == 1,
                "request cutoff leaves newer SQL rows for the next request");
            await engine.Run(CancellationToken.None);
            options.BatchSize = 100;
            var historyBeforeCurrentOnly = sink.History.Count;
            options.HistoryEnabled = false;
            await Exec("INSERT raw.bms_reading VALUES(22,'WATER-003','Water','WaterConsumption','C',NULL,SYSUTCDATETIME(),30,'m3','Fake Metasys COV',GETDATE());");
            await engine.Run(CancellationToken.None);
            Assert(sink.History.Count == historyBeforeCurrentOnly && (await sql.Summary(CancellationToken.None)).PendingRows == 0, "current-only mode tracks history separately");
            options.HistoryEnabled = true;
            await engine.Run(CancellationToken.None);
            Assert(sink.History.Count == historyBeforeCurrentOnly + 1 && (await sql.Summary(CancellationToken.None)).PendingRows == 0, "enabling history backfills current-only deliveries");
            Console.WriteLine("All self-tests passed; sink was simulated, not live Dataverse.");
        }
        finally
        {
            // dbName is generated in this method and CREATE succeeded before entering this block.
            using var drop = new SqlCommand($"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];", admin);
            await drop.ExecuteNonQueryAsync();
            Console.WriteLine("Removed isolated test database " + dbName);
        }
    }

    public static async Task Reconcile(IServiceProvider services)
    {
        var sql = services.GetRequiredService<SqlStore>();
        var mapper = services.GetRequiredService<ReadingMapper>();
        var options = services.GetRequiredService<SyncOptions>();
        var client = services.GetRequiredService<DataverseConnection>().Get();
        var summary = await sql.Summary(CancellationToken.None);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(summary));
        await using var c = await sql.Open(CancellationToken.None);
        using var cmd = sql.Command(c, """
            SELECT TOP(25) r.id,r.object_id,r.object_name,r.object_type,r.building,r.equipment_code,r.reading_time,r.reading_value,r.unit,r.source_system,r.ingested_at
            FROM raw.bms_reading r JOIN integration.dataverse_delivery d ON d.pipeline=@pipeline AND d.reading_id=r.id
            WHERE d.history_done=1 ORDER BY r.id DESC;
            """);
        var samples = await SqlStore.Read(cmd,CancellationToken.None);
        var catalog = await sql.ReadCatalog(c, CancellationToken.None);
        foreach (var building in catalog.Buildings)
        {
            var actual = await client.RetrieveAsync("fmc_bmsbuilding", mapper.BuildingId(building.BuildingCode),
                new ColumnSet("fmc_buildingcode"));
            Assert(actual.GetAttributeValue<string>("fmc_buildingcode") == building.BuildingCode,
                $"live building {building.BuildingCode}");
        }
        foreach (var equipment in catalog.Equipment)
        {
            var actual = await client.RetrieveAsync("fmc_bmsequipment", mapper.EquipmentId(equipment.EquipmentCode),
                new ColumnSet("fmc_equipmentcode", "fmc_buildingid"));
            Assert(actual.GetAttributeValue<string>("fmc_equipmentcode") == equipment.EquipmentCode &&
                   actual.GetAttributeValue<EntityReference>("fmc_buildingid")?.Id == mapper.BuildingId(equipment.BuildingCode),
                $"live equipment {equipment.EquipmentCode} -> {equipment.BuildingCode}");
        }
        foreach (var r in samples)
        {
            if (mapper.History(r,DateTime.UtcNow) is null) continue;
            var target = new EntityReference("fmc_bmsreading", new KeyAttributeCollection
            {
                ["fmc_bmsreadingid"] = mapper.ReadingId(r.Id), ["partitionid"] = ReadingMapper.Partition(r.ObjectId)
            });
            var actual = ((RetrieveResponse)await client.ExecuteAsync(new RetrieveRequest
                { Target = target, ColumnSet = new ColumnSet("fmc_sqlreadingid","fmc_readingvalue","fmc_objectid") })).Entity;
            Assert(actual.GetAttributeValue<string>("fmc_sqlreadingid") == r.Id.ToString() &&
                   actual.GetAttributeValue<decimal?>("fmc_readingvalue") == r.ReadingValue &&
                   actual.GetAttributeValue<string>("fmc_objectid") == r.ObjectId, $"live history SQL row {r.Id}");
        }
        Console.WriteLine($"Reconciled up to {samples.Count} history samples. Pending={summary.PendingRows}, dead-letter={summary.DeadLetterRows}. Current state is eventually consistent while source ingestion runs.");
    }

    public static async Task VerifyRelationships(IServiceProvider services)
    {
        var sql = services.GetRequiredService<SqlStore>();
        var mapper = services.GetRequiredService<ReadingMapper>();
        var client = services.GetRequiredService<DataverseConnection>().Get();
        await using var connection = await sql.Open(CancellationToken.None);
        using var cmd = new SqlCommand("""
            SELECT object_id,MAX(equipment_code) equipment_code
            FROM raw.bms_reading WHERE equipment_code IS NOT NULL
            GROUP BY object_id ORDER BY object_id;
            """, connection);
        var mappings = new List<(string ObjectId,string EquipmentCode)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) mappings.Add((reader.GetString(0), reader.GetString(1)));
        var query = new QueryExpression("fmc_bmspoint")
            { ColumnSet = new ColumnSet("fmc_objectid", "fmc_equipmentid") };
        query.Criteria.AddCondition("fmc_objectid", ConditionOperator.In, mappings.Select(m => (object)m.ObjectId).ToArray());
        var equipment = query.AddLink("fmc_bmsequipment", "fmc_equipmentid", "fmc_bmsequipmentid");
        equipment.EntityAlias = "equipment";
        equipment.Columns = new ColumnSet("fmc_equipmentcode", "fmc_buildingid");
        var building = equipment.AddLink("fmc_bmsbuilding", "fmc_buildingid", "fmc_bmsbuildingid");
        building.EntityAlias = "building";
        building.Columns = new ColumnSet("fmc_buildingcode");
        var rows = (await client.RetrieveMultipleAsync(query)).Entities;
        Assert(rows.Count == mappings.Count, $"live joined point count {mappings.Count}");
        foreach (var row in rows)
        {
            var objectId = row.GetAttributeValue<string>("fmc_objectid");
            var expected = mappings.Single(m => m.ObjectId == objectId);
            var equipmentCode = (string)row.GetAttributeValue<AliasedValue>("equipment.fmc_equipmentcode").Value;
            var buildingCode = (string)row.GetAttributeValue<AliasedValue>("building.fmc_buildingcode").Value;
            Assert(equipmentCode == expected.EquipmentCode,
                $"live relationship {objectId} -> {equipmentCode} -> {buildingCode}");
        }
    }

    private sealed class TestWriter : IDataverseWriter
    {
        public Dictionary<Guid,Entity> Buildings { get; } = [];
        public Dictionary<Guid,Entity> Equipment { get; } = [];
        public Dictionary<Guid,Entity> Points { get; } = [];
        public Dictionary<(Guid,string),Entity> History { get; } = [];
        public bool FailAfterHistory { get; set; }
        public Task WriteBuildings(IReadOnlyList<Entity> buildings,CancellationToken ct)
        { foreach(var b in buildings) Buildings[b.Id]=b; return Task.CompletedTask; }
        public Task WriteEquipment(IReadOnlyList<Entity> equipment,CancellationToken ct)
        { foreach(var e in equipment) Equipment[e.Id]=e; return Task.CompletedTask; }
        public Task WritePoints(IReadOnlyList<Entity> points,CancellationToken ct)
        { foreach(var p in points) Points[p.Id]=p; return Task.CompletedTask; }
        public Task WriteHistory(IReadOnlyList<Entity> rows,CancellationToken ct)
        {
            foreach(var r in rows) History[(r.Id,(string)r["partitionid"])]=r;
            if(FailAfterHistory) throw new TimeoutException("Simulated lost response after remote commit");
            return Task.CompletedTask;
        }
    }

    private sealed class TestRequestStore : ISyncRequestStore
    {
        private bool _claimed;
        private SyncRequest _request = new(Guid.NewGuid(), Guid.NewGuid().ToString("D"), null, 0, 0, 0, "1");
        public int? FinalStatus { get; private set; }
        public long Delivered { get; private set; }
        public int Batches { get; private set; }

        public Task<SyncRequest?> Claim(string workerOwner, CancellationToken ct)
        {
            if (_claimed) return Task.FromResult<SyncRequest?>(null);
            _claimed = true;
            return Task.FromResult<SyncRequest?>(_request);
        }
        public Task<SyncRequest> Initialize(SyncRequest request, string workerOwner, long cutoffId,
            CutoffSummary baseline, CancellationToken ct)
        {
            _request = request with
            {
                CutoffId = cutoffId,
                BaselineDelivered = baseline.DeliveredRows,
                BaselineDeadLetters = baseline.DeadLetterRows
            };
            return Task.FromResult(_request);
        }
        public Task Progress(SyncRequest request, string workerOwner, int batches, long delivered,
            long quarantined, CutoffSummary summary, CancellationToken ct)
        { Batches = batches; Delivered = delivered; return Task.CompletedTask; }
        public Task Complete(SyncRequest request, string workerOwner, int status, int batches,
            long delivered, long quarantined, CutoffSummary summary, string? error, CancellationToken ct)
        { FinalStatus = status; Batches = batches; Delivered = delivered; return Task.CompletedTask; }
        public Task Requeue(SyncRequest request, string workerOwner, int batches, long delivered,
            long quarantined, CutoffSummary summary, CancellationToken ct)
        { FinalStatus = SyncRequestStatuses.Queued; Batches = batches; Delivered = delivered; return Task.CompletedTask; }
    }
}
