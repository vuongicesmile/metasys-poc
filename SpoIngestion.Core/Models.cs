using Microsoft.Xrm.Sdk;

namespace SpoIngestion.Core;

public sealed record SpoSourceDefinition(
    string Key,
    string PathPrefix,
    string Mapping,
    string TargetTable,
    string[] Extensions,
    bool Enabled = true,
    string? Sheet = null,
    int MaxRows = 250_000,
    string? LocalSample = null,
    string TimestampUtcOffset = "+00:00",
    string? OwnedKeyPrefix = null);

public sealed record SpoIngestionOptions
{
    public string SourceNamespace { get; init; } = "bms_spo_dev01";
    public string SourceId { get; init; } = "FMC";
    public int HistoryTtlSeconds { get; init; } = 2_592_000;
    public int BatchSize { get; init; } = 100;
    public long MaxFileBytes { get; init; } = 52_428_800;
    public string RawContainer { get; init; } = "spo-raw";
    public string ControlContainer { get; init; } = "spo-control";
    public string QueueName { get; init; } = "spo-ingestion";
    public SpoSourceDefinition[] Sources { get; init; } = [];
    public Dictionary<string, int> EquipmentTypeChoices { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ParsedRow(int Ordinal, IReadOnlyDictionary<string, string?> Values);

public enum BronzeRecordKind { Building, Equipment, Point, History }

public sealed record BronzeRecord(
    int SourceOrdinal,
    string Identity,
    BronzeRecordKind Kind,
    Entity Entity,
    DateTime? EventTimeUtc = null,
    string? PartitionId = null,
    string? ParentIdentity = null);

public sealed record RowIssue(int Ordinal, string Code, string Message);

public sealed record DatasetPreview(
    string SourceKey,
    string Mapping,
    int InputRows,
    IReadOnlyDictionary<string, int> TargetCounts,
    IReadOnlyList<RowIssue> Issues);

public sealed record MappingResult(IReadOnlyList<BronzeRecord> Records, IReadOnlyList<RowIssue> Issues, int ExpiredHistory);

public sealed record SpoCaptureRequest(
    string SourcePath,
    string RawBlobName,
    string SourceETag,
    long ContentLength);

public sealed record SpoJobMessage(string JobId);

public sealed record SpoJobManifest(
    string JobId,
    string JobKey,
    string Status,
    string SourcePath,
    string SourceETag,
    string RawBlobName,
    string ContentSha256,
    long ContentLength,
    string SourceKey,
    string MappingVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Attempt = 0,
    int InputRows = 0,
    int Delivered = 0,
    int Skipped = 0,
    string? Error = null);

public sealed record RowReceipt(int Ordinal, string Identity, string Target, string Outcome, string? Detail = null);

public sealed record BronzeWriteResult(int Delivered, int Skipped, IReadOnlyList<RowReceipt> Receipts);

public sealed class SpoContractException(string code, int ordinal, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Ordinal { get; } = ordinal;
}

public sealed class SpoDependencyException(string message) : Exception(message);
