namespace BmsIngestionApp.Models;

public sealed class CovEvent
{
    public string ObjectId { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Building { get; init; } = "";
    public decimal PreviousValue { get; init; }
    public decimal CurrentValue { get; init; }
    public string Unit { get; init; } = "";
    public DateTime Timestamp { get; init; }
}

public sealed class SubscriptionRequest
{
    public List<string> ObjectIds { get; init; } = [];
}

public sealed class SubscriptionResponse
{
    public string SubscriptionId { get; init; } = "";
}
