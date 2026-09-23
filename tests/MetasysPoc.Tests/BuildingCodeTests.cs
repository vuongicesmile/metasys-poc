using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
using Microsoft.Xrm.Sdk.Metadata;

namespace MetasysPoc.Tests;

public sealed class BuildingCodeTests
{
    [Fact]
    public void Building_code_schema_is_limited_to_current_points_not_history()
    {
        var code = Assert.Single(DataverseProvisioner.Columns(false),
            column => column.SchemaName == "fmc_buildingcode");
        Assert.Equal(50, Assert.IsType<StringAttributeMetadata>(code).MaxLength);
        Assert.DoesNotContain(DataverseProvisioner.Columns(true),
            column => column.SchemaName == "fmc_buildingcode");
    }

    [Fact]
    public void Normalized_code_preserves_legacy_building_and_point_identity()
    {
        var mapper = new ReadingMapper(new SyncOptions());
        var time = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        var row = new BmsReading(9007199254740993, "P1", "Point", "Temperature",
            "Legacy Building Name", "E1", time, 12.3456m, "C", "Fake Metasys COV", time);
        var original = mapper.Point(row);
        var normalized = mapper.Point(row, "BLD-A");

        Assert.Equal(original.Id, normalized.Id);
        Assert.Equal("Legacy Building Name", normalized["fmc_building"]);
        Assert.Equal("BLD-A", normalized["fmc_buildingcode"]);
        Assert.Equal("9007199254740993", normalized["fmc_lastsqlid"]);
        Assert.Equal(12.3456m, normalized["fmc_currentvalue"]);
        Assert.False(original.Attributes.ContainsKey("fmc_buildingcode"));
        Assert.False(mapper.Point(row, " ").Attributes.ContainsKey("fmc_buildingcode"));
        Assert.False(mapper.History(row, time)!.Attributes.ContainsKey("fmc_buildingcode"));
    }
}
