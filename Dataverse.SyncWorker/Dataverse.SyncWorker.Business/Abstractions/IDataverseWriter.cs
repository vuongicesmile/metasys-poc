using Microsoft.Xrm.Sdk;

namespace DataverseSyncWorker.Abstractions;

public interface IDataverseWriter
{
    Task WriteBuildings(IReadOnlyList<Entity> buildings, CancellationToken ct);
    Task WriteEquipment(IReadOnlyList<Entity> equipment, CancellationToken ct);
    Task WritePoints(IReadOnlyList<Entity> points, CancellationToken ct);
    Task WriteHistory(IReadOnlyList<Entity> readings, CancellationToken ct);
}
