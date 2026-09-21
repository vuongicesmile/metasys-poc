namespace BMS.Ingestion.Business.Abstractions;

/// <summary>
/// Phối hợp các repository tham gia cùng một lần ghi nguyên tử của BMS ingestion.
/// Implementation sở hữu persistence session và giải phóng session bất đồng bộ.
/// </summary>
public interface IBmsIngestionUnitOfWork : IAsyncDisposable
{
    IBmsCatalogRepository Catalog { get; }

    IBmsReadingRepository Readings { get; }

    /// <summary>Ghi toàn bộ thay đổi đã stage trong Unit of Work.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Tạo Unit of Work ngắn hạn mà không làm lộ DI scope vào Business service.</summary>
public interface IBmsIngestionUnitOfWorkFactory
{
    ValueTask<IBmsIngestionUnitOfWork> CreateAsync(CancellationToken cancellationToken = default);
}
