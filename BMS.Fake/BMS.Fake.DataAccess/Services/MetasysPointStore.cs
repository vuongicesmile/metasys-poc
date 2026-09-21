using BMS.Fake.Business.Abstractions;
using BMS.Fake.Business.Contracts;
using BMS.Fake.DataAccess.Persistence;
using BMS.Ingestion.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BMS.Fake.DataAccess.Services;

/// <summary>Repository đọc/ghi current state của Fake Metasys bằng EF Core.</summary>
public sealed class MetasysPointStore(
    IDbContextFactory<FakeBmsDbContext> contextFactory) : IMetasysPointStore
{
    /// <summary>Đọc catalog building từ database, không tracking entity.</summary>
    public async Task<IReadOnlyList<BmsBuildingDto>> GetBuildingsAsync(
        CancellationToken cancellationToken = default)
    {
        // Tạo context ngắn hạn cho query.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // AsNoTracking phù hợp vì endpoint chỉ đọc và không update entity này.
        return await db.Buildings
            .AsNoTracking()
            .OrderBy(row => row.BuildingCode)
            .Select(row => new BmsBuildingDto(
                row.BuildingCode,
                row.Name,
                row.SourceBuilding,
                row.Description))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Đọc catalog equipment từ database, không tracking entity.</summary>
    public async Task<IReadOnlyList<BmsEquipmentDto>> GetEquipmentAsync(
        CancellationToken cancellationToken = default)
    {
        // Tạo context cho query equipment.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Chỉ project các field API cần, không trả EF entity ra ngoài.
        return await db.Equipment
            .AsNoTracking()
            .OrderBy(row => row.EquipmentCode)
            .Select(row => new BmsEquipmentDto(
                row.EquipmentCode,
                row.Name,
                row.EquipmentType,
                row.BuildingCode,
                row.Description))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Đọc snapshot point hiện tại từ database.</summary>
    public async Task<IReadOnlyList<MetasysPointDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // Tạo context đọc point.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Sắp xếp ổn định để API và simulator có kết quả dễ đọc.
        return await db.Points
            .AsNoTracking()
            .OrderBy(row => row.ObjectId)
            .Select(row => new MetasysPointDto(row.ObjectId)
            {
                ObjectName = row.ObjectName,
                ObjectType = row.ObjectType,
                Building = row.Building,
                EquipmentCode = row.EquipmentCode,
                Value = row.Value,
                Unit = row.Unit,
                Timestamp = row.Timestamp
            })
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Đọc một point theo ObjectId hoặc trả null nếu không tồn tại.</summary>
    public async Task<MetasysPointDto?> GetAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        // Tạo context cho một query point.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // FirstOrDefaultAsync trả null khi không tìm thấy ObjectId.
        return await db.Points
            .AsNoTracking()
            .Where(row => row.ObjectId == objectId)
            .Select(row => new MetasysPointDto(row.ObjectId)
            {
                ObjectName = row.ObjectName,
                ObjectType = row.ObjectType,
                Building = row.Building,
                EquipmentCode = row.EquipmentCode,
                Value = row.Value,
                Unit = row.Unit,
                Timestamp = row.Timestamp
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Kiểm tra nhanh một ObjectId có trong database.</summary>
    public async Task<bool> ExistsAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        // Tạo context chỉ để chạy EXISTS query.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Points.AnyAsync(row => row.ObjectId == objectId, cancellationToken);
    }

    /// <summary>
    /// Đọc giá trị cũ, cập nhật database và trả CovEvent trong cùng transaction.
    /// </summary>
    public async Task<CovEvent> ChangeValueAsync(
        string objectId,
        decimal newValue,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        // Context này được dùng cho cả đọc, update và commit.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        // Transaction giữ previous value và update nhất quán.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Không dùng AsNoTracking vì cần EF theo dõi entity để tạo UPDATE.
        var point = await db.Points
            .SingleOrDefaultAsync(row => row.ObjectId == objectId, cancellationToken);
        if (point is null)
            throw new KeyNotFoundException($"BMS point '{objectId}' was not found.");

        // Lưu giá trị cũ trước khi thay đổi để phát COV event chính xác.
        var previousValue = point.Value;
        // Cập nhật current state trong entity đang được EF tracking.
        point.Value = newValue;
        point.Timestamp = timestamp;

        // EF tạo UPDATE SQL cho point.
        await db.SaveChangesAsync(cancellationToken);
        // Xác nhận update thành công.
        await transaction.CommitAsync(cancellationToken);

        // CovEvent là shared contract mà BMS.Ingestion đọc qua SSE.
        return new CovEvent
        {
            ObjectId = point.ObjectId,
            ObjectName = point.ObjectName,
            ObjectType = point.ObjectType,
            Building = point.Building,
            EquipmentCode = point.EquipmentCode,
            PreviousValue = previousValue,
            CurrentValue = point.Value,
            Unit = point.Unit,
            Timestamp = point.Timestamp
        };
    }
}
