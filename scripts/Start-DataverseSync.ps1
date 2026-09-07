param(
    [Guid]$ClientId,
    [ValidateSet('Run','Provision','Once','Verify')][string]$Mode = 'Run',
    [string]$CertificateThumbprint = ''
)
$ErrorActionPreference = 'Stop'
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
    if ($Mode -eq 'Provision') { $arguments += @('--','--provision') }
    if ($Mode -eq 'Once') { $arguments += @('--','--run-once') }
    if ($Mode -eq 'Verify') { $arguments += @('--','--verify') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Worker exited with code $LASTEXITCODE" }
} finally {
    $env:Dataverse__ClientId = $oldId
    $env:Dataverse__ClientSecret = $oldSecret
    $env:Dataverse__CertificateThumbprint = $oldCertificate
}
