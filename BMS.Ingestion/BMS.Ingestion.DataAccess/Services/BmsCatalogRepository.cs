using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Contracts;
using BMS.Ingestion.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BMS.Ingestion.DataAccess.Services;

/// <summary>Upsert catalog nguồn vào raw.bms_building và raw.bms_equipment.</summary>
public sealed class BmsCatalogRepository(
    // Factory tạo DbContext theo từng lần xử lý catalog.
    IDbContextFactory<BmsIngestionDbContext> contextFactory) : IBmsCatalogRepository
{
    /// <summary>
    /// Validate và upsert trọn một catalog snapshot trong một SQL transaction.
    /// Tòa nhà được save trước thiết bị để thỏa mãn foreign key.
    /// </summary>
    public async Task PersistCatalogAsync(
        BmsCatalogDto catalog,
        CancellationToken cancellationToken = default)
    {
        // Kiểm tra duplicate key và quan hệ thiết bị - tòa nhà trước khi mở transaction.
        ValidateCatalog(catalog);

        // Tạo context mới cho lần đồng bộ catalog này.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        // Mọi thay đổi building/equipment phải commit hoặc rollback cùng nhau.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Dùng cùng một timestamp cho toàn bộ snapshot để dữ liệu nhất quán.
        var now = DateTime.UtcNow;

        // Upsert tòa nhà trước.
        await UpsertBuildingsAsync(db, catalog.Buildings, now, cancellationToken);
        // Flush INSERT/UPDATE tòa nhà xuống SQL trước khi xử lý FK thiết bị.
        await db.SaveChangesAsync(cancellationToken);

        // Upsert thiết bị sau khi tòa nhà đã tồn tại.
        await UpsertEquipmentAsync(db, catalog.Equipment, now, cancellationToken);
        // Flush INSERT/UPDATE thiết bị xuống SQL.
        await db.SaveChangesAsync(cancellationToken);

        // Xác nhận toàn bộ transaction thành công.
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Đọc các tòa nhà đã có một lần, sau đó update hoặc add từng DTO.</summary>
    private static async Task UpsertBuildingsAsync(
        BmsIngestionDbContext db,
        IReadOnlyList<BmsBuildingDto> buildings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Lấy danh sách key để chỉ query những tòa nhà trong snapshot hiện tại.
        var codes = buildings.Select(item => item.BuildingCode).ToArray();
        // Dictionary giúp tìm entity hiện có nhanh theo BuildingCode.
        var existing = await db.Buildings
            .Where(row => codes.Contains(row.BuildingCode))
            .ToDictionaryAsync(row => row.BuildingCode, StringComparer.Ordinal, cancellationToken);

        // Duyệt từng DTO nguồn.
        foreach (var building in buildings)
        {
            // Nếu key đã tồn tại, cập nhật entity đang được EF tracking.
            if (existing.TryGetValue(building.BuildingCode, out var entity))
            {
                Update(entity, building, now);
            }
            else
            {
                // Nếu chưa có, tạo entity mới để EF INSERT.
                db.Buildings.Add(CreateEntity(building, now));
            }
        }
    }

    /// <summary>Đọc các thiết bị đã có một lần, sau đó update hoặc add từng DTO.</summary>
    private static async Task UpsertEquipmentAsync(
        BmsIngestionDbContext db,
        IReadOnlyList<BmsEquipmentDto> equipment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Lấy danh sách key thiết bị trong snapshot hiện tại.
        var codes = equipment.Select(item => item.EquipmentCode).ToArray();
        // Tạo dictionary để tra entity hiện có theo EquipmentCode.
        var existing = await db.Equipment
            .Where(row => codes.Contains(row.EquipmentCode))
            .ToDictionaryAsync(row => row.EquipmentCode, StringComparer.Ordinal, cancellationToken);

        // Duyệt từng DTO thiết bị.
        foreach (var item in equipment)
        {
            // Key tồn tại thì update entity đang tracking.
            if (existing.TryGetValue(item.EquipmentCode, out var entity))
            {
                Update(entity, item, now);
            }
            else
            {
                // Key chưa tồn tại thì thêm entity mới.
                db.Equipment.Add(CreateEntity(item, now));
            }
        }
    }

    /// <summary>Kiểm tra identity và quan hệ trước khi mở transaction database.</summary>
    private static void ValidateCatalog(BmsCatalogDto catalog)
    {
        // Tạo tập mã tòa nhà để phát hiện duplicate và kiểm tra foreign key ở bộ nhớ.
        var buildingCodes = catalog.Buildings
            .Select(item => item.BuildingCode)
            .ToHashSet(StringComparer.Ordinal);
        // Tạo tập mã thiết bị để phát hiện duplicate.
        var equipmentCodes = catalog.Equipment
            .Select(item => item.EquipmentCode)
            .ToHashSet(StringComparer.Ordinal);

        // Nếu số phần tử tập hợp nhỏ hơn danh sách, nghĩa là có duplicate code.
        if (buildingCodes.Count != catalog.Buildings.Count ||
            equipmentCodes.Count != catalog.Equipment.Count)
            throw new InvalidOperationException("Fake Metasys catalog contains duplicate codes.");

        // Mỗi thiết bị phải tham chiếu tới một tòa nhà trong cùng snapshot.
        if (catalog.Equipment.Any(item => !buildingCodes.Contains(item.BuildingCode)))
            throw new InvalidOperationException("Fake Metasys equipment refers to a missing building.");
    }

    // Tạo entity building từ DTO để EF theo dõi như một bản ghi mới.
    private static BmsBuildingEntity CreateEntity(BmsBuildingDto source, DateTime now) => new()
    {
        BuildingCode = source.BuildingCode,
        Name = source.Name,
        SourceBuilding = source.SourceBuilding,
        Description = source.Description,
        SourceUpdatedAt = now,
        IngestedAt = now
    };

    // Cập nhật entity building hiện có; EF sẽ tạo câu UPDATE khi SaveChanges.
    private static void Update(BmsBuildingEntity target, BmsBuildingDto source, DateTime now)
    {
        target.Name = source.Name;
        target.SourceBuilding = source.SourceBuilding;
        target.Description = source.Description;
        target.SourceUpdatedAt = now;
        target.IngestedAt = now;
    }

    // Tạo entity equipment từ DTO để EF theo dõi như một bản ghi mới.
    private static BmsEquipmentEntity CreateEntity(BmsEquipmentDto source, DateTime now) => new()
    {
        EquipmentCode = source.EquipmentCode,
        Name = source.Name,
        EquipmentType = source.EquipmentType,
        BuildingCode = source.BuildingCode,
        Description = source.Description,
        SourceUpdatedAt = now,
        IngestedAt = now
    };

    // Cập nhật entity equipment hiện có; EF sẽ tạo câu UPDATE khi SaveChanges.
    private static void Update(BmsEquipmentEntity target, BmsEquipmentDto source, DateTime now)
    {
        target.Name = source.Name;
        target.EquipmentType = source.EquipmentType;
        target.BuildingCode = source.BuildingCode;
        target.Description = source.Description;
        target.SourceUpdatedAt = now;
        target.IngestedAt = now;
    }
}
