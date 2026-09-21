using BMS.Fake.DataAccess.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BMS.Fake.DataAccess.Persistence;

/// <summary>Khởi tạo database Fake và seed dữ liệu mẫu khi database còn trống.</summary>
public sealed class FakeBmsDatabaseInitializer(
    IDbContextFactory<FakeBmsDbContext> contextFactory)
{
    /// <summary>
    /// Tạo database/schema bằng EF Core rồi seed fixture một lần.
    /// Đây là bootstrap phù hợp cho POC local; production nên dùng EF migrations.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Tạo database BMS_Fake và các bảng nếu chưa tồn tại.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // Nếu đã có point, coi database đã được seed và không ghi đè state runtime.
        if (await db.Points.AnyAsync(cancellationToken))
            return;

        // Database relational dùng transaction; provider InMemory của unit test thì không hỗ trợ transaction.
        IDbContextTransaction? transaction = null;
        try
        {
            if (db.Database.IsRelational())
                transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Chuyển building DTO của fixture thành entity database.
            db.Buildings.AddRange(MetasysFixture.Buildings.Select(building => new FakeBuildingEntity
            {
                BuildingCode = building.BuildingCode,
                Name = building.Name,
                SourceBuilding = building.SourceBuilding,
                Description = building.Description
            }));

            // Chuyển equipment DTO của fixture thành entity database.
            db.Equipment.AddRange(MetasysFixture.Equipment.Select(item => new FakeEquipmentEntity
            {
                EquipmentCode = item.EquipmentCode,
                Name = item.Name,
                EquipmentType = item.EquipmentType,
                BuildingCode = item.BuildingCode,
                Description = item.Description
            }));

            // CreatePoints trả dictionary mới để lấy các point ban đầu cần seed.
            db.Points.AddRange(MetasysFixture.CreatePoints().Values.Select(point => new FakePointEntity
            {
                ObjectId = point.ObjectId,
                ObjectName = point.ObjectName,
                ObjectType = point.ObjectType,
                Building = point.Building,
                EquipmentCode = point.EquipmentCode,
                Value = point.Value,
                Unit = point.Unit,
                Timestamp = point.Timestamp
            }));

            // EF tạo INSERT cho toàn bộ fixture.
            await db.SaveChangesAsync(cancellationToken);

            // Xác nhận seed thành công nếu database có transaction.
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            // Luôn giải phóng transaction, kể cả khi seed bị lỗi giữa chừng.
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }
}
