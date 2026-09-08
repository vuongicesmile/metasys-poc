---
name: dv-solution
description: Create, export, unpack, package, import and verify the FMCentralBms Dataverse solution and its deployment artifacts.
license: MIT
---

# Dataverse solution lifecycle

Read [dv-overview](../dv-overview/SKILL.md). The current development source is
[dataverse/FMCentralBms](../../../dataverse/FMCentralBms); the solution unique name
is FMCentralBms and publisher is FMCentralBmsPublisher with prefix fmc.

## Inspect before packaging

Check git status for local edits, solution membership, publisher and intended
environment. Verify live identity for online operations. Inspect installed PAC
command usage before adding unfamiliar flags.

The existing provisioner creates/reuses publisher and solution using the .NET
SDK. Do not invent pac solution create or duplicate the solution because it is
not visible through the wrong profile.

## Pull an authorized metadata change

For a clean export destination, the project's baseline commands are:

```powershell
pac solution export --name FMCentralBms --path .\dataverse\FMCentralBms.zip --managed false --environment https://org06cbc9ec.crm5.dynamics.com/
```

After export finishes successfully, unpack separately:

```powershell
pac solution unpack --zipfile .\dataverse\FMCentralBms.zip --folder .\dataverse\FMCentralBms --packagetype Unmanaged
```

If the ZIP exists or the destination contains local edits, use a new temporary
export/unpack location and compare/merge the relevant files. Do not overwrite
unreviewed changes. Keep a recoverable export until the source is verified.
Check the manifest, entities, forms/views and role diff against the actual task.

## Package and deploy

Local pack/unpack is distinct from an online import. An export/deployment request
does not imply source-data migration, new identities or environment creation.

For deployment, prepare the exact package, dependencies, destination, environment
variables/connection references and intended managed/unmanaged mode first.
Use the deployment mode appropriate to that environment; this repo's source is
an unmanaged development export. Do not infer an import/upgrade flag from a
generic example. Never use force/upgrade to dismiss an unexplained import error.

Run authorized imports once and follow the actual job to terminal completion.
A solution appearing in a list does not prove the import or publish completed.
Inspect errors, version, component membership, key readiness and affected UI;
run relevant application/data checks.

Leave a reviewable diff and report verification. Commit/push only when requested
or already authorized by the surrounding workflow.
