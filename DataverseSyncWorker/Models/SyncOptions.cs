namespace DataverseSyncWorker.Models;

public sealed class SyncOptions
{
    public string Url { get; set; } = "https://org06cbc9ec.crm5.dynamics.com/";
    public Guid ExpectedOrganizationId { get; set; } = Guid.Parse("ab191700-b99e-f111-aaa0-000d3a80bb96");
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CertificateThumbprint { get; set; } = "";
    public string DeveloperTokenPython { get; set; } = "";
    public string DeveloperTokenScript { get; set; } = "";
    public string SourceId { get; set; } = "FMC";
    public bool Enabled { get; set; } = true;
    public bool HistoryEnabled { get; set; } = true;
    public int HistoryTtlSeconds { get; set; } = 2592000;
    public int BatchSize { get; set; } = 100;
    public int PollIntervalSeconds { get; set; } = 10;
    public string LegacyReadingTimeZoneId { get; set; } = "UTC";
    public string SqlIngestedTimeZoneId { get; set; } = "SE Asia Standard Time";
    public string Pipeline => $"{ExpectedOrganizationId:D}:{SourceId}";
    public bool UsesDeveloperToken => !string.IsNullOrWhiteSpace(DeveloperTokenPython) &&
        !string.IsNullOrWhiteSpace(DeveloperTokenScript);
    public bool HasCredentials => UsesDeveloperToken || (Guid.TryParse(ClientId, out _) &&
        (!string.IsNullOrWhiteSpace(ClientSecret) || !string.IsNullOrWhiteSpace(CertificateThumbprint)));
    public void Validate()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("Dataverse:Url must use HTTPS.");
        if (BatchSize is < 1 or > 100 || PollIntervalSeconds < 1 || HistoryTtlSeconds < 1)
            throw new InvalidOperationException("BatchSize must be 1..100; intervals and TTL must be positive.");
        if (string.IsNullOrWhiteSpace(SourceId) || SourceId.Length > 40)
            throw new InvalidOperationException("SourceId must contain 1..40 characters and remain stable.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(LegacyReadingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(SqlIngestedTimeZoneId);
    }
}

public sealed record BmsReading(long Id, string ObjectId, string? ObjectName,
    string? ObjectType, string? Building, DateTime ReadingTime, decimal? ReadingValue,
    string? Unit, string SourceSystem, DateTime? IngestedAt);

public sealed record SyncSummary(long SourceRows, long DeliveredRows, long PendingRows,
    long DeadLetterRows, long LastSuccessfulId);
public sealed record BatchResult(int Read, int Delivered, int Quarantined, bool Busy = false);
public sealed record DeadLetter(long ReadingId, string Error, int Attempts, DateTime LastFailedAt);
public sealed record SyncStatusResponse(Services.RuntimeSnapshot Runtime, SyncSummary Sql,
    string DataverseUrl, bool HistoryEnabled);
