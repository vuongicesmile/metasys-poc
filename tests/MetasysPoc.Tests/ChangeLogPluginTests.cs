using System.Text.Json;
using FMCentralBms.Plugins;
using Microsoft.Xrm.Sdk;
using DataverseSyncWorker.Hosting;
using DataverseSyncWorker.Services;

namespace MetasysPoc.Tests;

public sealed class ChangeLogPluginTests
{
    private static readonly Guid RecordId = Guid.NewGuid();
    private static Entity Row(string? name) => new("fmc_bmsbuilding", RecordId) { ["fmc_name"] = name };
    private static Entity Build(string action, Entity? before, Entity? after) => RecordChangeLog.BuildLog(
        "fmc_bmsbuilding", RecordId, action, before!, after!, Guid.Empty, Guid.Empty, Guid.Empty, DateTime.UtcNow);

    [Fact]
    public void Create_has_after_snapshot_and_no_before()
    {
        var log = Build("Create", null, Row("Building A"));
        Assert.Equal("null", log["fmc_before"]);
        using var after = JsonDocument.Parse((string)log["fmc_after"]);
        Assert.Equal("Building A", after.RootElement.GetProperty("fmc_name").GetString());
        Assert.Equal(RecordId.ToString("D"), log["fmc_recordid"]);
    }

    [Fact]
    public void Delete_keeps_record_name_and_before_without_a_source_lookup()
    {
        var log = Build("Delete", Row("Deleted Building"), null);
        Assert.Equal("Deleted Building", log["fmc_recordname"]);
        Assert.Equal("null", log["fmc_after"]);
        Assert.DoesNotContain(log.Attributes.Values, v => v is EntityReference);
    }

    [Fact]
    public void Update_records_explicit_clear_and_valid_json()
    {
        var before = Row("A\n\"B\\C\tViệt Nam");
        var log = Build("Update", before, Row(null));
        using var json = JsonDocument.Parse((string)log["fmc_before"]);
        Assert.Equal(before["fmc_name"], json.RootElement.GetProperty("fmc_name").GetString());
        Assert.Equal("fmc_name", log["fmc_changedfields"]);
    }

    [Fact]
    public void Noop_and_unselected_fields_do_not_generate_a_log()
    {
        var after = Row("Unchanged");
        after["modifiedon"] = DateTime.UtcNow;
        after["secret"] = "never capture";
        Assert.Null(Build("Update", Row("Unchanged"), after));
    }

    [Fact]
    public void Initiating_actor_and_execution_user_are_distinct()
    {
        var actor = Guid.NewGuid(); var runAs = Guid.NewGuid();
        var log = RecordChangeLog.BuildLog("fmc_bmsbuilding", RecordId, "Create", null!, Row("A"), actor, runAs, Guid.Empty, DateTime.UtcNow);
        Assert.Equal(actor.ToString("D"), log["fmc_userid"]);
        Assert.Equal(runAs.ToString("D"), log["fmc_runasuserid"]);
    }

    [Fact]
    public void Equipment_lookup_change_is_compared_by_id_not_label()
    {
        var id = Guid.NewGuid();
        var before = new Entity("fmc_bmsequipment") { ["fmc_buildingid"] = new EntityReference("fmc_bmsbuilding", id) { Name = "Old label" } };
        var after = new Entity("fmc_bmsequipment") { ["fmc_buildingid"] = new EntityReference("fmc_bmsbuilding", id) { Name = "New label" } };
        Assert.Null(RecordChangeLog.BuildLog("fmc_bmsequipment", RecordId, "Update", before, after, Guid.Empty, Guid.Empty, Guid.Empty, DateTime.UtcNow));
        after["fmc_buildingid"] = new EntityReference("fmc_bmsbuilding", Guid.NewGuid());
        Assert.NotNull(RecordChangeLog.BuildLog("fmc_bmsequipment", RecordId, "Update", before, after, Guid.Empty, Guid.Empty, Guid.Empty, DateTime.UtcNow));
    }

    [Fact]
    public void Step_images_match_plugin_allowlist()
    {
        foreach (var (table, columns) in DataversePluginProvisioner.ChangeLogColumns)
            Assert.Equal(columns.Split(','), RecordChangeLog.Columns(table));
        Assert.Null(RecordChangeLog.Columns("fmc_changelog"));
    }

    [Theory]
    [InlineData("--run-once")]
    [InlineData("--provision")]
    [InlineData("--enqueue")]
    public void Deployment_cannot_start_sync_or_other_mutations(string other)
    {
        Assert.Throws<InvalidOperationException>(() => WorkerCommandLine.Parse(["--deploy-change-log", other]));
    }
}
