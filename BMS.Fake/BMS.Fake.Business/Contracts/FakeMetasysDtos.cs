namespace BMS.Fake.Business.Contracts;

/// <summary>DTO tòa nhà mà Fake Metasys trả về qua catalog API.</summary>
public sealed record BmsBuildingDto(
    // Mã ổn định của tòa nhà.
    string BuildingCode,
    // Tên hiển thị.
    string Name,
    // Mã/tên tòa nhà từ source.
    string SourceBuilding,
    // Mô tả fixture.
    string Description);

/// <summary>DTO thiết bị mà Fake Metasys trả về qua catalog API.</summary>
public sealed record BmsEquipmentDto(
    // Mã ổn định của thiết bị.
    string EquipmentCode,
    // Tên hiển thị.
    string Name,
    // Loại thiết bị.
    string EquipmentType,
    // Mã tòa nhà chứa thiết bị.
    string BuildingCode,
    // Mô tả fixture.
    string Description);

/// <summary>
/// DTO trạng thái hiện tại của một point trong bộ nhớ Fake Metasys.
/// Đây là state nội bộ của simulator, không phải entity database.
/// </summary>
public sealed class MetasysPointDto(string objectId)
{
    // ID ổn định của point.
    public string ObjectId { get; } = objectId;

    // Các metadata cố định của point.
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Building { get; init; } = "";
    public string EquipmentCode { get; init; } = "";
    public string Unit { get; init; } = "";

    // Value và Timestamp thay đổi trong lúc simulator chạy.
    public decimal Value { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>Tạo bản copy để caller không sửa trực tiếp state trong store.</summary>
    public MetasysPointDto Copy() => new(ObjectId)
    {
        ObjectName = ObjectName,
        ObjectType = ObjectType,
        Building = Building,
        EquipmentCode = EquipmentCode,
        Value = Value,
        Unit = Unit,
        Timestamp = Timestamp
    };
}

/// <summary>Request body dùng để tạo một COV subscription.</summary>
public sealed class SubscriptionRequestDto
{
    // Danh sách ObjectId mà client muốn theo dõi.
    public List<string> ObjectIds { get; init; } = [];
}

/// <summary>Response trả về ID của subscription vừa tạo.</summary>
public sealed class SubscriptionResponseDto
{
    // ID dùng để mở SSE stream.
    public string SubscriptionId { get; init; } = "";
}
