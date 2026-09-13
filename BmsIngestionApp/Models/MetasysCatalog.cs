namespace BmsIngestionApp.Models;

public sealed record MetasysCatalog(BmsBuilding[] Buildings, BmsEquipment[] Equipment, MetasysPoint[] Points);
public sealed record MetasysPoint(string ObjectId);
