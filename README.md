# Metasys COV POC 2.0

## Documentation

Start at the [documentation index](docs/README.md):

- [Power Automate integration plan and pilot receipt](docs/plans/power-automate-sql-to-dataverse.vi.md)
- [Power Automate trigger operating runbook](docs/runbooks/power-automate-sql-sync.vi.md)
- [Custom API + Power Automate UI demo](docs/runbooks/custom-api-bms-sync-demo.vi.md)
- [Custom API implementation receipt](docs/plans/custom-api-request-bms-sync.vi.md)
- [Interactive Custom API lab](docs/interactive/custom-api-lab/index.html)
- [SQL-to-Dataverse operating runbook](docs/runbooks/sql-to-dataverse-runbook.vi.md)
- [Dataverse deployment and implemented contract](docs/reference/dataverse-deployment.md)
- [FMC BMS Demo model-driven app runbook (Vietnamese)](docs/runbooks/model-driven-app-demo.vi.md)
- [Original Plan 3.0 — historical design](docs/plans/plan-3.0-sql-to-dataverse.md)

## SQL to Dataverse (Plan 3.0)

`DataverseSyncWorker` is implemented at `http://localhost:5300/swagger`.
The `FMC BMS Demo` model-driven app also exposes **Power Automate Demo**. Its
**Request BMS Sync** button calls `fmc_RequestBmsSync`, queues/reuses one
`fmc_syncrequest`, and raises the `FMC - App Request BMS Sync Event` cloud flow.
Double-click [START-SQL-TO-DATAVERSE.cmd](START-SQL-TO-DATAVERSE.cmd) to start
the command worker, then run `FMC - Request SQL to Dataverse Sync` in Power
Automate. The worker claims queued requests and drains their SQL cutoff; keep
its window open and use Ctrl+C to stop until a production service identity is installed.
See the [repeatable setup and operating steps (Vietnamese)](docs/runbooks/sql-to-dataverse-runbook.vi.md).
Running `dotnet run --project .\DataverseSyncWorker` also starts the command worker.
For this Developer environment it reuses the authenticated Azure CLI bundled by
`rmit-fm-data`; no secret is copied into this repository. The Dataverse schema
has been provisioned, the initial SQL backlog has been delivered, and live rows
were reconciled. See [Dataverse deployment](docs/reference/dataverse-deployment.md) for commands,
production identity guidance, and tests.

### Run SQL to Dataverse

1. Start the command worker and keep its window open:

   ```powershell
   .\START-SQL-TO-DATAVERSE.cmd
   ```

2. Insert or ingest a new row into `FM_Central.raw.bms_reading`. Do not supply
   `id`; SQL Server generates the identity used for deterministic Dataverse
   delivery.

   ```sql
   USE [FM_Central];

   INSERT INTO raw.bms_reading
   (
       object_id, object_name, object_type, building, equipment_code,
       reading_time, reading_value, unit, source_system
   )
   OUTPUT INSERTED.id, INSERTED.object_id, INSERTED.reading_time, INSERTED.reading_value
   VALUES
   (
       'TEST-POWER-AUTOMATE-001', 'Power Automate Test Point',
       'Temperature', 'Test Building', 'EQ-TEST-RIG-001', SYSUTCDATETIME(),
       CAST(25.1234 AS DECIMAL(18,4)), 'C', 'Fake Metasys COV'
   );
   ```

3. In Power Automate, open solution `FMCentralBms` and run
   `FMC - Request SQL to Dataverse Sync`. The flow queues one
   `fmc_syncrequest`; the worker claims it, upserts SQL catalog into
   `fmc_bmsbuilding` and `fmc_bmsequipment`, updates Point lookups, then writes
   the SQL reading cutoff to `fmc_bmspoint` and `fmc_bmsreading`.

4. Check worker and delivery state:

   ```powershell
   Invoke-RestMethod http://localhost:5300/api/dataverse-sync/status
   dotnet run --project .\DataverseSyncWorker -- --verify
   ```

   A completed run has `pendingRows = 0` and `deadLetterRows = 0`. A successful
   request with `deliveredRows = 0` means there was no new SQL row before its
   cutoff. If a Power App reads the separate Silver table
   `cr3c8_fmc_silver_bmspoint`, allow or trigger its `BMS DataFlow` refresh
   after Bronze delivery.

Power Automate queues the request; it does not connect to local SQL directly.
The worker must therefore be running, or installed as the Windows Service
described in the [Power Automate runbook](docs/runbooks/power-automate-sql-sync.vi.md).

POC data flow:

```text
Fake Metasys API -> catalog + COV over SSE -> .NET ingestion -> SQL Server
                                    raw.bms_building / raw.bms_equipment / raw.bms_reading
```

## Prerequisites

- .NET 10 SDK
- SQL Server reachable from the ingestion app

## 1. Create the database objects

Run `sql/create-bms-tables.sql` against the target SQL Server. The script is
idempotent and creates the database, schema, and table when missing. If it finds
the earlier wide-table POC schema, it preserves its rows/columns and backfills
the generic Plan 2.0 columns.

Update `BmsIngestionApp/appsettings.json` if the SQL Server connection string is
different from the local Windows-authenticated default.

## 2. Start the fake Metasys API

```powershell
dotnet run --project .\FakeMetasysApi
```

The API listens on `http://localhost:5100` and exposes:

- Swagger UI: `http://localhost:5100/swagger`
- OpenAPI JSON: `http://localhost:5100/swagger/v1/swagger.json`

- `GET /api/metasys/objects`
- `GET /api/metasys/buildings`
- `GET /api/metasys/equipment`
- `GET /api/metasys/objects/{objectId}`
- `POST /api/metasys/subscriptions`
- `GET /api/metasys/subscriptions/{subscriptionId}/stream`

## 3. Start ingestion

In a second terminal:

```powershell
dotnet run --project .\BmsIngestionApp
```

The ingestion service listens on `http://localhost:5200`:

- Swagger UI: `http://localhost:5200/swagger`
- OpenAPI JSON: `http://localhost:5200/swagger/v1/swagger.json`
- Runtime status: `GET /api/ingestion/status`
- Recent COV events: `GET /api/ingestion/events/recent`

For an API/SSE-only smoke test that does not connect to SQL Server:

```powershell
dotnet run --project .\BmsIngestionApp -- --no-sql
```

The simulator updates the five in-memory BMS points every three seconds. Ingestion
upserts the 3-building/4-equipment catalog at startup. Only
actual value changes produce COV events, and each received event is inserted as
one row when SQL persistence is enabled.

## Verify SQL data

```sql
SELECT TOP (100) *
FROM FM_Central.raw.bms_reading
ORDER BY id DESC;

SELECT * FROM FM_Central.raw.bms_building ORDER BY building_code;
SELECT * FROM FM_Central.raw.bms_equipment ORDER BY equipment_code;
```

## Project agent skills

[AGENTS.md](AGENTS.md) defines this project's Power Platform workflow and routes
to 13 local skills in [.agents/skills](.agents/skills). They are adapted from the
skills used by `rmit-fm-data`, including its bundled Dataverse skill set, for this
repository's .NET/PAC tooling, `FMCentralBms` solution and SQL history contract.
The source locations and adaptation details are recorded in `AGENTS.md`.
