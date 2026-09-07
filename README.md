# Metasys COV POC 2.0

## SQL to Dataverse (Plan 3.0)

`DataverseSyncWorker` is implemented at `http://localhost:5300/swagger`.
Run `dotnet run --project .\DataverseSyncWorker` to view SQL backlog/status.
For this Developer environment it reuses the authenticated Azure CLI bundled by
`rmit-fm-data`; no secret is copied into this repository. The Dataverse schema
has been provisioned, the initial SQL backlog has been delivered, and live rows
were reconciled. See [Dataverse deployment](dataverse/README.md) for commands,
production identity guidance, and tests.

POC data flow:

```text
Fake Metasys API -> COV over SSE -> .NET ingestion -> SQL Server
                                             FM_Central.raw.bms_reading
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

The simulator updates the three in-memory BMS points every three seconds. Only
actual value changes produce COV events, and each received event is inserted as
one row when SQL persistence is enabled.

## Verify SQL data

```sql
SELECT TOP (100) *
FROM FM_Central.raw.bms_reading
ORDER BY id DESC;
```
