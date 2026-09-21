using Microsoft.Xrm.Sdk;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business;

/// <summary>
/// Chứa các quy tắc kiểm tra chung trước khi ghi dữ liệu SPO vào Dataverse.
///
/// Cloud job và local CLI phải dùng cùng validation; nếu mỗi đường chạy tự kiểm
/// tra một kiểu thì cùng một file có thể được preview thành công nhưng import thất bại.
/// </summary>
public sealed class SpoRecordValidator(SpoBronzeMapper mapper)
{
    public SpoValidationResult Validate(
        SpoSourceDefinition source,
        IReadOnlyList<ParsedRow> rows,
        DateTime utcNow)
    {
        var issues = new List<RowIssue>();
        var pointIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var historyIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var expiredHistory = 0;
        var catalogCount = 0;

        foreach (var chunk in rows.Chunk(1000))
        {
            var mapped = mapper.Map(source, chunk, utcNow);
            issues.AddRange(mapped.Issues);
            expiredHistory += mapped.ExpiredHistory;
            catalogCount += mapped.Records.Count(record =>
                record.Kind is BronzeRecordKind.Building or BronzeRecordKind.Equipment);

            foreach (var point in mapped.Records.Where(record => record.Kind == BronzeRecordKind.Point))
            {
                pointIdentities.Add(point.Identity);
                var eventKey = $"{point.Identity}|{point.EventTimeUtc:O}";
                var value = point.Entity.GetAttributeValue<decimal>("fmc_currentvalue");
                if (eventValues.TryGetValue(eventKey, out var previous) && previous != value)
                {
                    issues.Add(new(point.SourceOrdinal, "SPO-DUPLICATE-EVENT",
                        $"Event '{eventKey}' occurs with conflicting values {previous} and {value}."));
                }
                else
                {
                    eventValues.TryAdd(eventKey, value);
                }
            }

            foreach (var history in mapped.Records.Where(record => record.Kind == BronzeRecordKind.History))
                historyIdentities.Add(history.Identity);
        }

        var targetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (pointIdentities.Count > 0) targetCounts["fmc_bmspoint"] = pointIdentities.Count;
        if (historyIdentities.Count > 0) targetCounts["fmc_bmsreading"] = historyIdentities.Count;
        if (source.Mapping is "building-v1") targetCounts["fmc_bmsbuilding"] = catalogCount;
        if (source.Mapping is "equipment-v1" or "water-meter-v1" or "electric-meter-v1")
            targetCounts["fmc_bmsequipment"] = catalogCount;
        if (expiredHistory > 0) targetCounts["SkippedExpiredHistory"] = expiredHistory;

        return new(issues, expiredHistory, targetCounts);
    }
}

public sealed record SpoValidationResult(
    IReadOnlyList<RowIssue> Issues,
    int ExpiredHistory,
    IReadOnlyDictionary<string, int> TargetCounts);