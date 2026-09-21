using BMS.Fake.Business.Contracts;

namespace BMS.Fake.DataAccess.Simulation;

/// <summary>Fixture deterministic được adapter simulator sở hữu.</summary>
internal static class MetasysFixture
{
    // Catalog building cố định để mọi lần chạy có cùng dữ liệu đầu vào.
    public static IReadOnlyList<BmsBuildingDto> Buildings { get; } =
    [
        new("BLDG-A", "Building A", "Building A", "Building in the Fake Metasys BMS fixture."),
        new("BLDG-B", "Building B", "Building B", "Building in the Fake Metasys BMS fixture."),
        new("BLDG-TEST", "Test Building", "Test Building", "Building for POC relationship test points.")
    ];

    // Catalog equipment cố định; BuildingCode tạo quan hệ tới Buildings.
    public static IReadOnlyList<BmsEquipmentDto> Equipment { get; } =
    [
        new("EQ-A-WM-001", "Main Water Meter A", "WaterMeter", "BLDG-A", "Fake water meter equipment."),
        new("EQ-B-WM-002", "Secondary Water Meter B", "WaterMeter", "BLDG-B", "Fake water meter equipment."),
        new("EQ-A-TS-001", "Room Temperature Sensor A", "TemperatureSensor", "BLDG-A", "Fake temperature sensor equipment."),
        new("EQ-TEST-RIG-001", "BMS Relationship Test Rig", "TestRig", "BLDG-TEST", "Fake test rig with two points for a 1:N demo.")
    ];

    // Tạo dictionary state mới cho mỗi MetasysPointStore.
    public static Dictionary<string, MetasysPointDto> CreatePoints() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["WATER-001"] = NewPoint("WATER-001", "Main Water Meter", "WaterConsumption", "Building A", "EQ-A-WM-001", 350.00m, "m3"),
            ["WATER-002"] = NewPoint("WATER-002", "Secondary Water Meter", "WaterConsumption", "Building B", "EQ-B-WM-002", 220.00m, "m3"),
            ["TEMP-001"] = NewPoint("TEMP-001", "Room Temperature", "Temperature", "Building A", "EQ-A-TS-001", 24.50m, "C"),
            ["TEST-POWER-AUTOMATE-001"] = NewPoint("TEST-POWER-AUTOMATE-001", "Power Automate Test Point 1", "Temperature", "Test Building", "EQ-TEST-RIG-001", 25.1234m, "C"),
            ["TEST-POWER-AUTOMATE-002"] = NewPoint("TEST-POWER-AUTOMATE-002", "Power Automate Test Point 2", "Temperature", "Test Building", "EQ-TEST-RIG-001", 26.2345m, "C")
        };

    // Helper tạo một point DTO với metadata và giá trị ban đầu.
    private static MetasysPointDto NewPoint(
        string id,
        string name,
        string type,
        string building,
        string equipmentCode,
        decimal value,
        string unit) => new(id)
        {
            ObjectName = name,
            ObjectType = type,
            Building = building,
            EquipmentCode = equipmentCode,
            Value = value,
            Unit = unit,
            Timestamp = DateTime.UtcNow
        };
}
