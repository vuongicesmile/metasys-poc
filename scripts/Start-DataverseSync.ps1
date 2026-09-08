<#
.SYNOPSIS
Starts continuous SQL-to-Dataverse synchronization or runs a maintenance mode.
.DESCRIPTION
Run (the default) starts the configured worker mode. CommandDriven waits for
Dataverse requests; Continuous processes pending SQL rows until Ctrl+C.
Once processes ONE batch. Verify performs read-only
reconciliation. Provision changes Dataverse schema and security-role metadata.
Omit ClientId to reuse the configured developer identity. An explicit ClientId
selects application authentication; use CertificateThumbprint to avoid a secret
prompt. See docs/runbooks/sql-to-dataverse-runbook.vi.md for the repeatable setup.
.EXAMPLE
.\scripts\Start-DataverseSync.ps1
.EXAMPLE
.\scripts\Start-DataverseSync.ps1 -Mode Verify
#>
[CmdletBinding()]
param(
    [Guid]$ClientId = [Guid]::Empty,
    [ValidateSet('Run','Provision','Once','Verify')][string]$Mode = 'Run',
    [string]$CertificateThumbprint = ''
)
$ErrorActionPreference = 'Stop'
if ($CertificateThumbprint -and $ClientId -eq [Guid]::Empty) {
    throw 'CertificateThumbprint requires an explicit ClientId.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not available. Install the SDK required by DataverseSyncWorker and reopen the terminal.'
}
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'DataverseSyncWorker'
$oldId = $env:Dataverse__ClientId
$oldSecret = $env:Dataverse__ClientSecret
$oldCertificate = $env:Dataverse__CertificateThumbprint
try {
    if ($ClientId -ne [Guid]::Empty) {
        $env:Dataverse__ClientId = $ClientId.ToString()
        $env:Dataverse__CertificateThumbprint = $CertificateThumbprint
        if ($CertificateThumbprint) { $env:Dataverse__ClientSecret = '' }
        else {
            $secret = Read-Host 'Dataverse client secret (not saved to disk)' -AsSecureString
            $env:Dataverse__ClientSecret = [System.Net.NetworkCredential]::new('', $secret).Password
        }
    }
    $arguments = @('run','--project',$project,'--no-launch-profile','--configuration','Release')
    # Command-line configuration overrides the checked-in developer-token paths.
    # Empty process environment values are removed by Windows PowerShell 5.1,
    # so they cannot reliably override appsettings.json here.
    if ($ClientId -ne [Guid]::Empty) {
        $arguments += @('--', '--Dataverse:DeveloperTokenPython=', '--Dataverse:DeveloperTokenScript=')
    }
    if ($ClientId -eq [Guid]::Empty -and $Mode -ne 'Run') { $arguments += '--' }
    if ($Mode -eq 'Provision') { $arguments += '--provision' }
    if ($Mode -eq 'Once') { $arguments += '--run-once' }
    if ($Mode -eq 'Verify') { $arguments += '--verify' }
    Write-Host "SQL -> Dataverse mode: $Mode"
    if ($Mode -eq 'Run') {
        Write-Host 'Worker host: waits for Power Automate requests in CommandDriven mode. Press Ctrl+C to stop.'
        Write-Host 'Default status page: http://localhost:5300/swagger'
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Worker exited with code $LASTEXITCODE" }
} finally {
    $env:Dataverse__ClientId = $oldId
    $env:Dataverse__ClientSecret = $oldSecret
    $env:Dataverse__CertificateThumbprint = $oldCertificate
}
