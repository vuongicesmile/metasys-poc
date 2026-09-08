# Metasys BMS POC - Agent Instructions

## Purpose and sources

Work on the Metasys COV ingestion and SQL-to-Dataverse POC in this repository.
Use the Power Platform working patterns adapted from rmit-fm-data, with the
architecture and implementation of this repository as the project context.

Read [README.md](README.md) for the service map and
[Dataverse deployment](docs/reference/dataverse-deployment.md) for the implemented Dataverse contract.
The implementation amendments there take precedence over the original
[Plan 3.0](docs/plans/plan-3.0-sql-to-dataverse.md). Check the relevant code before changing it.
A historical deployment receipt is evidence of that run, not current live status.
For repeatable SQL-to-Dataverse operation, use the
[operating runbook](docs/runbooks/sql-to-dataverse-runbook.vi.md) and the Run launcher.
Use [docs/README.md](docs/README.md) as the documentation index. Keep plans in
docs/plans, operating procedures in docs/runbooks and implementation references
in docs/reference. Keep README.md and AGENTS.md at root for discovery, and skill
instructions under .agents/skills. Proposed plans are not implementation evidence.

Reply in Vietnamese when the user writes in Vietnamese. Keep product names,
commands, logical names and code identifiers in English. Report the outcome,
relevant changes, verification and any remaining limitation.

## Project boundaries

- FakeMetasysApi is the simulator on port 5100.
- BmsIngestionApp consumes COV over SSE on port 5200 and appends to SQL.
- SQL Server database FM_Central, table raw.bms_reading, is the system of record
  and holds full history.
- DataverseSyncWorker on port 5300 sends current state and retained history to
  Dataverse independently of ingestion.
- Power Automate `FMC - Request SQL to Dataverse Sync` queues `fmc_syncrequest`;
  CommandDriven worker instances atomically claim, lease and complete requests.
- The existing solution is FMCentralBms, publisher FMCentralBmsPublisher, prefix fmc.
- fmc_bmspoint is a standard table; fmc_bmsreading is an elastic table.
- Use the checked-in Developer environment identity and verify the actual target
  before cloud operations. Details are in the shared
  [project reference](.agents/skills/dv-overview/references/project.md).
- Production interfaces, hosting, licenses, capacity and service identities need
  evidence for the requested deployment. The simulator does not prove access to
  a real Johnson Controls Metasys installation.

The wider RMIT requirements, Sales Demo components, Bronze/Silver/Gold Dataverse
architecture, FR/NFR IDs, approval flows and reporting scope are not inherited
merely because the platform or development login is shared. Add such work when
the user requests it. This POC uses the .NET worker for COV synchronization;
a Power Automate flow per reading is not its current integration design.

## Implementation invariants

- Preserve raw SQL rows, source IDs, SourceId and deterministic GUID/partition
  mapping. Changing identity requires a reconciliation/migration design.
- Pending work comes from the per-row delivery ledger, not only MAX(id).
  Transactions can commit below the reported lastSuccessfulId.
- Acknowledge a valid row after point writes and enabled history writes succeed.
  Preserve safe replay after partial remote success.
- Current point state comes from the greatest reading_time, with SQL id as a
  tie-breaker. Backfill must not replace current state with an older event.
- History retention is calculated from event time. Replaying old data must not
  grant it another full TTL. Expired history can be intentionally acknowledged.
- Preserve text storage for SQL bigint IDs, four decimal places, and the
  configured timestamp conversions. ReadingMapper defines the actual mapping.
- The standard point table has the fmc_bmspoint_objectid alternate key.
  Elastic history uses its deterministic primary GUID plus partitionid;
  fmc_externalkey is diagnostic text, not a custom alternate key.
- Preserve the SQL application lock and local serialization. Schema/permission
  failures must remain visible; do not silently discard readings to clear lag.

## Power Platform work

Use the existing .NET SDK services for durable integration and provisioning,
PAC for solution packaging, and a callable MCP/CLI/API for suitable interactive
operations. Inspect installed command help and Microsoft documentation when
capabilities are uncertain. Do not invent a tool, SDK method or plugin script.

For schema changes, update DataverseProvisioner and dependent mapping/queries as
needed. Apply authorized changes through supported metadata APIs or the maker
portal, verify live metadata, and export/unpack into dataverse/FMCentralBms.
Locally prepared definitions are not proof of cloud deployment.

Keep application artifacts solution-aware. When adding flows/apps, use the
requested app type, environment variables, connection references and Dataverse
permissions. Check current feature support for the selected tooling.

Reuse the developer token helper and existing authentication where appropriate.
Never copy the source project's .env, token caches, private keys or client
secrets. Production runtime uses an application user with the required table
permissions; development/provisioning credentials are a separate concern.

## Scope and authorization

Continue work already authorized in the conversation. Known project values are
defaults to verify, not questions to ask again. Read-only checks, local changes
and applicable verification can proceed within the task.

A request to inspect, explain or diagnose does not authorize running sync,
provisioning, granting roles or changing cloud settings. The default worker
startup can write to Dataverse; use the read-only paths in the project reference.

Before an authorized mutation, identify the exact environment and affected
solution, records or principal. If the target conflicts with the task, the
operation is destructive beyond the approved scope, or a required decision is
missing, finish independent preparation and ask for that specific decision.
Role elevation, environment creation, production deployment and external
notifications are not implicit consequences of installing or using a skill.
Commit, push and communications to others require task authorization.

## Verification

Choose checks appropriate to the changed behavior:

- Documentation/skills: validate frontmatter, routing, referenced paths and diff.
- C# changes: build MetasysPoc.sln; run relevant verification from docs/reference/dataverse-deployment.md.
- --self-test creates and removes a uniquely named SQL test database and uses a
  simulated Dataverse sink. It requires local SQL access.
- --verify reads the live organization and SQL ledger, checking up to 25 retained
  history samples. It does not establish full reconciliation or capacity.
- Schema/deployment changes: inspect actual metadata, solution membership, key
  readiness and affected forms/views; retain the exported source diff.

## Local skill routing

These are project-local skills in .agents/skills. Read the smallest relevant
set of SKILL.md files before applying them. Start with dv-overview for Dataverse
operations, then select the specialist. If a running session has not indexed a
new skill yet, these links provide its complete instructions.

| Skill | Use for |
| --- | --- |
| [dv-overview](.agents/skills/dv-overview/SKILL.md) | Project context, tool choice and operation boundaries |
| [dv-connect](.agents/skills/dv-connect/SKILL.md) | Existing authentication, environment identity and MCP connection diagnosis |
| [dv-metadata](.agents/skills/dv-metadata/SKILL.md) | Tables, columns, keys, relationships, forms and views |
| [dv-data](.agents/skills/dv-data/SKILL.md) | Record writes, imports, backfill and replay |
| [dv-query](.agents/skills/dv-query/SKILL.md) | Read-only queries, counts and reconciliation |
| [dv-solution](.agents/skills/dv-solution/SKILL.md) | Solution export, unpack, packaging and deployment |
| [dv-security](.agents/skills/dv-security/SKILL.md) | Roles, application users and identity permissions |
| [dv-admin](.agents/skills/dv-admin/SKILL.md) | Environment settings, retention, audit and scoped cleanup |
| [microsoft-docs](.agents/skills/microsoft-docs/SKILL.md) | Current Microsoft documentation and capability verification |
| [solution-architect](.agents/skills/solution-architect/SKILL.md) | Requested overview, architecture and implementation plans |
| [mermaid-diagrams](.agents/skills/mermaid-diagrams/SKILL.md) | Architecture, sequence, state and data-model diagrams |
| [power-apps-code-app-scaffold](.agents/skills/power-apps-code-app-scaffold/SKILL.md) | An explicitly selected Power Apps Code App |
| [power-platform-mcp-connector-suite](.agents/skills/power-platform-mcp-connector-suite/SKILL.md) | A selected custom connector/MCP integration with Copilot Studio |

## Adaptation provenance

Adapted on 2026-09-08 from ~/data-platform-project/rmit-fm-data/AGENTS.md in
WSL Ubuntu-22.04 (home /home/tt):

- Eight dv-* skills: .tools/dataverse-skills/.github/plugins/dataverse/skills,
  source revision 001b31e0c78e63a0675078ad599e44c16078cd7f.
  The upstream [MIT notice](.agents/skills/LICENSE.dataverse.txt) is retained.
- Five supporting skills: /home/tt/.codex/skills, selected by the source project's
  skill routing.
- The instructions are adapted local editions, not an installation of the source
  plugin. Plugin-only auth.py/templates, ERP/X++ routes, unconditional Python
  requirements and automatic commit/push steps are replaced with this repo's
  working paths and task boundaries.
- Source Code Apps/MCP capability claims were checked against Microsoft Learn;
  the relevant local skills link to those sources and require verification when
  used with a different tool version.
