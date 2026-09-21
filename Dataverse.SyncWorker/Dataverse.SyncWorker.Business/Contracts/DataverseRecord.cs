namespace DataverseSyncWorker.Contracts;

/// <summary>
/// Bản ghi trung gian thuần .NET mà Application tạo ra trước khi ghi Dataverse.
/// Kiểu này giúp Business không phụ thuộc Microsoft.Xrm.Sdk.
/// </summary>
public sealed class DataverseRecord(string logicalName, Guid id)
{
    public string LogicalName { get; } = logicalName;
    public Guid Id { get; } = id;
    public IDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public object? this[string name]
    {
        get => Attributes[name];
        set => Attributes[name] = value;
    }

    public T? Get<T>(string name) =>
        Attributes.TryGetValue(name, out var value) && value is T typed ? typed : default;
}

/// <summary>Lookup thuần .NET; DataAccess sẽ đổi thành EntityReference.</summary>
public sealed record DataverseReference(string LogicalName, Guid Id);

/// <summary>Choice thuần .NET; DataAccess sẽ đổi thành OptionSetValue.</summary>
public sealed record DataverseChoice(int Value);
