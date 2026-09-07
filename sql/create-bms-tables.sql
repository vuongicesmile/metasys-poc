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
        reading_time DATETIME2 NOT NULL,
        reading_value DECIMAL(18,4) NULL,
        unit VARCHAR(50) NULL,
        source_system VARCHAR(100) NOT NULL,
        ingested_at DATETIME2 NOT NULL
            CONSTRAINT DF_raw_bms_reading_ingested_at DEFAULT GETDATE()
    );
END
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
