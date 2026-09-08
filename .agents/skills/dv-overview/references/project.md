# Metasys Power Platform project reference

Paths below are relative to the repository root unless linked otherwise.
This is the checked-in baseline as adapted on 2026-09-08; verify live state
when a task depends on it.

## Target and source of truth

| Item | Checked-in value |
| --- | --- |
| Dataverse URL | https://org06cbc9ec.crm5.dynamics.com/ |
| Expected organization ID | ab191700-b99e-f111-aaa0-000d3a80bb96 |
| Environment ID | 5abcb0e5-99b2-e51f-aa0e-90d84405798b |
| Tenant in developer token helper | 31983a93-6f80-4356-a4e1-a65055e8327e |
| Solution / publisher / prefix | FMCentralBms / FMCentralBmsPublisher / fmc |
| Runtime role | FM Central BMS Integration |
| SQL source | localhost / FM_Central / raw.bms_reading |
| SourceId | FMC |
| Point / history | fmc_bmspoint standard / fmc_bmsreading elastic |
| Request / flow | fmc_syncrequest / FMC - Request SQL to Dataverse Sync |
| Worker mode | CommandDriven (Dataverse request queue) |

Read [deployment notes](../../../../docs/reference/dataverse-deployment.md),
[options](../../../../DataverseSyncWorker/Models/SyncOptions.cs) and
[configuration](../../../../DataverseSyncWorker/appsettings.json) for changes
to this baseline. Public identifiers above do not supply credentials.

## Implementation map

| Concern | File |
| --- | --- |
| Auth and WhoAmI organization guard | [DataverseConnection.cs](../../../../DataverseSyncWorker/Services/DataverseConnection.cs) |
| Schema, solution, role and view provisioning | [DataverseProvisioner.cs](../../../../DataverseSyncWorker/Services/DataverseProvisioner.cs) |
| Deterministic identity, UTC, values and TTL | [ReadingMapper.cs](../../../../DataverseSyncWorker/Services/ReadingMapper.cs) |
| Source reads, delivery ledger and locks | [SqlStore.cs](../../../../DataverseSyncWorker/Services/SqlStore.cs) |
| Batch orchestration and acknowledgement | [SyncEngine.cs](../../../../DataverseSyncWorker/Services/SyncEngine.cs) |
| Remote writes and retry handling | [DataverseWriter.cs](../../../../DataverseSyncWorker/Services/DataverseWriter.cs) |
| Self-test and live sample checks | [Verification.cs](../../../../DataverseSyncWorker/Services/Verification.cs) |
| Command modes and endpoints | [Program.cs](../../../../DataverseSyncWorker/Program.cs) |
| Request claim, lease and progress | [SyncRequestStore.cs](../../../../DataverseSyncWorker/Services/SyncRequestStore.cs) |
| Command-driven orchestration | [CommandProcessor.cs](../../../../DataverseSyncWorker/Services/CommandProcessor.cs) |
| Flow/connection/environment-variable provisioning | [PowerAutomateProvisioner.cs](../../../../DataverseSyncWorker/Services/PowerAutomateProvisioner.cs) |
| SQL integration schema | [create-dataverse-sync-tables.sql](../../../../sql/create-dataverse-sync-tables.sql) |

History GUID is derived from SourceId + SQL id; point GUID from SourceId +
object_id. partitionid is the SHA256 hex of object_id. Preserve the exact
implementation. Current history TTL defaults to 2,592,000 seconds, calculated
from reading time; a change does not automatically rewrite existing records.

## Windows and shared developer authentication

The source workspace is /home/tt/data-platform-project/rmit-fm-data in WSL
Ubuntu-22.04. Windows accesses it at
`\\wsl.localhost\Ubuntu-22.04\home\tt\data-platform-project\rmit-fm-data`.

The configured DeveloperTokenPython is the Windows Python bundled under that
workspace's .tools/azure-cli/Scripts/python.exe. The local helper
[scripts/get-dataverse-token.py](../../../../scripts/get-dataverse-token.py)
invokes Azure CLI to obtain a token. Do not display its normal stdout.

A token-only probe, when diagnosing authentication:

```powershell
$syncConfig = Get-Content -LiteralPath .\DataverseSyncWorker\appsettings.json -Raw | ConvertFrom-Json
& $syncConfig.Dataverse.DeveloperTokenPython $syncConfig.Dataverse.DeveloperTokenScript --probe
```

The probe outputs only a status/length marker. It does not prove Dataverse
reachability, organization identity, table privileges or MCP connectivity.

Use PAC identity checks for PAC operations:

```powershell
pac auth list
pac org who --environment https://org06cbc9ec.crm5.dynamics.com/
```

For production identity work inspect
[Start-DataverseSync.ps1](../../../../scripts/Start-DataverseSync.ps1) and
[New-DataverseIdentity.ps1](../../../../scripts/New-DataverseIdentity.ps1)
before running them; they may authenticate, provision identities or write data.
UsesDeveloperToken takes precedence over ClientId/ClientSecret in the worker.
Start-DataverseSync.ps1 disables both developer-token options with command-line
overrides when an explicit ClientId is supplied. Direct worker launches must
set equivalent effective configuration; verify the actual caller.

For repeatable setup and a one-click Run trigger, see the
[operating runbook](../../../../docs/runbooks/sql-to-dataverse-runbook.vi.md).
For the cloud trigger and request lifecycle, use the
[Power Automate runbook](../../../../docs/runbooks/power-automate-sql-sync.vi.md).

## Commands and side effects

Run from the repository root. Do not start a write mode merely to inspect status.

| Command or mode | Effect |
| --- | --- |
| dotnet build .\MetasysPoc.sln | Local build |
| dotnet run --project .\DataverseSyncWorker -- --verify | Live SQL/Dataverse reads; up to 25 retained history samples |
| dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- --self-test | Creates/removes isolated SQL test database; simulated cloud sink |
| dotnet run --project .\DataverseSyncWorker -- --provision | Cloud metadata, views, solution and role writes |
| dotnet run --project .\DataverseSyncWorker -- --run-once | One synchronization batch and SQL delivery-state writes |
| dotnet run --project .\DataverseSyncWorker | Starts API and waits for queued requests in the checked-in CommandDriven mode |
| dotnet run --project .\DataverseSyncWorker -- --enqueue | Creates/coalesces a live sync request; cloud/SQL mutation follows when processed |
| dotnet run --project .\DataverseSyncWorker -- --process-command-once | Claims and processes one request through potentially multiple SQL batches |
| Start-DataverseSync.ps1 -Mode Verify | Read-only reconciliation, after its authentication setup |
| Development run-once/replay POST endpoints | Mutations, not status checks |

GET /api/dataverse-sync/status and GET /api/dataverse-sync/dead-letters are
read-only on an already running service. If starting the host for diagnosis,
set Dataverse__Enabled=false for that process and restore prior shell
configuration afterwards; inspect Program.cs for other startup side effects.
The --verify path returns before app.RunAsync and does not start background sync.

For source-only API/SSE checks, the documented BmsIngestionApp --no-sql mode
avoids SQL persistence but creates a simulator subscription.

## Verification limits

The delivery ledger is the authority for pending rows. lastSuccessfulId is
informational; history count also depends on TTL, HistoryEnabled and ingestion
in progress. --verify reports samples and ledger counts, not an exhaustive
point comparison, zero data loss, or a throughput/capacity certification.
