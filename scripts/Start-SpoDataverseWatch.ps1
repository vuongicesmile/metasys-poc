<#
.SYNOPSIS
Watches archived SharePoint files in Dataverse and imports changed versions.
.DESCRIPTION
Runs the existing SPO.Ingestion.Cli watch-dataverse command. Keep the window
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

$logDirectory = Join-Path $repo '.artifacts\spo-watch'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$logPath = Join-Path $logDirectory 'watch.log'
$existingWatchers = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.ProcessId -ne $PID -and
        $_.CommandLine -match 'SpoIngestion\.Cli' -and
        $_.CommandLine -match 'watch-dataverse'
    })
if ($existingWatchers.Count -gt 0) {
    Write-Host 'SharePoint watcher is already running; no duplicate was started.' -ForegroundColor Yellow
    Write-Host "Existing process IDs: $($existingWatchers.ProcessId -join ', ')"
    Write-Host "Log file: $logPath"
    Read-Host 'Press Enter to close this window'
    exit 0
}

Write-Host 'SharePoint -> Dataverse watcher'
Write-Host 'Waiting for archived file versions in fmc_spofile. Press Ctrl+C to stop.'
$logStart = "[$(Get-Date -Format o)] Starting SharePoint watcher`r`n"
Add-Content -LiteralPath $logPath -Value $logStart -Encoding UTF8
Write-Host "Log file: $logPath"
& dotnet run --project (Join-Path $repo 'SPO.Ingestion.Cli') `
    --configuration Release --no-launch-profile -- `
    watch-dataverse `
    --config (Join-Path $repo 'config/spo-ingestion.json') `
    --appsettings (Join-Path $repo 'Dataverse.SyncWorker/Dataverse.SyncWorker.App/appsettings.json') `
    --max $MaxFiles `
    --poll-seconds $PollSeconds 2>&1 | Tee-Object -FilePath $logPath -Append
$exitCode = $LASTEXITCODE
Add-Content -LiteralPath $logPath -Value "[$(Get-Date -Format o)] Watcher exited with code $exitCode`r`n" -Encoding UTF8
if ($exitCode -ne 0) {
    Write-Host "SharePoint watcher exited with code $exitCode. See $logPath" -ForegroundColor Red
} else {
    Write-Host "SharePoint watcher stopped. See $logPath" -ForegroundColor Yellow
}
Read-Host 'Press Enter to close this window'
