<#
.SYNOPSIS
Creates an ignored, per-machine Dataverse configuration for the SQL worker and SPO CLI.
.EXAMPLE
.\scripts\Setup-HomeDataverse.ps1 -PythonPath (Get-Command python).Source -SigningKeyPath C:\secure\FMCentralBms.Plugins.snk
#>
[CmdletBinding()]
param(
    [string]$PythonPath = 'python',
    [string]$SqlServer = 'localhost',
    [string]$SigningKeyPath = '',
    [string]$OutputPath = '',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputPath) {
    $OutputPath = Join-Path $repo 'Dataverse.SyncWorker\Dataverse.SyncWorker.App\appsettings.Local.json'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Local configuration already exists: $OutputPath. Edit it manually or use -Force to replace it."
}
if (-not $SqlServer.Trim()) { throw 'SqlServer cannot be empty.' }
$pythonCommand = Get-Command $PythonPath -ErrorAction SilentlyContinue
if (-not $pythonCommand) { throw "Python executable not found: $PythonPath" }
$pythonExe = $pythonCommand.Source
if ($SigningKeyPath) {
    $SigningKeyPath = [IO.Path]::GetFullPath($SigningKeyPath)
    if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) {
        throw "Original plug-in signing key not found: $SigningKeyPath"
    }
}
$tokenScript = Join-Path $repo 'scripts\get-dataverse-token.py'
$settings = [ordered]@{
    ConnectionStrings = [ordered]@{
        Sql = "Server=$SqlServer;Database=FM_Central;Trusted_Connection=True;TrustServerCertificate=True;"
    }
    Dataverse = [ordered]@{
        DeveloperTokenPython = $pythonExe
        DeveloperTokenScript = $tokenScript
    }
    LocalPlugin = [ordered]@{
        SigningKeyPath = $SigningKeyPath
    }
}
$directory = Split-Path $OutputPath -Parent
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Wrote per-machine configuration: $OutputPath"
Write-Host 'Target organization remains guarded by checked-in appsettings.json.'
if (-not $SigningKeyPath) { Write-Host 'SigningKeyPath is empty; the checked-in original plug-in key will be used.' }
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Warning 'az is not on PATH. Install Azure CLI and run az login, or use the existing bundled azure.cli Python environment.'
}
