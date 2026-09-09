namespace FakeMetasysApi.Models;

public sealed record BmsBuilding(string BuildingCode, string Name, string SourceBuilding, string Description);
public sealed record BmsEquipment(string EquipmentCode, string Name, string EquipmentType,
    string BuildingCode, string Description);
