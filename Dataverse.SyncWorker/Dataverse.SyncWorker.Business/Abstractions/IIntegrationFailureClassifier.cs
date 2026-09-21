namespace DataverseSyncWorker.Abstractions;

/// <summary>
/// Phân loại lỗi integration mà không để Business phụ thuộc SDK của nhà cung cấp.
/// </summary>
public interface IIntegrationFailureClassifier
{
    bool IsTransient(Exception exception);
    bool IsPermanent(Exception exception);
}
