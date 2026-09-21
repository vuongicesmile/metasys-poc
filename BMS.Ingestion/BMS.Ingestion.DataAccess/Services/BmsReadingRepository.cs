using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Contracts;
using BMS.Ingestion.DataAccess.Persistence;

namespace BMS.Ingestion.DataAccess.Services;

/// <summary>Repository append COV reading vào raw.bms_reading bằng EF Core.</summary>
public sealed class BmsReadingRepository(
    // Scoped DbContext được sở hữu bởi Unit of Work hiện tại.
    BmsIngestionDbContext db) : IBmsReadingRepository
{
    // Giá trị cố định ghi vào source_system để phân biệt nguồn Fake Metasys.
    private const string SourceSystem = "Fake Metasys COV";

    /// <summary>Tạo một row lịch sử chỉ-append và để SQL Server sinh identity.</summary>
    public void Add(BmsReadingDto reading)
    {
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
    }
}
