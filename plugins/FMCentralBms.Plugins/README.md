# FMCentralBms.Plugins

Dataverse plug-in source for the Equipment → Building validation rule.

The local signing key `FMCentralBms.Plugins.snk` is intentionally excluded from
Git. Before building on another machine, obtain the existing key from the project
owner through a secure channel and place it next to the `.csproj`. Do not generate
a replacement key when updating the deployed assembly: that changes its public
key token and assembly identity.

```powershell
dotnet build .\plugins\FMCentralBms.Plugins -c Release
```

The exported DLL under `dataverse/FMCentralBms/PluginAssemblies` is the deployment
artifact and contains no private signing key.

See the [step-by-step guide](../../docs/runbooks/dataverse-plugin-step-by-step.vi.md)
and [UI testing runbook](../../docs/runbooks/bms-demo-app.vi.md).
