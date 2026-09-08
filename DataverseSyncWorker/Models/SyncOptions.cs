namespace DataverseSyncWorker.Models;

public sealed class SyncOptions
{
    public string Url { get; set; } = "https://org06cbc9ec.crm5.dynamics.com/";
    public Guid ExpectedOrganizationId { get; set; } = Guid.Parse("ab191700-b99e-f111-aaa0-000d3a80bb96");
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CertificateThumbprint { get; set; } = "";
    public string CertificateStoreLocation { get; set; } = "CurrentUser";
    public string DeveloperTokenPython { get; set; } = "";
    public string DeveloperTokenScript { get; set; } = "";
    public string SourceId { get; set; } = "FMC";
    public bool Enabled { get; set; } = true;
    public bool HistoryEnabled { get; set; } = true;
    public int HistoryTtlSeconds { get; set; } = 2592000;
    public int BatchSize { get; set; } = 100;
    public int PollIntervalSeconds { get; set; } = 10;
    public string ExecutionMode { get; set; } = "Continuous";
    public int CommandPollIntervalSeconds { get; set; } = 10;
    public int CommandMaxDurationMinutes { get; set; } = 15;
    public int CommandLeaseSeconds { get; set; } = 90;
    public bool ProvisionPowerAutomate { get; set; }
    public string PowerAutomateConnectionId { get; set; } = "";
    public string PowerAutomateConnectionReference { get; set; } = "fmc_sharedcommondataserviceforapps";
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
        if (ExecutionMode is not ("Continuous" or "CommandDriven"))
            throw new InvalidOperationException("ExecutionMode must be Continuous or CommandDriven.");
        if (CommandPollIntervalSeconds < 1 || CommandMaxDurationMinutes < 1 || CommandLeaseSeconds < 30)
            throw new InvalidOperationException("Command polling/duration must be positive and the command lease must be at least 30 seconds.");
        if (ProvisionPowerAutomate && (string.IsNullOrWhiteSpace(PowerAutomateConnectionId) ||
            string.IsNullOrWhiteSpace(PowerAutomateConnectionReference)))
            throw new InvalidOperationException("Power Automate provisioning requires a connection ID and connection reference logical name.");
        if (CertificateStoreLocation is not ("CurrentUser" or "LocalMachine"))
            throw new InvalidOperationException("CertificateStoreLocation must be CurrentUser or LocalMachine.");
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

public static class SyncRequestStatuses
{
    public const int Queued = 789100000;
    public const int Running = 789100001;
    public const int Succeeded = 789100002;
    public const int CompletedWithIssues = 789100003;
    public const int Failed = 789100004;
}

public sealed record CutoffSummary(long SourceRows, long DeliveredRows, long PendingRows,
    long DeadLetterRows, long EligiblePendingRows);

public sealed record SyncRequest(
    Guid Id,
    string CorrelationId,
    long? CutoffId,
    long BaselineDelivered,
    long BaselineDeadLetters,
    int Batches,
    string? RowVersion);
