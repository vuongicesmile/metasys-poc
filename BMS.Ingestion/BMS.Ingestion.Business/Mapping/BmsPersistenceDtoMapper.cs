using BMS.Ingestion.Business.Contracts;
using BMS.Ingestion.Domain.Models;

namespace BMS.Ingestion.Business.Mapping;

/// <summary>Chuyển model nguồn thành DTO persistence tại ranh giới Business/DataAccess.</summary>
public static class BmsPersistenceDtoMapper
{
    /// <summary>Tạo DTO persistence từ catalog do Metasys trả về.</summary>
    public static BmsCatalogDto ToPersistenceDto(this MetasysCatalog catalog) => new(
        // Chuyển từng BmsBuilding của domain thành BmsBuildingDto.
        catalog.Buildings
            .Select(building => new BmsBuildingDto(
                // Giữ nguyên mã tòa nhà để không thay đổi identity.
                building.BuildingCode,
                // Sao chép tên hiển thị.
                building.Name,
                // Sao chép mã/tên từ hệ thống nguồn.
                building.SourceBuilding,
                // Sao chép mô tả.
                building.Description))
            // Materialize thành array để DTO giữ một snapshot độc lập.
            .ToArray(),
        // Chuyển từng thiết bị của domain thành BmsEquipmentDto.
        catalog.Equipment
            .Select(item => new BmsEquipmentDto(
                // Giữ nguyên mã thiết bị.
                item.EquipmentCode,
                // Sao chép tên thiết bị.
                item.Name,
                // Sao chép loại thiết bị.
                item.EquipmentType,
                // Giữ quan hệ thiết bị - tòa nhà.
                item.BuildingCode,
                // Sao chép mô tả thiết bị.
                item.Description))
            // Materialize danh sách trước khi trả về.
            .ToArray());

    /// <summary>Tạo DTO persistence từ một COV event vừa nhận.</summary>
    public static BmsReadingDto ToPersistenceDto(this CovEvent covEvent) => new(
        // Sao chép ID point.
        covEvent.ObjectId,
        // Sao chép tên point.
        covEvent.ObjectName,
        // Sao chép loại point.
        covEvent.ObjectType,
        // Sao chép tòa nhà.
        covEvent.Building,
        // Sao chép mã thiết bị.
        covEvent.EquipmentCode,
        // Đổi tên Timestamp của event thành ReadingTime của DTO persistence.
        covEvent.Timestamp,
        // Chỉ lưu giá trị hiện tại; PreviousValue không được ghi vào raw table.
        covEvent.CurrentValue,
        // Sao chép đơn vị đo.
        covEvent.Unit);
}
