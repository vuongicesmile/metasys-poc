namespace DataverseSyncWorker.Contracts;

/// <summary>
/// Bản ghi trung gian thuần .NET mà Application tạo ra trước khi ghi Dataverse.
/// Kiểu này giúp Business không phụ thuộc Microsoft.Xrm.Sdk.
/// </summary>
public sealed class DataverseRecord(string logicalName, Guid id)
{
    /// <summary>Tên logical của bảng đích, ví dụ fmc_bmspoint.</summary>
    public string LogicalName { get; } = logicalName;

    /// <summary>Khóa chính xác định trước để retry không tạo bản ghi trùng.</summary>
    public Guid Id { get; } = id;

    /// <summary>Các cột và giá trị cần gửi sang Dataverse.</summary>
    public IDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    /// <summary>Cho phép mapper gán cột bằng cú pháp record["tên_cột"].</summary>
    public object? this[string name]
    {
        get => Attributes[name];
        set => Attributes[name] = value;
    }

    /// <summary>Đọc một cột theo kiểu mong muốn; trả default khi cột không tồn tại hoặc sai kiểu.</summary>
    public T? Get<T>(string name) =>
        Attributes.TryGetValue(name, out var value) && value is T typed ? typed : default;
}

/// <summary>Lookup thuần .NET; DataAccess sẽ đổi thành EntityReference.</summary>
public sealed record DataverseReference(string LogicalName, Guid Id);

/// <summary>Choice thuần .NET; DataAccess sẽ đổi thành OptionSetValue.</summary>
public sealed record DataverseChoice(int Value);
