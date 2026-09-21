using SPO.Ingestion.Common;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business;

public sealed class SpoPreviewer(TabularParser parser, SpoBronzeMapper mapper)
{
    public DatasetPreview Preview(Stream content, string sourcePath, SpoIngestionOptions options, DateTime utcNow)
    {
        var source = SpoConfiguration.Resolve(options, sourcePath);
        var rows = parser.Parse(content, Path.GetExtension(sourcePath), source);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<RowIssue>();
        var pointIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var historyIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expired = 0;
        foreach (var chunk in rows.Chunk(1000))
        {
            var mapped = mapper.Map(source, chunk, utcNow);
            foreach (var point in mapped.Records.Where(x => x.Kind == BronzeRecordKind.Point))
                pointIdentities.Add(point.Identity);
            foreach (var history in mapped.Records.Where(x => x.Kind == BronzeRecordKind.History))
                historyIdentities.Add(history.Identity);
            foreach (var group in mapped.Records.Where(x => x.Kind is not (BronzeRecordKind.Point or BronzeRecordKind.History)).GroupBy(x => x.Record.LogicalName))
                counts[group.Key] = counts.GetValueOrDefault(group.Key) + group.Count();
            issues.AddRange(mapped.Issues);
            expired += mapped.ExpiredHistory;
        }
        if (pointIdentities.Count > 0) counts["fmc_bmspoint"] = pointIdentities.Count;
        if (historyIdentities.Count > 0) counts["fmc_bmsreading"] = historyIdentities.Count;
        if (expired > 0) counts["SkippedExpiredHistory"] = expired;
        return new(source.Key, source.Mapping, rows.Count, counts, issues);
    }
}
