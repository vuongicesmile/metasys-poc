using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using ClosedXML.Excel;
using Microsoft.Xrm.Sdk;
using SpoIngestion.Core;

namespace MetasysPoc.Tests;

public sealed class SpoIngestionTests
{
    private static readonly SpoIngestionOptions Options = new()
    {
        SourceNamespace = "spo-test",
        SourceId = "FMC",
        HistoryTtlSeconds = 2_592_000,
        EquipmentTypeChoices = new(StringComparer.OrdinalIgnoreCase) { ["WaterMeter"] = 789100000 }
    };

    [Fact]
    public void Csv_parser_supports_quoted_commas_and_newlines()
    {
        const string input = "id,description\r\n1,\"comma, value\"\r\n2,\"two\nlines\"\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));

        var rows = new TabularParser().Parse(stream, ".csv", Source("building-v1"));

        Assert.Equal(2, rows.Count);
        Assert.Equal("comma, value", rows[0].Values["description"]);
        Assert.Equal("two\nlines", rows[1].Values["description"]);
    }

    [Fact]
    public void Json_parser_requires_an_array_and_preserves_property_names()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("[{\"building_id\":\"BLD001\"}]"));

        var rows = new TabularParser().Parse(stream, "json", Source("building-v1"));

        Assert.Single(rows);
        Assert.Equal("BLD001", rows[0].Values["building_id"]);
    }

    [Fact]
    public void Xlsx_parser_reads_the_registered_sheet()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Buildings");
        sheet.Cell(1, 1).Value = "building_id";
        sheet.Cell(1, 2).Value = "building_name";
        sheet.Cell(2, 1).Value = "BLD001";
        sheet.Cell(2, 2).Value = "Demo";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var rows = new TabularParser().Parse(stream, "xlsx", Source("building-v1") with { Sheet = "Buildings" });

        Assert.Single(rows);
        Assert.Equal("Demo", rows[0].Values["building_name"]);
    }

    [Fact]
    public void Xlsx_parser_rejects_formulas()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Buildings");
        sheet.Cell(1, 1).Value = "building_id";
        sheet.Cell(2, 1).FormulaA1 = "=\"BLD001\"";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var error = Assert.Throws<InvalidDataException>(() =>
            new TabularParser().Parse(stream, "xlsx", Source("building-v1") with { Sheet = "Buildings" }));

        Assert.Contains("formulas", error.Message);
    }

    [Fact]
    public void Building_mapping_uses_the_existing_contract_and_deterministic_identity()
    {
        var row = Row(("building_id", "BLD001"), ("building_name", "Head Office"), ("city", "HCM"));

        var first = new SpoBronzeMapper(Options).Map(Source("building-v1"), [row], DateTime.UtcNow);
        var second = new SpoBronzeMapper(Options).Map(Source("building-v1"), [row], DateTime.UtcNow);

        var entity = Assert.Single(first.Records).Entity;
        Assert.Equal("fmc_bmsbuilding", entity.LogicalName);
        Assert.Equal("BLD001", entity["fmc_buildingcode"]);
        Assert.Equal(entity.Id, Assert.Single(second.Records).Entity.Id);
    }

    [Fact]
    public void Reading_mapping_uses_configured_offset_and_does_not_stamp_sql_columns()
    {
        var row = Row(("electric_meter_id", "EM001"), ("building_id", "BLD001"),
            ("timestamp", "2026-09-14 08:00:00"), ("energy_kwh", "1.23456"),
            ("demand_kw", "2"), ("voltage_v", "380"), ("power_factor", "0.95"));
        var source = Source("electricity-reading-v1") with { TimestampUtcOffset = "+07:00" };

        var result = new SpoBronzeMapper(Options).Map(source, [row], new DateTime(2026, 9, 14, 2, 0, 0, DateTimeKind.Utc));

        Assert.Empty(result.Issues);
        Assert.Equal(8, result.Records.Count);
        var point = result.Records.First(x => x.Kind == BronzeRecordKind.Point).Entity;
        var history = result.Records.First(x => x.Kind == BronzeRecordKind.History).Entity;
        Assert.Equal(new DateTime(2026, 9, 14, 1, 0, 0, DateTimeKind.Utc), point["fmc_lastreadingtime"]);
        Assert.Equal(1.2346m, point["fmc_currentvalue"]);
        Assert.False(point.Attributes.ContainsKey("fmc_lastsqlid"));
        Assert.False(history.Attributes.ContainsKey("fmc_sqlreadingid"));
        Assert.False(history.Attributes.ContainsKey("fmc_sqlingestedat"));
    }

    [Fact]
    public void Expired_reading_keeps_current_point_but_skips_history()
    {
        var row = Row(("water_meter_id", "WM001"), ("building_id", "BLD001"),
            ("timestamp", "2026-01-01T00:00:00Z"), ("consumption_m3", "1"), ("flow_m3h", "2"));

        var result = new SpoBronzeMapper(Options).Map(Source("water-reading-v1"), [row],
            new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, x => Assert.Equal(BronzeRecordKind.Point, x.Kind));
        Assert.Equal(2, result.ExpiredHistory);
    }

    [Fact]
    public void Preview_counts_duplicate_events_once()
    {
        const string csv = "water_meter_id,building_id,timestamp,consumption_m3,flow_m3h\n" +
                           "WM001,BLD001,2026-09-14T00:00:00Z,1,2\n" +
                           "WM001,BLD001,2026-09-14T00:00:00Z,1,2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var options = Options with
        {
            Sources = [new("water", "/Shared Documents/Water/", "water-reading-v1",
                "fmc_bmspoint,fmc_bmsreading", ["csv"])]
        };

        var preview = new SpoPreviewer(new TabularParser(), new SpoBronzeMapper(options)).Preview(
            stream, "/Shared Documents/Water/readings.csv", options,
            new DateTime(2026, 9, 14, 1, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, preview.TargetCounts["fmc_bmspoint"]);
        Assert.Equal(2, preview.TargetCounts["fmc_bmsreading"]);
    }

    [Fact]
    public void Unsupported_equipment_choice_is_a_contract_issue()
    {
        var row = Row(("equipment_id", "EQ001"), ("building_id", "BLD001"), ("equipment_type", "AHU"));

        var result = new SpoBronzeMapper(Options).Map(Source("equipment-v1"), [row], DateTime.UtcNow);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("SPO-CHOICE", issue.Code);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Source_ownership_prefix_rejects_an_unassigned_business_key()
    {
        var row = Row(("building_id", "SQL-BUILDING-001"), ("building_name", "Not owned"));

        var result = new SpoBronzeMapper(Options).Map(
            Source("building-v1") with { OwnedKeyPrefix = "BLD" }, [row], DateTime.UtcNow);

        Assert.Equal("SPO-OWNERSHIP", Assert.Single(result.Issues).Code);
        Assert.Empty(result.Records);
    }

    [Fact]
    public async Task Azurite_manifest_hash_idempotency_and_lease_smoke_test()
    {
        if (Environment.GetEnvironmentVariable("RUN_SPO_AZURITE_TESTS") != "1") return;
        const string storage = "UseDevelopmentStorage=true";
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var options = Options with
        {
            RawContainer = $"raw-{suffix}",
            ControlContainer = $"control-{suffix}",
            QueueName = $"queue-{suffix}",
            Sources = [new("building", "/Shared Documents/FMC-Inbox/Building/", "building-v1",
                "fmc_bmsbuilding", ["csv"])]
        };
        var store = new BlobJobStore(storage, options);
        var raw = new BlobContainerClient(storage, options.RawContainer);
        var queue = new QueueClient(storage, options.QueueName);
        try
        {
            await store.Initialize(CancellationToken.None);
            var bytes = Encoding.UTF8.GetBytes("building_id,building_name\nBLD001,Demo\n");
            const string blobName = "captures/demo.csv";
            await raw.GetBlobClient(blobName).UploadAsync(BinaryData.FromBytes(bytes));
            var request = new SpoCaptureRequest(
                "/Shared Documents/FMC-Inbox/Building/demo.csv", blobName, "etag-1", bytes.Length);

            var first = await store.FinalizeCapture(request, options, CancellationToken.None);
            var replay = await store.FinalizeCapture(request, options, CancellationToken.None);

            Assert.Equal(first.JobId, replay.JobId);
            Assert.Equal("building-v1", first.MappingVersion);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), first.ContentSha256);
            await using var lease = await store.TryLease(first.JobId, CancellationToken.None);
            Assert.NotNull(lease);
            await store.Save(first with { Status = "Processing" }, lease!, CancellationToken.None);
            Assert.Equal("Processing", (await store.Read(first.JobId, CancellationToken.None)).Status);
        }
        finally
        {
            await raw.DeleteIfExistsAsync();
            await new BlobContainerClient(storage, options.ControlContainer).DeleteIfExistsAsync();
            await queue.DeleteIfExistsAsync();
        }
    }

    private static SpoSourceDefinition Source(string mapping) =>
        new("test", "/Shared Documents/Test/", mapping, "target", ["csv", "json", "xlsx"]);

    private static ParsedRow Row(params (string Name, string Value)[] values) =>
        new(1, values.ToDictionary(x => x.Name, x => (string?)x.Value, StringComparer.OrdinalIgnoreCase));
}
