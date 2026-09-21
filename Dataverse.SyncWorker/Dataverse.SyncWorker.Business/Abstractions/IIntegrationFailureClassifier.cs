namespace DataverseSyncWorker.Abstractions;

/// <summary>
/// Phân loại lỗi integration mà không để Business phụ thuộc SDK của nhà cung cấp.
/// </summary>
public interface IIntegrationFailureClassifier
{
    /// <summary>Lỗi tạm thời có thể retry, ví dụ timeout hoặc Dataverse throttling.</summary>
    bool IsTransient(Exception exception);

    /// <summary>Lỗi cố định cần người vận hành sửa cấu hình, quyền hoặc schema.</summary>
    bool IsPermanent(Exception exception);
}
