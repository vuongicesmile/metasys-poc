namespace FakeMetasysApi.Models;

public sealed class MetasysPoint
{
    public string ObjectId { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Building { get; init; } = "";
    public string EquipmentCode { get; init; } = "";
    public decimal Value { get; set; }
    public string Unit { get; init; } = "";
    public DateTime Timestamp { get; set; }

    public MetasysPoint Copy() => new()
    {
        ObjectId = ObjectId,
        ObjectName = ObjectName,
        ObjectType = ObjectType,
        Building = Building,
        EquipmentCode = EquipmentCode,
        Value = Value,
        Unit = Unit,
        Timestamp = Timestamp
    };
}
