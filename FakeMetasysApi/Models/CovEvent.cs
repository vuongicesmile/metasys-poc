namespace FakeMetasysApi.Models;

public sealed class CovEvent
{
    public string ObjectId { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Building { get; init; } = "";
    public string EquipmentCode { get; init; } = "";
    public decimal PreviousValue { get; init; }
    public decimal CurrentValue { get; init; }
    public string Unit { get; init; } = "";
    public DateTime Timestamp { get; init; }
}
