namespace SPO.Ingestion.Business;

/// <summary>Kết quả xử lý một file đã được lưu trong Dataverse inbox.</summary>
public sealed record SpoInboxFileResult(
    Guid FileId,
    string FileName,
    string SourcePath,
    string ETag,
    string Status,
    int InputRows = 0,
    int Delivered = 0,
    int Skipped = 0,
    string? Error = null);
