using SPO.Ingestion.Common;
using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business;

public sealed record LocalIngestionResult(
    string Status,
    string SourceKey,
    string Mapping,
    string SourcePath,
    int InputRows,
    int Delivered,
    int Skipped,
    IReadOnlyDictionary<string, int> TargetCounts,
    IReadOnlyList<RowIssue> Issues,
    IReadOnlyList<RowReceipt> Receipts);

/// <summary>
/// Runs the SPO pipeline locally when Azure Storage/Functions are not available.
/// It deliberately reuses the same parser, mapper and Dataverse writer as the
/// cloud Functions path; only the Blob manifest and Queue trigger are omitted.
/// </summary>
public sealed class SpoLocalProcessor(
    TabularParser parser,
    SpoBronzeMapper mapper,
    SpoRecordValidator validator,
    ISpoBronzeWriter writer,
    SpoIngestionOptions options)
{
    public async Task<LocalIngestionResult> Process(
        Stream content,
        string sourcePath,
        DateTime utcNow,
        CancellationToken cancellationToken,
        bool currentOnly = false)
    {
        var source = SpoConfiguration.Resolve(options, sourcePath);
        var rows = parser.Parse(content, Path.GetExtension(sourcePath), source);
        var validation = validator.Validate(source, rows, utcNow);
        if (currentOnly && source.Mapping.EndsWith("reading-v1", StringComparison.Ordinal))
            validation = validation with
            {
                ExpiredHistory = 0,
                TargetCounts = validation.TargetCounts
                    .Where(x => x.Key == "fmc_bmspoint")
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            };
        if (validation.Issues.Count > 0)
        {
            return new("Invalid", source.Key, source.Mapping, sourcePath, rows.Count, 0,
                validation.ExpiredHistory, validation.TargetCounts, validation.Issues, []);
        }

        if (!source.Mapping.EndsWith("reading-v1", StringComparison.Ordinal))
        {
            var mapped = mapper.Map(source, rows, utcNow);
            var result = await writer.Write(mapped.Records, cancellationToken);
            return new("Completed", source.Key, source.Mapping, sourcePath, rows.Count,
                result.Delivered, result.Skipped + mapped.ExpiredHistory, validation.TargetCounts, [], result.Receipts);
        }

        var latest = new Dictionary<string, BronzeRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in rows.Chunk(1000))
        foreach (var point in mapper.Map(source, chunk, utcNow).Records.Where(x => x.Kind == BronzeRecordKind.Point))
        {
            if (!latest.TryGetValue(point.Identity, out var current) ||
                point.EventTimeUtc > current.EventTimeUtc ||
                point.EventTimeUtc == current.EventTimeUtc && point.SourceOrdinal > current.SourceOrdinal)
                latest[point.Identity] = point;
        }

        var receipts = new List<RowReceipt>();
        var pointResult = await writer.Write(latest.Values.ToArray(), cancellationToken);
        receipts.AddRange(pointResult.Receipts);
        var delivered = pointResult.Delivered;
        var skipped = pointResult.Skipped + validation.ExpiredHistory;
        var deliveredEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (currentOnly)
            return new("Completed", source.Key, source.Mapping, sourcePath, rows.Count,
                delivered, skipped, validation.TargetCounts, [], receipts);

        foreach (var chunk in rows.Chunk(1000))
        {
            var history = new List<BronzeRecord>();
            foreach (var item in mapper.Map(source, chunk, utcNow).Records.Where(x => x.Kind == BronzeRecordKind.History))
            {
                if (deliveredEvents.Add(item.Identity)) history.Add(item);
                else skipped++;
            }
            if (history.Count == 0) continue;
            var historyResult = await writer.Write(history, cancellationToken);
            receipts.AddRange(historyResult.Receipts);
            delivered += historyResult.Delivered;
            skipped += historyResult.Skipped;
        }

        return new("Completed", source.Key, source.Mapping, sourcePath, rows.Count,
            delivered, skipped, validation.TargetCounts, [], receipts);
    }

}
