namespace BMS.Ingestion.DataAccess.Persistence;

/// <summary>EF entity đại diện cho raw.bms_building; không lộ ra Business layer.</summary>
internal sealed class BmsBuildingEntity
{
    // Khóa chính tương ứng với cột raw.bms_building.building_code.
    public string BuildingCode { get; set; } = "";
    // Tên tòa nhà lưu trong cột name.
    public string Name { get; set; } = "";
    // Mã tòa nhà từ hệ thống nguồn.
    public string SourceBuilding { get; set; } = "";
    // Mô tả; database cho phép NULL.
    public string? Description { get; set; }
    // Thời điểm catalog nguồn được cập nhật.
    public DateTime SourceUpdatedAt { get; set; }
    // Thời điểm bản ghi được ingestion vào SQL.
    public DateTime IngestedAt { get; set; }
}

/// <summary>EF entity đại diện cho raw.bms_equipment; không lộ ra Business layer.</summary>
internal sealed class BmsEquipmentEntity
{
    // Khóa chính của thiết bị.
    public string EquipmentCode { get; set; } = "";
    // Tên hiển thị của thiết bị.
    public string Name { get; set; } = "";
    // Loại thiết bị.
    public string EquipmentType { get; set; } = "";
    // Foreign key trỏ tới raw.bms_building.building_code.
    public string BuildingCode { get; set; } = "";
    // Mô tả; database cho phép NULL.
    public string? Description { get; set; }
    // Thời điểm catalog nguồn được cập nhật.
    public DateTime SourceUpdatedAt { get; set; }
    // Thời điểm bản ghi được ingestion vào SQL.
    public DateTime IngestedAt { get; set; }
}

/// <summary>EF entity đại diện cho raw.bms_reading; SQL Server tự tạo Id và IngestedAt.</summary>
internal sealed class BmsReadingEntity
{
    // Khóa identity do SQL Server tự tăng.
    public long Id { get; set; }
    // ID ổn định của point từ Metasys.
    public string ObjectId { get; set; } = "";
    // Tên point; database cho phép NULL.
    public string? ObjectName { get; set; }
    // Loại point; database cho phép NULL.
    public string? ObjectType { get; set; }
    // Tòa nhà của point; database cho phép NULL.
    public string? Building { get; set; }
    // Thiết bị của point; database cho phép NULL.
    public string? EquipmentCode { get; set; }
    // Thời điểm reading xảy ra.
    public DateTime ReadingTime { get; set; }
    // Giá trị đo; database dùng decimal(18,4).
    public decimal? ReadingValue { get; set; }
    // Đơn vị đo; database cho phép NULL.
    public string? Unit { get; set; }
    // Tên hệ thống phát sinh reading.
    public string SourceSystem { get; set; } = "";
    // Thời điểm SQL nhận reading; database có default value.
    public DateTime IngestedAt { get; set; }
}
