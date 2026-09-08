---
name: power-apps-code-app-scaffold
description: Scaffold or extend a Power Apps Code App when that app type is selected for metasys-poc; does not apply to ordinary canvas or model-driven app changes.
---

# Power Apps Code App scaffolding

Use this when the user selects a Code App or the agreed design requires one.
Inspect an existing app before scaffolding into a new subdirectory. Keep the
.NET integration boundary and SQL source ownership from
[AGENTS.md](../../../AGENTS.md).

## Verify the current development path

Read [Code Apps overview](https://learn.microsoft.com/en-us/power-apps/developer/code-apps/overview),
[current Microsoft samples](https://github.com/microsoft/PowerAppsCodeApps) and
[Code Apps ALM](https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm).
Inspect the installed CLI and selected template.

As checked on 2026-09-08, the ALM guide documents Power Apps CLI pa app push
with solution targeting. The RMIT source skill's pac code workflow, SDK ^0.3.1,
preview label, fixed port and lack-of-solutions claim are historical examples.
Verify compatibility rather than copying those assumptions into a new app.
Existing apps need not be migrated just to perform an unrelated feature edit.

## Build the requested app

1. Reuse the existing framework; for a new app, a current official React,
   TypeScript and Vite starter is a reasonable default.
2. Use the template's supported initialization, dev proxy, generated data models
   and connector services. Pin/lock the chosen dependency versions.
3. Select only the data sources the feature needs. A BMS viewer normally reads
   the Dataverse point/history tables. Do not copy worker secrets or connect a
   browser directly to the local SQL Server.
4. Build loading, empty, permission and network-error states plus the relevant
   responsive/keyboard behavior. Request only required fields and page history.
5. Document local startup and the selected deployment/connection configuration.
   Verify environment enablement, licensing and policies when deployment
   depends on them; do not change tenant settings as a scaffolding side effect.

Run the app's build/type/lint checks as applicable and inspect the rendered UI
with an available browser following its skill. Verify live connector behavior
separately from mocked UI behavior.

For authorized publishing, resolve the actual FMCentralBms solution ID and
target explicitly using supported tooling. Package preparation is distinct from
upload/publish. Do not add unrelated profile connectors, offline storage,
notifications or a new hosting service unless requested.
