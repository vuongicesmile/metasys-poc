using System.Globalization;
using FMCentralBms.Plugins;
using Microsoft.Xrm.Sdk;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace MetasysPoc.Tests;

public sealed class FmReadingEvidenceTests
{
    private static Entity Reading() => new(S.ReadingTable, Guid.NewGuid()) {
        ["fmc_objectid"] = "AHU-01-TEMP", ["fmc_objectname"] = "Supply air",
        ["fmc_equipmentcode"] = "AHU-01", ["fmc_building"] = "Library",
        ["fmc_readingvalue"] = -1.2345m, ["fmc_unit"] = "C",
        ["fmc_readingtime"] = new DateTime(2026, 10, 1, 3, 15, 0, DateTimeKind.Unspecified),
        ["fmc_sqlreadingid"] = "9223372036854775807", ["fmc_externalkey"] = "FMC:9223372036854775807" };

    [Fact] public void Snapshot_preserves_bigint_four_decimals_and_utc_independent_of_culture()
    {
        var reading = Reading(); var actor = Guid.NewGuid(); var original = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            var snapshot = FmReadingEvidence.Snapshot(reading, actor, DateTime.UtcNow);
            Assert.Contains("Value: -1.2345 C", snapshot);
            Assert.Contains("SQL reading ID: 9223372036854775807", snapshot);
            Assert.Contains("2026-10-01 03:15:00 UTC", snapshot);
            Assert.Contains(reading.Id.ToString(), snapshot); Assert.Contains(actor.ToString(), snapshot);
        } finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact] public void Capture_uses_source_and_same_lookup_does_not_refresh_evidence()
    {
        var reading = Reading(); var reads = 0;
        var caller = WorkflowProxy.Make<IOrganizationService>((m, a) => {
            Assert.Equal("Retrieve", m.Name); Assert.Equal(S.ReadingTable, a![0]); Assert.Equal(reading.Id, a[1]); reads++; return reading; });
        var target = new Entity(S.Request) { [S.EvidenceReading] = reading.Id.ToString("D") };
        FmReadingEvidence.Capture(caller, target, null!, Guid.NewGuid(), DateTime.UtcNow);
        var snapshot = target.GetAttributeValue<string>(S.EvidenceSnapshot);
        reading["fmc_readingvalue"] = 99m;
        var next = new Entity(S.Request) { [S.EvidenceReading] = reading.Id.ToString("D") };
        FmReadingEvidence.Capture(caller, next, target, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(1, reads); Assert.False(next.Contains(S.EvidenceSnapshot)); Assert.Contains("-1.2345", snapshot);
    }

    [Fact] public void Inaccessible_missing_or_incomplete_readings_cannot_be_attached()
    {
        var target = new Entity(S.Request) { [S.EvidenceReading] = Reading().Id.ToString("D") };
        var denied = WorkflowProxy.Make<IOrganizationService>((_, _) => throw new InvalidPluginExecutionException("Read denied"));
        Assert.Throws<InvalidPluginExecutionException>(() => FmReadingEvidence.Capture(denied, target, null!, Guid.NewGuid(), DateTime.UtcNow));
        Assert.False(target.Contains(S.EvidenceSnapshot));
        var incomplete = Reading(); incomplete.Attributes.Remove("fmc_readingvalue");
        var caller = WorkflowProxy.Make<IOrganizationService>((_, _) => incomplete);
        Assert.Throws<InvalidPluginExecutionException>(() => FmReadingEvidence.Capture(caller, target, null!, Guid.NewGuid(), DateTime.UtcNow));
        Assert.False(target.Contains(S.EvidenceSnapshot));
    }

    [Fact] public void Removing_reference_clears_snapshot_and_invalid_id_is_rejected()
    {
        var caller = WorkflowProxy.Make<IOrganizationService>((_, _) => throw new Exception("Must not read"));
        var target = new Entity(S.Request) { [S.EvidenceReading] = null };
        FmReadingEvidence.Capture(caller, target, null!, Guid.NewGuid(), DateTime.UtcNow);
        Assert.True(target.Contains(S.EvidenceSnapshot)); Assert.Null(target[S.EvidenceSnapshot]);
        target[S.EvidenceReading] = "invalid-reading-id";
        Assert.Throws<InvalidPluginExecutionException>(() => FmReadingEvidence.Capture(caller, target, null!, Guid.NewGuid(), DateTime.UtcNow));
    }
}
