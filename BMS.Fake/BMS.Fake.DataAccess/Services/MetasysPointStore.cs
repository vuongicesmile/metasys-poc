using BMS.Ingestion.Domain.Models;
using BMS.Fake.Business.Abstractions;
using BMS.Fake.DataAccess.Simulation;

namespace BMS.Fake.DataAccess.Services;

/// <summary>In-memory source adapter that owns mutable simulator state.</summary>
public sealed class MetasysPointStore : IMetasysPointStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, MetasysPoint> _points = MetasysFixture.CreatePoints();

    public IReadOnlyList<BmsBuilding> GetBuildings() => MetasysFixture.Buildings;

    public IReadOnlyList<BmsEquipment> GetEquipment() => MetasysFixture.Equipment;

    public IReadOnlyList<MetasysPoint> GetAll()
    {
        lock (_sync)
        {
            return _points.Values
                .OrderBy(point => point.ObjectId)
                .Select(point => point.Copy())
                .ToArray();
        }
    }

    public MetasysPoint? Get(string objectId)
    {
        lock (_sync)
        {
            return _points.TryGetValue(objectId, out var point) ? point.Copy() : null;
        }
    }

    public bool Exists(string objectId)
    {
        lock (_sync)
        {
            return _points.ContainsKey(objectId);
        }
    }

    public CovEvent ChangeValue(string objectId, decimal newValue, DateTime timestamp)
    {
        lock (_sync)
        {
            if (!_points.TryGetValue(objectId, out var point))
                throw new KeyNotFoundException($"BMS point '{objectId}' was not found.");

            var previousValue = point.Value;
            point.Value = newValue;
            point.Timestamp = timestamp;

            return new CovEvent
            {
                ObjectId = point.ObjectId,
                ObjectName = point.ObjectName,
                ObjectType = point.ObjectType,
                Building = point.Building,
                EquipmentCode = point.EquipmentCode,
                PreviousValue = previousValue,
                CurrentValue = point.Value,
                Unit = point.Unit,
                Timestamp = point.Timestamp
            };
        }
    }
}
