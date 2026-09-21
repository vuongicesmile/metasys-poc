using BMS.Ingestion.Business.Contracts;

namespace BMS.Ingestion.Business.Abstractions;

/// <summary>Cổng persistence cho catalog tòa nhà và thiết bị từ hệ thống nguồn.</summary>
public interface IBmsCatalogRepository
{
    /// <summary>Stage việc upsert một snapshot catalog trong Unit of Work hiện tại.</summary>
    Task StageAsync(
        // DTO catalog được truyền vào thay vì EF entity.
        BmsCatalogDto catalog,
        // Cho phép hủy thao tác khi ứng dụng dừng.
        CancellationToken cancellationToken = default);
}
