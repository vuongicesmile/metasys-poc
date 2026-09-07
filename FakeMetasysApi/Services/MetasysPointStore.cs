using FakeMetasysApi.Models;

namespace FakeMetasysApi.Services;

public sealed class MetasysPointStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, MetasysPoint> _points = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WATER-001"] = NewPoint("WATER-001", "Main Water Meter", "WaterConsumption", "Building A", 350.00m, "m3"),
        ["WATER-002"] = NewPoint("WATER-002", "Secondary Water Meter", "WaterConsumption", "Building B", 220.00m, "m3"),
        ["TEMP-001"] = NewPoint("TEMP-001", "Room Temperature", "Temperature", "Building A", 24.50m, "C")
    };

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

    public CovEvent ChangeValue(string objectId, decimal newValue, DateTime timestamp)
    {
        lock (_sync)
        {
            var point = _points[objectId];
            var previousValue = point.Value;
            point.Value = newValue;
            point.Timestamp = timestamp;

            return new CovEvent
            {
                ObjectId = point.ObjectId,
                ObjectName = point.ObjectName,
                ObjectType = point.ObjectType,
                Building = point.Building,
                PreviousValue = previousValue,
                CurrentValue = point.Value,
                Unit = point.Unit,
                Timestamp = point.Timestamp
            };
        }
    }

    private static MetasysPoint NewPoint(
        string id,
        string name,
        string type,
        string building,
        decimal value,
        string unit) => new()
    {
        ObjectId = id,
        ObjectName = name,
        ObjectType = type,
        Building = building,
        Value = value,
        Unit = unit,
        Timestamp = DateTime.UtcNow
    };
}
