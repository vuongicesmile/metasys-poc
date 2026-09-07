namespace BmsIngestionApp.Models;

public sealed class IngestionStatusSnapshot
{
    public string State { get; init; } = "Starting";
    public string MetasysBaseUrl { get; init; } = "";
    public bool SqlEnabled { get; init; }
    public string? SubscriptionId { get; init; }
    public long EventsReceived { get; init; }
    public long RowsInserted { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? LastEventAt { get; init; }
    public CovEvent? LastEvent { get; init; }
    public string? Error { get; init; }
}

public sealed record IngestionRuntimeOptions(bool SqlEnabled);
