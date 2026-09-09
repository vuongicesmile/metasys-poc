USE [FM_Central];
GO

-- Idempotent demo fixture matching FakeMetasysApi. The normal runtime path is
-- Fake API -> BmsIngestionApp -> these SQL catalog tables.
MERGE raw.bms_building AS target
USING (VALUES
    ('BLDG-A',    N'Building A',    'Building A',    N'Building in the Fake Metasys BMS fixture.'),
    ('BLDG-B',    N'Building B',    'Building B',    N'Building in the Fake Metasys BMS fixture.'),
    ('BLDG-TEST', N'Test Building', 'Test Building', N'Building for POC relationship test points.')
) AS source(building_code,name,source_building,description)
ON target.building_code=source.building_code
WHEN MATCHED THEN UPDATE SET name=source.name,source_building=source.source_building,
    description=source.description,source_updated_at=SYSUTCDATETIME(),ingested_at=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT(building_code,name,source_building,description,source_updated_at)
    VALUES(source.building_code,source.name,source.source_building,source.description,SYSUTCDATETIME());
GO

MERGE raw.bms_equipment AS target
USING (VALUES
    ('EQ-A-WM-001',     N'Main Water Meter A',        'WaterMeter',        'BLDG-A',    N'Fake water meter equipment.'),
    ('EQ-B-WM-002',     N'Secondary Water Meter B',   'WaterMeter',        'BLDG-B',    N'Fake water meter equipment.'),
    ('EQ-A-TS-001',     N'Room Temperature Sensor A', 'TemperatureSensor', 'BLDG-A',    N'Fake temperature sensor equipment.'),
    ('EQ-TEST-RIG-001', N'BMS Relationship Test Rig', 'TestRig',           'BLDG-TEST', N'Fake test rig with two points for a 1:N demo.')
) AS source(equipment_code,name,equipment_type,building_code,description)
ON target.equipment_code=source.equipment_code
WHEN MATCHED THEN UPDATE SET name=source.name,equipment_type=source.equipment_type,
    building_code=source.building_code,description=source.description,
    source_updated_at=SYSUTCDATETIME(),ingested_at=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT(equipment_code,name,equipment_type,building_code,description,source_updated_at)
    VALUES(source.equipment_code,source.name,source.equipment_type,source.building_code,source.description,SYSUTCDATETIME());
GO

UPDATE raw.bms_reading
SET equipment_code=CASE object_id
    WHEN 'WATER-001' THEN 'EQ-A-WM-001'
    WHEN 'WATER-002' THEN 'EQ-B-WM-002'
    WHEN 'TEMP-001' THEN 'EQ-A-TS-001'
    WHEN 'TEST-POWER-AUTOMATE-001' THEN 'EQ-TEST-RIG-001'
    WHEN 'TEST-POWER-AUTOMATE-002' THEN 'EQ-TEST-RIG-001'
END
WHERE object_id IN ('WATER-001','WATER-002','TEMP-001','TEST-POWER-AUTOMATE-001','TEST-POWER-AUTOMATE-002');
GO

SELECT (SELECT COUNT(*) FROM raw.bms_building) AS buildings,
       (SELECT COUNT(*) FROM raw.bms_equipment) AS equipment,
       (SELECT COUNT(DISTINCT object_id) FROM raw.bms_reading WHERE equipment_code IS NOT NULL) AS mapped_points;
GO
