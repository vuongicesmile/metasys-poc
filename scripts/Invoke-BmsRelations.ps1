<#
.SYNOPSIS
Runs the scoped BMS Building/Equipment migration, seed or read-only checks.
.DESCRIPTION
Preview is the default and makes no cloud writes. Provision changes only BMS
relationship metadata/forms/views; Apply creates catalog rows and sets five
point lookups from the checked-in JSON manifest. No mode starts synchronization.
Test uses an in-memory Dataverse fixture and never connects to SQL or Dataverse.
#>
[CmdletBinding()]
param(
    [ValidateSet('Status','Provision','Preview','Apply','Verify','Test')]
    [string]$Mode = 'Preview',
    [string]$Manifest = '',
    [string]$Receipt = '',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$relationRepo = Split-Path $PSScriptRoot -Parent
$relationOutput = Join-Path $relationRepo '.artifacts\bms-relations\app'
$relationDll = Join-Path $relationOutput 'DataverseSyncWorker.dll'
if (-not $Manifest) { $Manifest = Join-Path $relationRepo 'config\bms-relations.seed.json' }
if ($Receipt -and $Mode -ne 'Apply') { throw 'Receipt can only be supplied with Mode Apply.' }
if (-not $NoBuild) {
    & dotnet build (Join-Path $relationRepo 'DataverseSyncWorker\DataverseSyncWorker.csproj') --configuration Release --artifacts-path (Join-Path $relationRepo '.artifacts\bms-relations\build') --output $relationOutput -p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
}
if (-not (Test-Path -LiteralPath $relationDll)) { throw 'Relationship command has not been built. Run without -NoBuild.' }
$relationModes = @{
    Status = '--bms-relations-status'
    Provision = '--provision-bms-relations'
    Preview = '--seed-bms-relations'
    Apply = '--seed-bms-relations'
    Verify = '--verify-bms-relations'
    Test = '--self-test-bms-relations'
}
$relationArgs = @($relationModes[$Mode], '--manifest', (Resolve-Path -LiteralPath $Manifest).Path)
if ($Mode -eq 'Preview') { $relationArgs += '--dry-run' }
if ($Mode -eq 'Apply') {
    if (-not $Receipt) {
        $relationFileName = 'seed-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N') + '.json'
        $Receipt = Join-Path $relationRepo (Join-Path '.artifacts\bms-relations\receipts' $relationFileName)
    }
    $relationArgs += @('--apply', '--receipt', $Receipt)
}
Write-Host "BMS relationships mode: $Mode"
& dotnet $relationDll @relationArgs
if ($LASTEXITCODE -ne 0) { throw "BMS relationship command failed: $LASTEXITCODE" }
