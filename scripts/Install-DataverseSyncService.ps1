<#
.SYNOPSIS
Publishes and registers the command-driven SQL-to-Dataverse worker as a Windows service.
.DESCRIPTION
This script intentionally requires a certificate-based Dataverse application identity.
It never stores a client secret. It does not create the Entra app, install a certificate,
grant certificate private-key access, or grant SQL permissions to the service account.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][Guid]$ClientId,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9 ]+$')][string]$CertificateThumbprint,
    [ValidateSet('NT AUTHORITY\NetworkService','NT AUTHORITY\LocalService')]
    [string]$ServiceAccount = 'NT AUTHORITY\NetworkService',
    [string]$ServiceName = 'FMCentralDataverseSync',
    [string]$InstallDirectory = 'C:\ProgramData\FMCentralBms\DataverseSyncWorker',
    [switch]$Start
)
$ErrorActionPreference = 'Stop'
$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'DataverseSyncWorker\DataverseSyncWorker.csproj'
$expectedRoot = [System.IO.Path]::GetFullPath('C:\ProgramData\FMCentralBms')
$resolvedInstall = [System.IO.Path]::GetFullPath($InstallDirectory)
if (-not $resolvedInstall.StartsWith($expectedRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallDirectory must stay below $expectedRoot"
}
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Stop and update it through the controlled deployment procedure."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK is required for publish.' }
$certificate = Get-ChildItem -LiteralPath "Cert:\LocalMachine\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
    throw "Certificate $normalizedThumbprint with a private key was not found in LocalMachine\My."
}

if (-not $PSCmdlet.ShouldProcess($resolvedInstall, "Publish and register Windows service $ServiceName")) { return }
New-Item -ItemType Directory -Path $resolvedInstall -Force | Out-Null
dotnet publish $project -c Release -o $resolvedInstall --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$productionConfig = @{
    Dataverse = @{
        ExecutionMode = 'CommandDriven'
        ClientId = $ClientId.ToString('D')
        ClientSecret = ''
        CertificateThumbprint = $normalizedThumbprint
        CertificateStoreLocation = 'LocalMachine'
        DeveloperTokenPython = ''
        DeveloperTokenScript = ''
        ProvisionPowerAutomate = $false
    }
}
$productionConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resolvedInstall 'appsettings.Production.json') -Encoding UTF8

$executable = Join-Path $resolvedInstall 'DataverseSyncWorker.exe'
$binaryPath = ('"{0}" --environment Production' -f $executable)
& sc.exe create $ServiceName 'binPath=' $binaryPath 'start=' 'auto' 'obj=' $ServiceAccount
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE" }
& sc.exe description $ServiceName 'Processes queued FM Central SQL-to-Dataverse sync requests.' | Out-Null
& sc.exe failure $ServiceName 'reset=' '86400' 'actions=' 'restart/5000/restart/15000/restart/60000' | Out-Null
& sc.exe failureflag $ServiceName '1' | Out-Null

Write-Output "Registered $ServiceName as $ServiceAccount."
Write-Output 'Before starting, grant this account least-privilege SQL access and read access to the certificate private key.'
if ($Start) {
    Start-Service -Name $ServiceName
    Get-Service -Name $ServiceName
}
