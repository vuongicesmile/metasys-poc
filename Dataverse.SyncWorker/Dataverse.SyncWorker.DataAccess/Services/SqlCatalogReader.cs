using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.DataAccess.Persistence;
using DataverseSyncWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace DataverseSyncWorker.Services;

/// <summary>
/// Adapter EF Core chuyển các row persistence thành domain record.
/// </summary>
public sealed class SqlCatalogReader(IDbContextFactory<SqlSyncDbContext> contextFactory)
    : ISqlCatalogReader
{
    public async Task<(List<BmsBuilding> Buildings, List<BmsEquipment> Equipment)> Read(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // AsNoTracking phù hợp với catalog read-only: EF không cần giữ snapshot
        // thay đổi, giảm memory và làm rõ rằng worker không ghi ngược catalog.
        var buildings = await db.Buildings
            .AsNoTracking()
            .OrderBy(row => row.BuildingCode)
            .Select(row => new BmsBuilding(row.BuildingCode, row.Name, row.SourceBuilding,
                row.Description, row.SourceUpdatedAt))
            .ToListAsync(ct);

        var equipment = await db.Equipment
            .AsNoTracking()
            .OrderBy(row => row.EquipmentCode)
            .Select(row => new BmsEquipment(row.EquipmentCode, row.Name, row.EquipmentType,
                row.BuildingCode, row.Description, row.SourceUpdatedAt))
            .ToListAsync(ct);

        // Giữ invariant cũ ở boundary SQL: equipment không được trỏ tới building
        // không tồn tại, nếu không lookup Dataverse sẽ tạo dữ liệu mồ côi.
        var buildingCodes = buildings.Select(row => row.BuildingCode).ToHashSet(StringComparer.Ordinal);
        if (equipment.Any(row => !buildingCodes.Contains(row.BuildingCode)))
            throw new InvalidOperationException("SQL equipment catalog refers to a missing building.");

        return (buildings, equipment);
    }
}