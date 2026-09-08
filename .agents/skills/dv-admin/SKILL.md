---
name: dv-admin
description: Inspect or change scoped Dataverse environment settings, auditing, retention and cleanup for the Metasys POC.
license: MIT
---

# Dataverse environment administration

Read [dv-overview](../dv-overview/SKILL.md).
Use [dv-security](../dv-security/SKILL.md) for role changes and
[dv-data](../dv-data/SKILL.md) for individual record mutations.

## Read before changing settings

Identify the requested setting, target organization, current value and effect.
Prefer the installed PAC command when it supports the operation. For settings
stored elsewhere, verify the documented API/metadata and current version with
[microsoft-docs](../microsoft-docs/SKILL.md).

Organization columns, OrgDB XML, recycle-bin configuration and feature-setting
overrides are different mechanisms. Do not invent keys or patch a whole settings
blob without preserving unrelated values. Inspect only the settings needed for
the task; a diagnostic request does not authorize toggling features.

Apply authorized changes to the exact target, then read back actual values and
check the affected behavior. Follow asynchronous jobs to completion before a
dependent change.

## Retention and cleanup

BMS history currently uses an event-age elastic TTL. Raw SQL preserves full
history. Audit retention, long-term archival, recycle-bin retention and elastic
row TTL are separate behaviors; do not substitute one for another.

Before changing retention, establish its effect on existing rows, future writes,
capacity and recovery. Keep the mapper/configuration and deployment notes
consistent when the worker's history policy changes.

For requested cleanup, prepare a precise table/filter/cutoff, inspect matching
records/counts and establish recovery and reconciliation effects. An empty or
omitted filter can target an entire table. Do not execute broad deletion from
an ambiguous cleanup request.

Deleting delivered Dataverse history does not automatically make the SQL ledger
resend it. Do not reset receipts, SourceId, raw SQL identities or source rows as
a cleanup shortcut. For irreversible actions, ensure the user's authorization
covers the exact scope before execution.

Report the setting or job changed, target, completion state, verified result and
any data removed, including whether it can be recovered.
