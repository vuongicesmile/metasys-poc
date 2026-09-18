namespace BMS.Ingestion.Domain.Models;

/// <summary>One Building in the source catalog; BuildingCode is its stable identity.</summary>
public sealed record BmsBuilding(
    string BuildingCode,
    string Name,
    string SourceBuilding,
    string Description);

/// <summary>One equipment item linked to a Building by BuildingCode.</summary>
public sealed record BmsEquipment(
    string EquipmentCode,
    string Name,
    string EquipmentType,
    string BuildingCode,
    string Description);

/// <summary>One source point exposed by the simulator and consumed by ingestion.</summary>
public sealed record MetasysPoint(string ObjectId)
{
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Building { get; init; } = "";
    public string EquipmentCode { get; init; } = "";
    public decimal Value { get; set; }
    public string Unit { get; init; } = "";
    public DateTime Timestamp { get; set; }

    public MetasysPoint Copy() => this with { };
}

public sealed record MetasysCatalog(
    BmsBuilding[] Buildings,
    BmsEquipment[] Equipment,
    MetasysPoint[] Points);
