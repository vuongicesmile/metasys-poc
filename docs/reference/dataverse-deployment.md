# FM Central BMS deployment

For daily operation, use the [Power Automate trigger runbook](../runbooks/power-automate-sql-sync.vi.md),
the [SQL-to-Dataverse technical runbook](../runbooks/sql-to-dataverse-runbook.vi.md)
and [command-worker launcher](../../START-SQL-TO-DATAVERSE.cmd).
Run mode waits for queued requests; Once processes only one batch directly.

Paths and commands below are relative to the repository root. These notes
describe the existing integration and the deployed Developer-environment pilot.

Target: `https://org06cbc9ec.crm5.dynamics.com/`
Organization: `ab191700-b99e-f111-aaa0-000d3a80bb96`
Environment: `5abcb0e5-99b2-e51f-aa0e-90d84405798b`

Implementation status: complete in the target Developer environment. The
`FMCentralBms` solution, BMS tables, `fmc_syncrequest`, keys, views, roles,
connection reference, environment variable, and activated cloud flow were
provisioned. On 2026-09-08 request `b4d35f3c-3fab-f111-aaad-00224819a344`
delivered 183 rows in two batches to cutoff 7404 with zero pending/dead-letter;
25 retained history samples were reconciled. This is a dated receipt, not a live
production-readiness claim. The exported
unmanaged solution is checked out at `dataverse/FMCentralBms`.

The Building/Equipment extension was verified on 2026-09-09. Request
`c99f3d86-1fac-f111-aaad-00224819a344` succeeded at SQL cutoff 23377 with zero
quarantined rows. Live verification passed for 3 Buildings, 4 Equipment, all 5
Point → Equipment → Building joins, and 25 retained history samples. Ingestion
continued after that cutoff, so later rows correctly remained pending for the
next request.

Local development reuses the signed-in Azure CLI bundled by `rmit-fm-data` via
`scripts/get-dataverse-token.py`. No access token or refresh token is stored in
this repository. Run the worker directly:

```powershell
dotnet run --project .\DataverseSyncWorker -- --run-once
dotnet run --project .\DataverseSyncWorker
dotnet run --project .\DataverseSyncWorker -- --verify
```

## Production deployment identity

For unattended production runtime, an Entra administrator must create an app registration and client secret or
certificate. Register its application user in the target Dataverse environment.
The one-time provisioning identity needs permission to create publisher, solution,
tables, columns, alternate keys, and security roles. This differs from runtime
permissions. An administrator may run provisioning with a separate deployment
identity. Do not give the steady-state worker an administrator role.

Run from `D:\metasys-poc`:

```powershell
# Secret is entered at a hidden prompt, never stored in the repository.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-DataverseSync.ps1 -ClientId <deployment-client-id> -Mode Provision
```

The command verifies the organization ID before writes and creates/reuses:

- Publisher `FMCentralBmsPublisher`, prefix `fmc`.
- Unmanaged solution `FMCentralBms`.
- Standard table `fmc_bmspoint`, with `fmc_bmspoint_objectid` alternate key.
- Standard table `fmc_bmsbuilding`, with `fmc_bmsbuilding_buildingcode` alternate key.
- Standard table `fmc_bmsequipment`, with `fmc_bmsequipment_equipmentcode` alternate key.
- Relationships `fmc_bmsbuilding_bmsequipment` and `fmc_bmsequipment_bmspoint`.
- Elastic table `fmc_bmsreading`, using its built-in GUID + partition key.
- Security role `FM Central BMS Integration`, with organization-level Create,
  Read and Write on BMS tables and requests, plus Append/Append To needed by the
  Building/Equipment/Point lookups.
- Security role `FM Central BMS Sync Requestor`, with Create/Read on requests.

Assign the runtime application user that role. If provisioning used the same app,
remove its deployment/admin roles before starting the command worker.
Wait until the point alternate-key index is Active before first synchronization.

```powershell
sqlcmd -S localhost -E -C -b -i .\sql\create-dataverse-sync-tables.sql
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-DataverseSync.ps1 -ClientId <runtime-client-id> -Mode Once
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-DataverseSync.ps1 -ClientId <runtime-client-id> -Mode Run
```

For certificate authentication, pass `-CertificateThumbprint <thumbprint>`;
the private certificate must be accessible in the account's `My` certificate store.
When `-ClientId` is supplied, the launcher overrides both developer-token options
with empty command-line values so the application identity is actually selected.
Direct worker launches must set equivalent effective configuration themselves.
The worker also accepts `Dataverse__ClientId`, `Dataverse__ClientSecret` or
`Dataverse__CertificateThumbprint` environment variables directly.

The default route opens Swagger at `http://localhost:5300/swagger`; JSON is at
`http://localhost:5300/swagger/v1/swagger.json`. Status and dead-letter endpoints
are read-only. Development additionally exposes run-once and replay endpoints.
Default binding is loopback; do not expose these endpoints externally without auth.

## Delivery semantics and corrections to Plan 3.0

- Elastic tables cannot have custom alternate keys. Reading GUIDs are derived
  deterministically from `SourceId + SQL id`; partitions are a stable SHA256 of
  `object_id`. `fmc_externalkey` is a diagnostic text field, not a key definition.
- Current state uses a deterministic point GUID. The object-id alternate key adds
  uniqueness. Do not seed the same object ID manually under a different GUID.
- SQL bigint IDs are stored in Dataverse as text, not a 32-bit Whole Number.
- Dataverse decimal uses 4 decimal places and the platform range ±100 billion.
  Values outside that range go to validation dead-letter; raw SQL stays intact.
- `reading_time` for `Fake Metasys COV` is UTC. Legacy reading times are assumed
  UTC by default, configurable through `LegacyReadingTimeZoneId`. Confirm the
  intended timezone of legacy rows before backfill. `ingested_at` is converted
  from the local SQL server timezone (`SE Asia Standard Time`).
- Raw is append-only. Source corrections after successful delivery are outside
  this version's incremental contract and require an explicit resync workflow.
- The SQL delivery ledger, not `MAX(id)`, determines pending rows. This handles
  identity values whose transactions commit out of order. The reported
  `lastSuccessfulId` is informational, not proof every smaller ID was delivered.
- A SQL application lock serializes this pipeline across processes; run-once also
  shares the local semaphore with background sync.
- A batch is acknowledged only after both point and history writes succeed.
  Elastic partial failures leave the batch pending and safe to replay. Validation
  errors are quarantined individually. Permanent remote schema/permission errors
  block sync until corrected/restarted instead of silently discarding readings.
- Each command/run first reads `raw.bms_building` and `raw.bms_equipment`, then
  upserts Building → Equipment before Point/history writes. Point payloads carry
  the deterministic `fmc_equipmentid` lookup from `raw.bms_reading.equipment_code`.
- Latest point means greatest `reading_time`, with SQL `id` breaking ties. Old
  backfill/replay uses the source's current latest value, not the replayed value.
- History TTL is calculated from reading time. Rows older than retention are
  intentionally skipped and acknowledged; replay does not grant another 30 days.
- Delivered history is not automatically repopulated after Dataverse deletion.
  Do not reset the source IDs, change SourceId, or manually delete target data
  without planning reconciliation.

## Verification

```powershell
dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- --self-test
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-DataverseSync.ps1 -ClientId <runtime-client-id> -Mode Verify
```

Self-test creates a uniquely named disposable SQL database, exercises the real
SQL delivery engine against a simulated Dataverse sink, and removes only that
test database. It does not claim to verify live cloud writes. Live `Verify`
checks ledger counts and up to 25 actual retained history rows; full capacity
and volume testing remains a separate operational task.

After successful provisioning, export the deployable solution with existing PAC:

```powershell
pac solution export --name FMCentralBms --path .\dataverse\FMCentralBms.zip
pac solution unpack --zipfile .\dataverse\FMCentralBms.zip --folder .\dataverse\FMCentralBms
```

The live unmanaged export is in `dataverse/FMCentralBms`; the source provisioning
definition remains reviewable in `DataverseProvisioner.cs`.
