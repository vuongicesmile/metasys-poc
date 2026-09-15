<#
.SYNOPSIS
Watches archived SharePoint files in Dataverse and imports changed versions.
.DESCRIPTION
Runs the existing SpoIngestion.Cli watch-dataverse command. Keep the window
open while using Sync SharePoint now or Sync All now in FMC BMS Demo.
#>
[CmdletBinding()]
param(
    [ValidateRange(1,100)][int]$MaxFiles = 20,
    [ValidateRange(2,3600)][int]$PollSeconds = 10
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not available.'
}

Write-Host 'SharePoint -> Dataverse watcher'
Write-Host 'Waiting for archived file versions in fmc_spofile. Press Ctrl+C to stop.'
& dotnet run --project (Join-Path $repo 'SpoIngestion.Cli') `
    --configuration Release --no-launch-profile -- `
    watch-dataverse `
    --config (Join-Path $repo 'config/spo-ingestion.json') `
    --appsettings (Join-Path $repo 'DataverseSyncWorker/appsettings.json') `
    --max $MaxFiles `
    --poll-seconds $PollSeconds
if ($LASTEXITCODE -ne 0) { throw "SharePoint watcher exited with code $LASTEXITCODE" }
