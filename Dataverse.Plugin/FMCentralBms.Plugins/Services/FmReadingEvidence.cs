#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace FMCentralBms.Plugins
{
    internal static class FmReadingEvidence
    {
        internal static void Capture(IOrganizationService caller, Entity target, Entity existing, Guid actor, DateTime now)
        {
            if (!target.Contains(S.EvidenceReading) && !target.Contains(S.EvidenceReadingIds)) return;
            var raw = target.GetAttributeValue<string>(S.EvidenceReadingIds);
            if (string.IsNullOrWhiteSpace(raw)) raw = target.GetAttributeValue<string>(S.EvidenceReading);
            var ids = ParseIds(raw);
            FmWorkflowPolicy.Require(string.IsNullOrWhiteSpace(raw) || ids.Count > 0, "Select a Standard BMS reading as evidence.");
            if (ids.Count == 0) { target[S.EvidenceReading] = null; target[S.EvidenceReadingIds] = null; target[S.EvidenceSnapshot] = null; return; }
            FmWorkflowPolicy.Require(ids.Count <= 10, "You can attach up to 10 readings as evidence.");
            var canonical = string.Join("\n", ids.Select(x => x.ToString("D")));
            var previous = existing?.GetAttributeValue<string>(S.EvidenceReadingIds);
            if (string.IsNullOrWhiteSpace(previous)) previous = existing?.GetAttributeValue<string>(S.EvidenceReading);
            target[S.EvidenceReading] = ids[0].ToString("D");
            target[S.EvidenceReadingIds] = canonical;
            if (string.Equals(previous, canonical, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(existing?.GetAttributeValue<string>(S.EvidenceSnapshot))) return;
            var readings = ids.Select(id => caller.Retrieve(S.ReadingTable, id, new ColumnSet(
                "fmc_objectid", "fmc_objectname", "fmc_equipmentcode", "fmc_building", "fmc_readingtime",
                "fmc_readingvalue", "fmc_unit", "fmc_sqlreadingid", "fmc_sourcesystem", "fmc_externalkey"))).ToList();
            foreach (var reading in readings)
                FmWorkflowPolicy.Require(reading.GetAttributeValue<object>("fmc_readingvalue") is decimal && reading.GetAttributeValue<object>("fmc_readingtime") is DateTime &&
                    !string.IsNullOrWhiteSpace(reading.GetAttributeValue<string>("fmc_objectid")) && !string.IsNullOrWhiteSpace(reading.GetAttributeValue<string>("fmc_sqlreadingid")),
                    "Reading is missing its value, timestamp, point or SQL ID; choose a complete reading.");
            target[S.EvidenceSnapshot] = Snapshot(readings, actor, now);
        }

        private static List<Guid> ParseIds(string raw) => (raw ?? "").Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Guid.TryParse(x.Trim(), out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToList();
        private static string Text(Entity row, string field) => (row.GetAttributeValue<string>(field) ?? "—").Replace("\r", " ").Replace("\n", " ");
        private static string Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        internal static string Snapshot(Entity reading, Guid actor, DateTime now) => Snapshot(new[] { reading }, actor, now);
        internal static string Snapshot(IReadOnlyList<Entity> readings, Guid actor, DateTime now) =>
            "BMS reading evidence (" + readings.Count.ToString(CultureInfo.InvariantCulture) + " readings, snapshot at attachment)\n" +
            string.Join("\n\n", readings.Select((reading, index) => "Reading " + (index + 1).ToString(CultureInfo.InvariantCulture) + "\n" +
                "Point: " + Text(reading, "fmc_objectid") + " — " + Text(reading, "fmc_objectname") + "\n" +
                "Equipment: " + Text(reading, "fmc_equipmentcode") + "\n" + "Building: " + Text(reading, "fmc_building") + "\n" +
                "Reading time: " + Utc(reading.GetAttributeValue<DateTime>("fmc_readingtime")) + "\n" +
                "Value: " + reading.GetAttributeValue<decimal>("fmc_readingvalue").ToString("F4", CultureInfo.InvariantCulture) + " " + Text(reading, "fmc_unit") + "\n" +
                "SQL reading ID: " + Text(reading, "fmc_sqlreadingid") + "\n" + "Source: " + Text(reading, "fmc_sourcesystem") + "\n" +
                "External key: " + Text(reading, "fmc_externalkey") + "\n" + "Dataverse reading ID: " + reading.Id.ToString("D"))) +
            "\n\nCaptured at: " + Utc(now) + "\nCaptured by user ID: " + actor.ToString("D");
    }
}
