# FMCentralBms.Plugins

Dataverse plug-in source for FMCentralBms (single signed net48 assembly).

## Layout

```text
Infrastructure/   PluginServices, BusinessEventPluginBase
Shared/           Entity names, choice constants, ClientRequestId, EmailAddress
Services/         SyncRequestStore, NotificationOutbox, NotificationMessageBuilder
Plugins/          IPlugin entry points registered in Dataverse
```

Public type names stay under `FMCentralBms.Plugins.*` so existing step
registrations keep working after rebuild.

## Signing

The original signing key `FMCentralBms.Plugins.snk` is checked into this public
POC by explicit project-owner choice so another machine can build the same
assembly identity after a pull. It is **not** a Dataverse credential; anyone
with this public repo can sign a DLL with that identity, but deployment still
requires Dataverse permissions. Do not generate a replacement key when updating
the deployed assembly: that changes its public key token and assembly identity.

```powershell
.\scripts\Publish-FmcPlugin.ps1          # build + read-only Dataverse preflight
.\scripts\Publish-FmcPlugin.ps1 -Publish # explicit registration/update
```

The exported DLL under `dataverse/FMCentralBms/PluginAssemblies` is the deployment
artifact and contains no private signing key.

See the [step-by-step guide](../../docs/runbooks/dataverse-plugin-step-by-step.vi.md)
and [home-machine setup](../../docs/runbooks/home-dataverse-plugin-sync.vi.md)
and [UI testing runbook](../../docs/runbooks/bms-demo-app.vi.md).
