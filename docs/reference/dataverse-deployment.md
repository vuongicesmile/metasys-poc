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

## FM Request in-app notification (2026-09-30)

Epic 3 workflow bổ sung sau bản notification: migrations 004/005, signed assembly
`1.0.0.12`, transactional `fmc_TransitionFmRequest` / `fmc_ProcessFmRequestDeadline`,
synchronous lifecycle guards, Approval Routes snapshots, history/email outbox,
scheduled deadline flow và webresource actions trong FMC BMS Demo đã được triển khai.
Xem [Epic 3 runbook và receipt](../runbooks/epic-3-fm-workflow.vi.md) để demo,
đọc giới hạn verification và phân biệt với bell-on-create bên dưới.

`FMC BMS Demo` has In-app notifications enabled. Signed plug-in assembly
`FMCentralBms.Plugins` version `1.0.0.8` and the asynchronous PostOperation
`Create` step `BMS: Notify Operators on FM Request creation` are deployed in
`FMCentralBms`. The step sends a native `SendAppNotification` to each active
user with the `FMC BMS Demo Operator` role, including team-derived members.
The recipient role has the minimum notification receive privileges and is
assigned to `vuong.nguyenq@titancorpvn.com` in this Developer environment.

Live smoke test: FM Request `e773ed00-7dbc-f111-aaaf-00224819a344`
(`NOTIFY-SMOKE-20260930-101414`) produced appnotification
`fcfd7a01-7dbc-f111-aaad-70a8a501ecc3` owned by that user, with a link
to the request. The solution was exported and unpacked after deployment under
`.artifacts/fm-request-notification-final-20260930`. This verifies one
server-side delivery, not the visual state of the user's open app. The app
loads new notifications on launch or navigation after the polling interval;
remaining on an unchanged page does not continuously refresh the bell.

## Operations Center reading query repair (2026-09-30)

The live generated page still queried retired `fmc_bmsreading`, so its
`Promise.all` data load rejected and the whole Operations Center displayed
"Unable to load data". The page now queries the Standard
`fmc_bmsreadingsnapshot` table and is published with that table in its nine
data-source bindings. The current source and generated TSX passed the UI tests;
live Web API reads succeeded for each dashboard table/selected column. A live
page download confirmed the new query and binding, and a post-publish solution
export is retained under `.artifacts/ops-center-fix-live-20260930`. A manual
browser visual check remains separate from these server-side checks.

## Current reading contract (2026-09-24)

The historical elastic `fmc_bmsreading` table was retired. The Standard
`fmc_bmsreadingsnapshot` table retains the 7,863 SQL readings from 2026-09-10
Asia/Bangkok (UTC `[2026-09-09T17:00:00Z, 2026-09-10T17:00:00Z)`) and now
receives new SQL readings after cutover ID `82708`. `SnapshotSyncEnabled=true`,
`SnapshotStartSqlId=82708`, and `HistoryEnabled=false` in the SQL worker. Each
new reading is upserted by deterministic GUID after its current point; both
writes must succeed before the delivery ledger is acknowledged. Prior delivered
SQL rows are not replayed into the Standard table. The model-driven app's
`BMS Readings` menu and default view show `fmc_equipmentcode` and new rows.
SQL `raw.bms_reading` remains the full-history system of record. SPO ingestion
still updates current points only and does not populate this SQL-sourced table.

Live smoke test on 2026-09-24: Fake Metasys COV -> ingestion SQL rows `82709`
and `82710` -> worker -> Standard Dataverse readings. Both had matching SQL ID,
Equipment Code, value, second-resolution time and `current_done=history_done=1`;
the Standard table then held 7,863 base + 2 post-cutover rows. Two other
simulator rows (`82711`-`82712`) were left pending deliberately by the 2-row
test batch.

Migration commands: `--prepare-reading-snapshot`, `--import-reading-snapshot`,
`--verify-reading-snapshot`, `--switch-reading-snapshot-app`,
`--refresh-reading-snapshot-app`,
`--backup-elastic-readings`, and `--retire-elastic-reading`. The last command
requires the verified 7,863-row snapshot, disabled history, updated app
navigation and a readable local backup of the old elastic data. The backup is
under `Dataverse.SyncWorker/Dataverse.SyncWorker.App/.artifacts/reading-snapshot/`
and is not part of the Git solution export. Deleting the table is not the same
as deleting SQL raw rows or delivery receipts.

The remaining sections retain dated deployment receipts and historical elastic
semantics; this current contract takes precedence for operation.

Implementation status: complete in the target Developer environment. The
`FMCentralBms` solution, BMS tables, `fmc_syncrequest`, keys, views, roles,
connection reference, environment variable, and activated cloud flow were
provisioned. On 2026-09-08 request `b4d35f3c-3fab-f111-aaad-00224819a344`
delivered 183 rows in two batches to cutoff 7404 with zero pending/dead-letter;
25 retained history samples were reconciled. This is a dated receipt, not a live
production-readiness claim. The exported
unmanaged solution is checked out at `dataverse/FMCentralBms`.

On 2026-09-12, the existing `FMC BMS Demo` model-driven app was rebuilt and
published as a complete Vietnamese demo shell. The live build created ten
focused views, eight main/Quick Create forms, a responsive `Trung tâm vận hành
BMS` page, Vietnamese navigation, a generated app icon, and the `FMC BMS Demo
Operator` / `FMC BMS Demo Viewer` roles. Builder verification passed 109/109
components, and a separate live download confirmed eight app tables and one
deployed generative page. The app ID remains
`d19f4897-d227-4df3-8361-988f97c53e89`; the operating guide is
[FMC BMS Demo model-driven app](../runbooks/model-driven-app-demo.vi.md).

Later on 2026-09-12, the operations page and sync web resource were published
with an English/Vietnamese selector and English as the default. The selector
persists the preference, translates page copy/status/error messages and formats
dates/numbers. Native demo navigation, ten view names and eight form names/labels
were changed to English in place, preserving component IDs. The page selector
does not change the user's Power Apps platform language. Published readback
verified all 21 affected metadata/web-resource records; ValidateApp returned no
issues. Local browser tests exercised both languages, persistence, blocked
storage, translated errors and no extra data queries; six sync-page tests passed.

On 2026-09-11, unbound action `fmc_RequestBmsSync`, main-operation plug-in
`FMCentralBms.Plugins.RequestBmsSync`, business-event catalogs, active-key
coalescing, flow `FMC - App Request BMS Sync Event`, and the **Power Automate
Demo** page were deployed to the same Developer environment. Test request
`671d5a8a-b2ad-f111-aaad-00224819a344` was returned for both the original call
and an idempotent retry; both corresponding flow runs succeeded. See the
[implementation receipt](../plans/custom-api-request-bms-sync.vi.md) and
[UI test runbook](../runbooks/custom-api-bms-sync-demo.vi.md).

On 2026-09-16, solution-aware user email notification was deployed to the same
Developer environment. A terminal `fmc_syncrequest` now invokes asynchronous
plug-in `QueueSyncNotification`, creates an idempotent `fmc_notification`
outbox row, and flow `FMC - Send User Email Notification` sends it through the
Office 365 Outlook connector before recording a Sent/Failed receipt. The live
flow ID is `23cc5a60-0e28-4afd-8226-042dfeab84d4`; plug-in assembly version is
`1.0.0.5`. Direct-outbox and producer-chain tests both reached Sent with one
attempt. See the [notification plan and receipt](../plans/user-email-notifications.vi.md)
and [operating runbook](../runbooks/user-email-notifications.vi.md). This dated
receipt does not prove future mail delivery or production mailbox readiness.

The Building/Equipment extension was verified on 2026-09-09. Request
`c99f3d86-1fac-f111-aaad-00224819a344` succeeded at SQL cutoff 23377 with zero
quarantined rows. Live verification passed for 3 Buildings, 4 Equipment, all 5
Point → Equipment → Building joins, and 25 retained history samples. Ingestion
continued after that cutoff, so later rows correctly remained pending for the
next request.

Local development uses the signed-in Azure CLI via the portable
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
- Standard table `fmc_bmsreadingsnapshot`, with deterministic SQL reading GUID
  and `fmc_equipmentcode` text(100), for the fixed 2026-09-10 demo day.
- Standard table `fmc_syncrequest`, with correlation alternate key and
  `fmc_syncrequest_activekey` to prevent concurrent active requests per pipeline.
- Standard table `fmc_notification`, with a correlation alternate key and
  delivery receipts for solution-aware user email notifications.
- Security role `FM Central BMS Integration`, with organization-level Create,
  Read and Write on BMS tables, requests and notifications, plus Append/Append
  To needed by the Building/Equipment/Point lookups.
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

- The retired elastic history used deterministic reading GUIDs and SHA256
  partitions; `fmc_externalkey` was diagnostic text, not a custom alternate
  key. The Standard snapshot preserves the deterministic reading GUID and SQL ID
  but has no partition or TTL.
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
- In the current `SnapshotSyncEnabled=true` mode, post-cutover rows are
  acknowledged only after both point and Standard reading writes succeed.
  Earlier rows remain point-only; the old elastic history path stays disabled.
  Validation
  errors are quarantined individually. Permanent remote schema/permission errors
  block sync until corrected/restarted instead of silently discarding readings.
- Each command/run first reads `raw.bms_building` and `raw.bms_equipment`, then
  upserts Building → Equipment before Point writes. Point payloads carry
  the deterministic `fmc_equipmentid` lookup from `raw.bms_reading.equipment_code`.
- Latest point means greatest `reading_time`, with SQL `id` breaking ties. Old
  backfill/replay uses the source's current latest value, not the replayed value.
- The retired elastic history calculated TTL from reading time. Standard
  readings have no TTL and post-cutover rows are refreshed idempotently by the
  command worker.
- The 10/09 base was loaded from SQL without replaying the delivery ledger or
  changing raw identities. No older elastic history is automatically repopulated.
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
test database. It does not claim to verify live cloud writes. Live `--verify`
checks point/catalog, post-cutover reading samples and ledger state; use
`--verify-reading-snapshot` for the 7,863-row base plus new rows. Full capacity
and volume testing remains a separate operational task.

After successful provisioning, export the deployable solution with existing PAC:

```powershell
pac solution export --name FMCentralBms --path .\dataverse\FMCentralBms.zip
pac solution unpack --zipfile .\dataverse\FMCentralBms.zip --folder .\dataverse\FMCentralBms
```

The live unmanaged export is in `dataverse/FMCentralBms`; the source provisioning
definition remains reviewable in `DataverseProvisioner.cs`.
