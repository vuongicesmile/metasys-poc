---
name: dv-query
description: Read, filter, count and analyze Dataverse BMS records or reconcile them with SQL and delivery receipts without starting synchronization.
license: MIT
---

# Read and reconcile Dataverse data

Read [dv-overview](../dv-overview/SKILL.md) and its project reference.

## Read from the requested source

If the question asks about live Dataverse, query that organization. Local
solution XML establishes schema, not live data. SQL is the BMS system of record;
use it for full history or explicitly requested reconciliation and label which
source each result comes from.

Use an already callable MCP/CLI for small reads or the existing .NET SDK for
scripted queries. Select needed fields, bound result size and follow paging
continuations when full counts/exports are required. Discover entity-set and
navigation names from metadata. Dataverse query features depend on table type
and endpoint; do not treat them as unrestricted SQL Server T-SQL.

In PowerShell, quote OData paths so literal $select/$filter and & query separators
reach the tool unchanged. Prefer structured tool arguments or SDK queries.

## Existing read-only checks

For an already running service, GET /api/dataverse-sync/status and
GET /api/dataverse-sync/dead-letters inspect SQL/runtime state.

For retained cloud samples:

```powershell
dotnet run --project .\DataverseSyncWorker -- --verify
```

This reads SQL and Dataverse and checks up to 25 retained history rows.
Do not start the default continuous worker or run --run-once/--provision to
answer a status question. See the project reference for disabling background
sync if the API host must be started.

## Reconciliation reasoning

- Use per-row delivery receipts and unresolved dead letters to establish pending
  state; lastSuccessfulId does not prove all smaller IDs were delivered.
- Compare point state to the latest event time and SQL-id tie-breaker.
- Match history by the mapper's GUID plus partition, SQL ID, object and value.
- Scope counts to a stated cutoff/retention window. TTL expiry and concurrent
  ingestion explain why raw SQL and retained history counts may differ.
- Account for HistoryEnabled and already acknowledged history before proposing
  a resync. A mismatch is a finding, not authorization to change the ledger.
- Label capped/approximate counts, incomplete paging and sampled comparisons.

Return the result with its source, observation time, filter/window and practical
limit. Do not claim zero data loss or complete reconciliation from sampled rows.
