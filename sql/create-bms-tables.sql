USE master;
GO

IF DB_ID('FM_Central') IS NULL
BEGIN
    CREATE DATABASE FM_Central;
END
GO

USE FM_Central;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'raw')
BEGIN
    EXEC('CREATE SCHEMA raw');
END
GO

IF OBJECT_ID('raw.bms_building', 'U') IS NULL
BEGIN
    CREATE TABLE raw.bms_building
    (
        building_code VARCHAR(50) NOT NULL CONSTRAINT PK_raw_bms_building PRIMARY KEY,
        name NVARCHAR(200) NOT NULL,
        source_building VARCHAR(100) NOT NULL,
        description NVARCHAR(2000) NULL,
        source_updated_at DATETIME2 NOT NULL,
        ingested_at DATETIME2 NOT NULL CONSTRAINT DF_raw_bms_building_ingested_at DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('raw.bms_equipment', 'U') IS NULL
BEGIN
    CREATE TABLE raw.bms_equipment
    (
        equipment_code VARCHAR(100) NOT NULL CONSTRAINT PK_raw_bms_equipment PRIMARY KEY,
        name NVARCHAR(200) NOT NULL,
        equipment_type VARCHAR(50) NOT NULL,
        building_code VARCHAR(50) NOT NULL,
        description NVARCHAR(2000) NULL,
        source_updated_at DATETIME2 NOT NULL,
        ingested_at DATETIME2 NOT NULL CONSTRAINT DF_raw_bms_equipment_ingested_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_raw_bms_equipment_building FOREIGN KEY(building_code)
            REFERENCES raw.bms_building(building_code)
    );
END
GO

IF OBJECT_ID('raw.bms_reading', 'U') IS NULL
BEGIN
    CREATE TABLE raw.bms_reading
    (
        id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_raw_bms_reading PRIMARY KEY,
        object_id VARCHAR(100) NOT NULL,
        object_name VARCHAR(200) NULL,
        object_type VARCHAR(100) NULL,
        building VARCHAR(100) NULL,
        equipment_code VARCHAR(100) NULL,
        reading_time DATETIME2 NOT NULL,
        reading_value DECIMAL(18,4) NULL,
        unit VARCHAR(50) NULL,
        source_system VARCHAR(100) NOT NULL,
        ingested_at DATETIME2 NOT NULL
            CONSTRAINT DF_raw_bms_reading_ingested_at DEFAULT GETDATE()
    );
END
GO

IF COL_LENGTH('raw.bms_reading', 'equipment_code') IS NULL
    ALTER TABLE raw.bms_reading ADD equipment_code VARCHAR(100) NULL;
GO

-- Deterministic compatibility backfill for the five POC points. Catalog rows are
-- ingested from FakeMetasysApi; this only fills the relationship on old readings.
UPDATE raw.bms_reading
SET equipment_code = CASE object_id
    WHEN 'WATER-001' THEN 'EQ-A-WM-001'
    WHEN 'WATER-002' THEN 'EQ-B-WM-002'
    WHEN 'TEMP-001' THEN 'EQ-A-TS-001'
    WHEN 'TEST-POWER-AUTOMATE-001' THEN 'EQ-TEST-RIG-001'
    WHEN 'TEST-POWER-AUTOMATE-002' THEN 'EQ-TEST-RIG-001'
END
WHERE equipment_code IS NULL
  AND object_id IN ('WATER-001','WATER-002','TEMP-001','TEST-POWER-AUTOMATE-001','TEST-POWER-AUTOMATE-002');
GO

-- Non-destructive compatibility migration for an earlier wide-table POC.
-- Existing legacy columns and rows are retained.
IF COL_LENGTH('raw.bms_reading', 'object_id') IS NULL
BEGIN
    ALTER TABLE raw.bms_reading ADD object_id VARCHAR(100) NULL;

    IF COL_LENGTH('raw.bms_reading', 'meter_id') IS NOT NULL
    BEGIN
        EXEC('
            UPDATE raw.bms_reading
            SET object_id = COALESCE(NULLIF(meter_id, ''''), CONCAT(''LEGACY-'', id))
            WHERE object_id IS NULL;
        ');
    END
    ELSE
    BEGIN
        EXEC('
            UPDATE raw.bms_reading
            SET object_id = CONCAT(''LEGACY-'', id)
            WHERE object_id IS NULL;
        ');
    END

    ALTER TABLE raw.bms_reading ALTER COLUMN object_id VARCHAR(100) NOT NULL;
END
GO

IF COL_LENGTH('raw.bms_reading', 'object_name') IS NULL
BEGIN
    ALTER TABLE raw.bms_reading ADD object_name VARCHAR(200) NULL;

    IF COL_LENGTH('raw.bms_reading', 'meter_id') IS NOT NULL
        EXEC('UPDATE raw.bms_reading SET object_name = meter_id WHERE object_name IS NULL;');
END
GO

IF COL_LENGTH('raw.bms_reading', 'object_type') IS NULL
BEGIN
    ALTER TABLE raw.bms_reading ADD object_type VARCHAR(100) NULL;

    IF COL_LENGTH('raw.bms_reading', 'water_consumption') IS NOT NULL
        EXEC('UPDATE raw.bms_reading SET object_type = ''WaterConsumption'' WHERE object_type IS NULL AND water_consumption IS NOT NULL;');
    IF COL_LENGTH('raw.bms_reading', 'water_flow') IS NOT NULL
        EXEC('UPDATE raw.bms_reading SET object_type = ''Flow'' WHERE object_type IS NULL AND water_flow IS NOT NULL;');
    IF COL_LENGTH('raw.bms_reading', 'temperature') IS NOT NULL
        EXEC('UPDATE raw.bms_reading SET object_type = ''Temperature'' WHERE object_type IS NULL AND temperature IS NOT NULL;');
END
GO

IF COL_LENGTH('raw.bms_reading', 'reading_value') IS NULL
BEGIN
    ALTER TABLE raw.bms_reading ADD reading_value DECIMAL(18,4) NULL;

    IF COL_LENGTH('raw.bms_reading', 'water_consumption') IS NOT NULL
        EXEC('UPDATE raw.bms_reading SET reading_value = water_consumption WHERE reading_value IS NULL AND water_consumption IS NOT NULL;');
    IF COL_LENGTH('raw.bms_reading', 'water_flow') IS NOT NULL
        EXEC('UPDATE raw.bms_reading SET reading_value = water_flow WHERE reading_value IS NULL AND water_flow IS NOT NULL;');
    IF COL_LENGTH('raw.bms_reading', 'temperature') IS NOT NULL
        EXEC('UPDATE raw.bms_reading SET reading_value = temperature WHERE reading_value IS NULL AND temperature IS NOT NULL;');
END
GO

IF COL_LENGTH('raw.bms_reading', 'unit') IS NULL
BEGIN
    ALTER TABLE raw.bms_reading ADD unit VARCHAR(50) NULL;

    EXEC('
        UPDATE raw.bms_reading
        SET unit = CASE object_type
            WHEN ''WaterConsumption'' THEN ''m3''
            WHEN ''Flow'' THEN ''L/s''
            WHEN ''Temperature'' THEN ''C''
            ELSE NULL
        END
        WHERE unit IS NULL;
    ');
END
GO

-- Normalize shared columns to the Plan 2.0 contract without removing legacy columns.
UPDATE raw.bms_reading
SET reading_time = COALESCE(reading_time, ingested_at, GETDATE())
WHERE reading_time IS NULL;

UPDATE raw.bms_reading
SET source_system = COALESCE(NULLIF(source_system, ''), 'Legacy BMS')
WHERE source_system IS NULL OR source_system = '';

ALTER TABLE raw.bms_reading ALTER COLUMN reading_time DATETIME2 NOT NULL;
ALTER TABLE raw.bms_reading ALTER COLUMN source_system VARCHAR(100) NOT NULL;
GO
