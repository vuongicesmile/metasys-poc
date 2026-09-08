---
name: dv-overview
description: Orient Dataverse and Power Platform work in metasys-poc, choose tools and specialist skills, and preserve the SQL-to-Dataverse contract.
license: MIT
---

# Dataverse overview for Metasys POC

Read [AGENTS.md](../../../AGENTS.md) and the
[project reference](references/project.md) for Power Platform operations in this
repository. Follow the user's requested outcome and existing authorization.

## Choose the working surface

| Work | Preferred starting point |
| --- | --- |
| Durable sync, mapping and retry behavior | Existing DataverseSyncWorker C# services |
| Tables, columns, keys and default views | DataverseProvisioner and supported .NET metadata requests |
| Solution export/import/pack/unpack | Existing Windows PAC CLI |
| Small live queries or scoped CRUD | Callable Dataverse MCP, authenticated CLI, or existing SDK |
| A gap in those surfaces | Documented Dataverse Web API with existing authentication |
| Browser-only maker tasks | Available browser capability, following its own skill |

Discover callable tools before assuming MCP is missing. Inspect local tooling
before installing anything. An absent .env, Python SDK or MCP server is not a
broken .NET worker. PAC, Azure CLI, the worker and MCP have distinct authentication
surfaces; success in one does not prove the others work.

Use SDK logical names and metadata-derived Web API entity-set/navigation names.
Do not guess pluralization or lookup casing. Verify evolving capabilities using
[microsoft-docs](../microsoft-docs/SKILL.md).

## Route by the requested action

- Connection/authentication: [dv-connect](../dv-connect/SKILL.md).
- Schema/forms/views: [dv-metadata](../dv-metadata/SKILL.md).
- Writes/import/replay: [dv-data](../dv-data/SKILL.md).
- Reads/reconciliation: [dv-query](../dv-query/SKILL.md).
- ALM/deployment: [dv-solution](../dv-solution/SKILL.md).
- Roles/application users: [dv-security](../dv-security/SKILL.md).
- Settings/retention/cleanup: [dv-admin](../dv-admin/SKILL.md).

## Change lifecycle

1. Resolve the requested scope and verify the target organization with a live
   identity read when doing cloud work. Reuse the known solution/publisher.
2. Prepare reviewable local changes and carry out operations covered by the
   task's authorization. A skill invocation alone grants no additional access.
3. Verify actual state. After metadata changes, export/unpack the affected
   solution, preserving unrelated local edits.
4. Report what changed and what was verified. Distinguish local preparation,
   live deployment, sampled checks and remaining uncertainty.

This local edition uses the repo's .NET/PAC workflow. It does not require the
source plugin, telemetry attribution, scripts/auth.py or ERP tooling.
