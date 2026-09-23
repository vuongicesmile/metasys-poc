<#
.SYNOPSIS
Builds and checks the signed FMCentralBms plug-in; deploys only with -Publish.
.DESCRIPTION
Requires the original private signing key. A new key changes the assembly identity.
The Dataverse worker verifies WhoAmI against ExpectedOrganizationId before any write.
.EXAMPLE
.\scripts\Publish-FmcPlugin.ps1
.EXAMPLE
.\scripts\Publish-FmcPlugin.ps1 -Publish
#>
[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$SigningKeyPath = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$configPath = Join-Path $repo 'Dataverse.SyncWorker\Dataverse.SyncWorker.App\appsettings.Local.json'
$pluginProject = Join-Path $repo 'Dataverse.Plugin\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj'
$workerProject = Join-Path $repo 'Dataverse.SyncWorker\Dataverse.SyncWorker.App\Dataverse.SyncWorker.App.csproj'
$dll = Join-Path $repo 'Dataverse.Plugin\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll'
$repoSigningKey = Join-Path $repo 'Dataverse.Plugin\FMCentralBms.Plugins\FMCentralBms.Plugins.snk'
$expectedToken = 'e122b5e4dcc2589d'
$expectedOrganization = 'ab191700-b99e-f111-aaa0-000d3a80bb96'
$expectedUrl = 'https://org06cbc9ec.crm5.dynamics.com/'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET SDK before building the plug-in.' }
$settings = if (Test-Path -LiteralPath $configPath) {
    Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
} else { $null }
if (-not $SigningKeyPath -and $settings) { $SigningKeyPath = $settings.LocalPlugin.SigningKeyPath }
if (-not $SigningKeyPath) { $SigningKeyPath = $env:FMC_PLUGIN_SIGNING_KEY }
if (-not $SigningKeyPath) { $SigningKeyPath = $repoSigningKey }
$SigningKeyPath = [IO.Path]::GetFullPath($SigningKeyPath)
if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) { throw "Signing key not found: $SigningKeyPath" }
if ($settings -and $settings.Dataverse.Url -and $settings.Dataverse.Url.TrimEnd('/') -ne $expectedUrl.TrimEnd('/')) {
    throw 'Local configuration tries to change the approved Dataverse URL.'
}
if ($settings -and $settings.Dataverse.ExpectedOrganizationId -and $settings.Dataverse.ExpectedOrganizationId -ne $expectedOrganization) {
    throw 'Local configuration tries to change the approved organization.'
}

& dotnet build $pluginProject -c Release "-p:AssemblyOriginatorKeyFile=$SigningKeyPath"
if ($LASTEXITCODE -ne 0) { throw 'Plug-in build failed; nothing was uploaded.' }
$identity = [Reflection.AssemblyName]::GetAssemblyName($dll)
$token = [BitConverter]::ToString($identity.GetPublicKeyToken()).Replace('-', '').ToLowerInvariant()
if ($identity.Name -ne 'FMCentralBms.Plugins' -or $token -ne $expectedToken) {
    throw "Plug-in identity mismatch: $($identity.FullName). Use the ORIGINAL .snk; nothing was uploaded."
}
Write-Host "Local assembly: $($identity.FullName)"

function Get-LivePluginStatus {
    $result = & dotnet run --project $workerProject --no-launch-profile --configuration Release -- --plugin-status 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not read the target Dataverse plug-in status: $($result -join [Environment]::NewLine)" }
    $json = $result | Where-Object { $_ -is [string] -and $_.StartsWith('{') -and $_.Contains('"Assembly"') } | Select-Object -Last 1
    if (-not $json) { throw "Worker did not return plug-in status: $($result -join [Environment]::NewLine)" }
    return ($json | ConvertFrom-Json)
}

$live = Get-LivePluginStatus
if ($live.Environment.TrimEnd('/') -ne $expectedUrl.TrimEnd('/') -or $live.Organization -ne $expectedOrganization -or
    $live.Solution -ne 'FMCentralBms' -or $live.Publisher -ne 'FMCentralBmsPublisher') {
    throw 'Dataverse organization or solution identity mismatch. Nothing was uploaded.'
}
if (-not $live.Assembly) { throw 'The existing FMCentralBms.Plugins assembly was not found. Refusing an implicit first registration.' }
if ($live.Assembly.PublicKeyToken.ToLowerInvariant() -ne $expectedToken) {
    throw 'The live assembly has a different public key token. Nothing was uploaded.'
}
$liveVersion = [Version]$live.Assembly.Version
$localVersion = $identity.Version
Write-Host "Live assembly:  version $liveVersion, public key token $($live.Assembly.PublicKeyToken)"
if ($localVersion.Major -ne $liveVersion.Major -or $localVersion.Minor -ne $liveVersion.Minor) {
    throw 'Keep the existing major/minor assembly version for an in-place update; only increment build/revision.'
}
if ($Publish -and $localVersion -le $liveVersion) {
    throw 'Increment AssemblyVersion build/revision before publishing changed code. The existing registrar skips equal versions.'
}
if (-not $Publish) {
    Write-Host 'Check complete. No Dataverse writes. Use -Publish after reviewing the target and version.'
    return
}

& dotnet run --project $workerProject --no-launch-profile --configuration Release -- --register-plugin "--plugin-path=$dll"
if ($LASTEXITCODE -ne 0) { throw 'Plug-in registration failed. Inspect the Dataverse state before retrying.' }
$after = Get-LivePluginStatus
if ($after.Assembly.Version -ne $localVersion.ToString() -or $after.Assembly.PublicKeyToken.ToLowerInvariant() -ne $expectedToken) {
    throw 'Registration returned but the live plug-in identity/version did not match the local build.'
}
Write-Host "Published FMCentralBms.Plugins $localVersion to $expectedUrl. Export/unpack FMCentralBms solution source before committing."
