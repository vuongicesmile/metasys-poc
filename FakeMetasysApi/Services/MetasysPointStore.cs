using FakeMetasysApi.Models;

namespace FakeMetasysApi.Services;

public sealed class MetasysPointStore
{
    private static readonly BmsBuilding[] Buildings =
    [
        new("BLDG-A", "Building A", "Building A", "Building in the Fake Metasys BMS fixture."),
        new("BLDG-B", "Building B", "Building B", "Building in the Fake Metasys BMS fixture."),
        new("BLDG-TEST", "Test Building", "Test Building", "Building for POC relationship test points.")
    ];
    private static readonly BmsEquipment[] Equipment =
    [
        new("EQ-A-WM-001", "Main Water Meter A", "WaterMeter", "BLDG-A", "Fake water meter equipment."),
        new("EQ-B-WM-002", "Secondary Water Meter B", "WaterMeter", "BLDG-B", "Fake water meter equipment."),
        new("EQ-A-TS-001", "Room Temperature Sensor A", "TemperatureSensor", "BLDG-A", "Fake temperature sensor equipment."),
        new("EQ-TEST-RIG-001", "BMS Relationship Test Rig", "TestRig", "BLDG-TEST", "Fake test rig with two points for a 1:N demo.")
    ];
    private readonly object _sync = new();
    private readonly Dictionary<string, MetasysPoint> _points = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WATER-001"] = NewPoint("WATER-001", "Main Water Meter", "WaterConsumption", "Building A", "EQ-A-WM-001", 350.00m, "m3"),
        ["WATER-002"] = NewPoint("WATER-002", "Secondary Water Meter", "WaterConsumption", "Building B", "EQ-B-WM-002", 220.00m, "m3"),
        ["TEMP-001"] = NewPoint("TEMP-001", "Room Temperature", "Temperature", "Building A", "EQ-A-TS-001", 24.50m, "C"),
        ["TEST-POWER-AUTOMATE-001"] = NewPoint("TEST-POWER-AUTOMATE-001", "Power Automate Test Point 1", "Temperature", "Test Building", "EQ-TEST-RIG-001", 25.1234m, "C"),
        ["TEST-POWER-AUTOMATE-002"] = NewPoint("TEST-POWER-AUTOMATE-002", "Power Automate Test Point 2", "Temperature", "Test Building", "EQ-TEST-RIG-001", 26.2345m, "C")
    };

    public IReadOnlyList<BmsBuilding> GetBuildings() => Buildings;
    public IReadOnlyList<BmsEquipment> GetEquipment() => Equipment;

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
                EquipmentCode = point.EquipmentCode,
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
        string equipmentCode,
        decimal value,
        string unit) => new()
    {
        ObjectId = id,
        ObjectName = name,
        ObjectType = type,
        Building = building,
        EquipmentCode = equipmentCode,
        Value = value,
        Unit = unit,
        Timestamp = DateTime.UtcNow
    };
}
