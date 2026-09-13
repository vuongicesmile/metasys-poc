using BmsIngestionApp.Models;

namespace BmsIngestionApp.Abstractions;

public interface IBmsReadingRepository
{
    Task InsertAsync(CovEvent covEvent, CancellationToken cancellationToken = default);
    Task PersistCatalogAsync(IReadOnlyList<BmsBuilding> buildings,
        IReadOnlyList<BmsEquipment> equipment, CancellationToken cancellationToken = default);
}
