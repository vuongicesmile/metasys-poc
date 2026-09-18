using BMS.Ingestion.Domain.Models;

namespace BMS.Fake.Business.Abstractions;

/// <summary>Business-facing port for source point state.</summary>
public interface IMetasysPointStore
{
    IReadOnlyList<BmsBuilding> GetBuildings();
    IReadOnlyList<BmsEquipment> GetEquipment();
    IReadOnlyList<MetasysPoint> GetAll();
    MetasysPoint? Get(string objectId);
    bool Exists(string objectId);
    CovEvent ChangeValue(string objectId, decimal newValue, DateTime timestamp);
}
