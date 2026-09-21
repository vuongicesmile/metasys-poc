using BMS.Ingestion.Business.Contracts;

namespace BMS.Ingestion.Business.Abstractions;

/// <summary>Cổng persistence cho catalog tòa nhà và thiết bị từ hệ thống nguồn.</summary>
public interface IBmsCatalogRepository
{
    /// <summary>Upsert một snapshot catalog trong một transaction duy nhất.</summary>
    Task PersistCatalogAsync(
        // DTO catalog được truyền vào thay vì EF entity.
        BmsCatalogDto catalog,
        // Cho phép hủy thao tác khi ứng dụng dừng.
        CancellationToken cancellationToken = default);
}
