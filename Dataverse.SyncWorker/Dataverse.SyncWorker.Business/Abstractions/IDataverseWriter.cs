using Microsoft.Xrm.Sdk;

namespace DataverseSyncWorker.Abstractions;

/// <summary>
/// Cổng ghi các bản ghi đã được mapper sang Dataverse.
/// Business chỉ biết cổng này, còn SDK/API cụ thể nằm trong DataAccess.
/// </summary>
public interface IDataverseWriter
{
    Task WriteBuildings(IReadOnlyList<Entity> buildings, CancellationToken ct);
    Task WriteEquipment(IReadOnlyList<Entity> equipment, CancellationToken ct);
    Task WritePoints(IReadOnlyList<Entity> points, CancellationToken ct);
    Task WriteHistory(IReadOnlyList<Entity> readings, CancellationToken ct);
}
