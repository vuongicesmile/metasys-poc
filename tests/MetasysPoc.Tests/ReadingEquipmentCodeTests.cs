using Dataverse.SyncWorker.Business.Services;
using Dataverse.SyncWorker.DataAccess.Services;
using Dataverse.SyncWorker.Common.Configuration;
using Microsoft.Xrm.Sdk.Metadata;

namespace MetasysPoc.Tests;

public sealed class ReadingEquipmentCodeTests
{
    [Fact]
    public void Equipment_code_is_defined_on_history_as_text_100()
    {
        var column = Assert.Single(DataverseProvisioner.Columns(true),
            attribute => attribute.SchemaName == "fmc_equipmentcode");
        Assert.Equal(100, Assert.IsType<StringAttributeMetadata>(column).MaxLength);
    }

    [Fact]
    public void Fixed_snapshot_keeps_sql_identity_and_equipment_code_without_elastic_fields()
    {
        var mapper = new ReadingMapper(new SyncOptions());
        var time = new DateTime(2026, 9, 9, 17, 0, 0, DateTimeKind.Utc);
        var source = new BmsReading(73911, "TEMP-001", "Room Temperature", "Temperature",
            "Building A", "EQ-A-TS-001", time, 19.7000m, "C", "Fake Metasys COV", time);
        var snapshot = mapper.Snapshot(source);

        Assert.Equal("fmc_bmsreadingsnapshot", snapshot.LogicalName);
        Assert.Equal(mapper.ReadingId(73911), snapshot.Id);
        Assert.Equal("73911", snapshot["fmc_sqlreadingid"]);
        Assert.Equal("EQ-A-TS-001", snapshot["fmc_equipmentcode"]);
        Assert.Equal(time, snapshot["fmc_readingtime"]);
        Assert.False(snapshot.Attributes.ContainsKey("partitionid"));
        Assert.False(snapshot.Attributes.ContainsKey("ttlinseconds"));
    }
}
