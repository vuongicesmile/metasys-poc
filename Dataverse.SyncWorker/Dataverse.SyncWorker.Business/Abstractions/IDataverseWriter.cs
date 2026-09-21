using DataverseSyncWorker.Contracts;

namespace DataverseSyncWorker.Abstractions;

/// <summary>
/// Cổng ghi các bản ghi đã được mapper sang Dataverse.
/// Business chỉ biết cổng này, còn SDK/API cụ thể nằm trong DataAccess.
/// </summary>
public interface IDataverseWriter
{
    Task WriteBuildings(IReadOnlyList<DataverseRecord> buildings, CancellationToken ct);
    Task WriteEquipment(IReadOnlyList<DataverseRecord> equipment, CancellationToken ct);
    Task WritePoints(IReadOnlyList<DataverseRecord> points, CancellationToken ct);
    Task WriteHistory(IReadOnlyList<DataverseRecord> readings, CancellationToken ct);
}
