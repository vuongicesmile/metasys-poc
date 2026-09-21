namespace BMS.Ingestion.Business.Contracts;

/// <summary>
/// DTO chứa dữ liệu tòa nhà chuẩn bị ghi vào catalog raw của BMS.
/// DTO không phụ thuộc vào response của Metasys hoặc EF entity.
/// </summary>
public sealed record BmsBuildingDto(
    // Mã định danh ổn định của tòa nhà.
    string BuildingCode,
    // Tên hiển thị của tòa nhà.
    string Name,
    // Mã/tên tòa nhà do hệ thống nguồn cung cấp.
    string SourceBuilding,
    // Mô tả thêm; có thể là chuỗi rỗng.
    string Description);

/// <summary>Dữ liệu thiết bị chuẩn bị ghi vào catalog raw của BMS.</summary>
public sealed record BmsEquipmentDto(
    // Mã định danh ổn định của thiết bị.
    string EquipmentCode,
    // Tên hiển thị của thiết bị.
    string Name,
    // Loại thiết bị, ví dụ TemperatureSensor hoặc WaterMeter.
    string EquipmentType,
    // Mã tòa nhà mà thiết bị thuộc về.
    string BuildingCode,
    // Mô tả thêm của thiết bị.
    string Description);

/// <summary>Một ảnh chụp catalog được upsert trong cùng một transaction.</summary>
public sealed record BmsCatalogDto(
    // Danh sách tòa nhà trong lần đọc catalog hiện tại.
    IReadOnlyList<BmsBuildingDto> Buildings,
    // Danh sách thiết bị trong lần đọc catalog hiện tại.
    IReadOnlyList<BmsEquipmentDto> Equipment);

/// <summary>
/// Một reading sẽ được append vào <c>raw.bms_reading</c>.
/// PreviousValue không đưa vào DTO vì bảng raw chỉ lưu giá trị quan sát được.
/// </summary>
public sealed record BmsReadingDto(
    // ID của point trong hệ thống Metasys.
    string ObjectId,
    // Tên hiển thị của point.
    string ObjectName,
    // Loại dữ liệu của point.
    string ObjectType,
    // Tên hoặc mã tòa nhà chứa point.
    string Building,
    // Mã thiết bị liên quan đến point.
    string EquipmentCode,
    // Thời điểm point phát sinh reading.
    DateTime ReadingTime,
    // Giá trị đo được; EF map giá trị này thành decimal(18,4).
    decimal ReadingValue,
    // Đơn vị đo, ví dụ C, m3 hoặc L/s.
    string Unit);
