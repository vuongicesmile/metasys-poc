using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Contracts;
using BMS.Ingestion.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BMS.Ingestion.DataAccess.Services;

/// <summary>Repository append COV reading vào raw.bms_reading bằng EF Core.</summary>
public sealed class BmsReadingRepository(
    // Factory tạo DbContext mới cho mỗi thao tác, tránh dùng chung context lâu dài.
    IDbContextFactory<BmsIngestionDbContext> contextFactory) : IBmsReadingRepository
{
    // Giá trị cố định ghi vào source_system để phân biệt nguồn Fake Metasys.
    private const string SourceSystem = "Fake Metasys COV";

    /// <summary>Tạo một row lịch sử chỉ-append và để SQL Server sinh identity.</summary>
    public async Task InsertAsync(
        BmsReadingDto reading,
        CancellationToken cancellationToken = default)
    {
        // Tạo DbContext cho đúng một thao tác insert.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Chuyển DTO thành EF entity; business layer không biết entity này.
        db.Readings.Add(new BmsReadingEntity
        {
            // Sao chép từng trường DTO sang cột tương ứng.
            ObjectId = reading.ObjectId,
            ObjectName = reading.ObjectName,
            ObjectType = reading.ObjectType,
            Building = reading.Building,
            EquipmentCode = reading.EquipmentCode,
            ReadingTime = reading.ReadingTime,
            ReadingValue = reading.ReadingValue,
            Unit = reading.Unit,
            SourceSystem = SourceSystem
        });

        // EF tạo INSERT SQL và thực thi bất đồng bộ.
        await db.SaveChangesAsync(cancellationToken);
    }
}
