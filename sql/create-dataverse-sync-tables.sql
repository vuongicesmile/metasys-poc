USE FM_Central;
GO
IF SCHEMA_ID('integration') IS NULL EXEC('CREATE SCHEMA integration');
GO
IF OBJECT_ID('integration.dataverse_delivery','U') IS NULL
CREATE TABLE integration.dataverse_delivery
(
    pipeline varchar(150) NOT NULL,
    reading_id bigint NOT NULL,
    current_done bit NOT NULL DEFAULT 0,
    history_done bit NOT NULL DEFAULT 0,
    delivered_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_dataverse_delivery PRIMARY KEY(pipeline, reading_id)
);
GO
IF OBJECT_ID('integration.dataverse_sync_state','U') IS NULL
CREATE TABLE integration.dataverse_sync_state
(
    pipeline_name varchar(150) NOT NULL PRIMARY KEY,
    last_successful_id bigint NOT NULL DEFAULT 0,
    last_completed_at datetime2 NULL,
    updated_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF OBJECT_ID('integration.dataverse_dead_letter','U') IS NULL
CREATE TABLE integration.dataverse_dead_letter
(
    pipeline varchar(150) NOT NULL,
    bms_reading_id bigint NOT NULL,
    target_table varchar(100) NOT NULL,
    payload_json nvarchar(max) NOT NULL,
    error_message nvarchar(2000) NOT NULL,
    attempt_count int NOT NULL DEFAULT 1,
    first_failed_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    last_failed_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    resolved_at datetime2 NULL,
    CONSTRAINT PK_dataverse_dead_letter PRIMARY KEY(pipeline, bms_reading_id)
);
GO
