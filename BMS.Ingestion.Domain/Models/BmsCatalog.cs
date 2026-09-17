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

public sealed record MetasysPoint(string ObjectId);

public sealed record MetasysCatalog(
    BmsBuilding[] Buildings,
    BmsEquipment[] Equipment,
    MetasysPoint[] Points);
