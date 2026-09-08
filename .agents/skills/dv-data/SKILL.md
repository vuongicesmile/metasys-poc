---
name: dv-data
description: Write, import, upsert or replay Dataverse records and SQL-to-Dataverse BMS batches while preserving deterministic identity and delivery receipts.
license: MIT
---

# Dataverse data writes and replay

Read [dv-overview](../dv-overview/SKILL.md), then inspect ReadingMapper,
SyncEngine, SqlStore and DataverseWriter for BMS data work.
Use [dv-query](../dv-query/SKILL.md) for a read-only request.

## Choose the write path

The existing .NET worker is the integration path for SQL BMS readings.
Use its batch/replay behavior instead of a second direct uploader that bypasses
delivery receipts. Callable MCP/CLI/SDK operations can fit separately requested
record edits or seed data; inspect the schema and their actual capabilities first.

Before a write, establish the target, table, exact row selection/count, stable
identity and desired values from the task. Inspect existing records to avoid
duplicates. Carry out authorized work without re-asking settled choices.

## Preserve the BMS contract

- Derive reading and point GUIDs and partitions with ReadingMapper.
- Do not manually seed a point under a different GUID for the same object_id.
- Preserve bigint IDs as text and validate numeric range, decimal precision and UTC.
- Preserve event-age TTL. An expired reading may be acknowledged without a
  history write; distinguish this from a dropped/error row.
- Pending selection uses the ledger, including rows committed below an older
  high watermark. Current state must not regress during backfill.
- Respect the configured batch limit (currently 1..100) and single pipeline writer.
- Acknowledge only after point and enabled history writes complete.
  Retry partial failures idempotently; quarantine validation failures explicitly.
- Respect service throttling and the writer's retry handling. Persistent
  permission/schema failures need diagnosis, not unbounded retries.

## Import, correction and deletion

For separately requested imports, profile the source, resolve required fields and
lookups, establish stable keys and load dependencies in order. Record rejected
rows and reconcile counts/values; do not silently skip unresolved references.

Changes to already delivered raw rows are outside the current incremental
contract. Prepare a scoped correction/resync before promising propagation.
Releasing a dead-letter and deleting delivered Dataverse data are different:
deletion is not automatically healed by the ledger.

Before deletion, identify exact records and recovery/reconciliation effects.
Perform only the approved deletion scope. Reuse the SDK's documented table-type
semantics rather than assuming standard and elastic bulk operations are atomic.

Verify saved IDs, values and receipts after writes. Report delivered, pending,
quarantined and retention-skipped results when observable, with sample limits.
