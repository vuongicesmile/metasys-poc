using BMS.Ingestion.Domain.Models;

namespace BMS.Ingestion.Business.Abstractions;

/// <summary>Business-facing persistence port; SQL commands stay in DataAccess.</summary>
public interface IBmsReadingRepository
{
    Task InsertAsync(CovEvent covEvent, CancellationToken cancellationToken = default);
    Task PersistCatalogAsync(
        IReadOnlyList<BmsBuilding> buildings,
        IReadOnlyList<BmsEquipment> equipment,
        CancellationToken cancellationToken = default);
}
