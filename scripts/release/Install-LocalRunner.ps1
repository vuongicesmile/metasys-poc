[CmdletBinding()]
param(
    [string]$RunnerDirectory = 'D:\Coder\fmc-runner',
    [string]$GhPath = 'D:\Coder\AI-AGENT-PLATFORM\.tools\github-cli\bin\gh.exe',
    [string]$AzurePython = 'D:\Coder\AI-AGENT-PLATFORM\.tools\azure-cli\Scripts\python.exe'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repository = 'vuongicesmile/metasys-poc'
if (-not (Test-Path -LiteralPath $AzurePython)) { throw 'Existing Azure CLI Python is required.' }
foreach ($tool in @('git','pac','node','npm','python','dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing $tool" }
}
$runnerRoot = [IO.Path]::GetFullPath($RunnerDirectory)
if (Test-Path -LiteralPath (Join-Path $runnerRoot '.runner')) { throw 'Runner already configured; use its existing scheduled task.' }
New-Item -ItemType Directory -Path $runnerRoot -Force | Out-Null
$release = Invoke-RestMethod https://api.github.com/repos/actions/runner/releases/latest
$asset = @($release.assets | Where-Object name -match '^actions-runner-win-x64-.*\.zip$')
if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:[a-f0-9]{64}$') { throw 'No uniquely identifiable runner asset with SHA256 digest.' }
$zip = Join-Path $runnerRoot $asset[0].name
& curl.exe --fail --location --silent --show-error --max-time 180 --output $zip $asset[0].browser_download_url
if ($LASTEXITCODE -ne 0) { throw 'Runner download failed.' }
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset[0].digest.Substring(7)) { throw 'Runner checksum mismatch.' }
Expand-Archive -LiteralPath $zip -DestinationPath $runnerRoot -Force
# Reuse Git Credential Manager only for this process; never print or persist its token.
$credentialLines = "protocol=https`nhost=github.com`n`n" | git credential fill
if ($LASTEXITCODE -ne 0) { throw 'GitHub credential lookup failed.' }
$password = @($credentialLines | Where-Object { $_.StartsWith('password=') })
if ($password.Count -ne 1) { throw 'GitHub credential is unavailable.' }
$previousToken = $env:GH_TOKEN
$env:GH_TOKEN = $password[0].Substring(9)
try {
    $registration = & $GhPath api --method POST "repos/$repository/actions/runners/registration-token" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $registration.token) { throw 'Could not obtain runner registration token.' }
    Push-Location $runnerRoot
    try {
        & .\config.cmd --unattended --url "https://github.com/$repository" --token $registration.token --name 'FMC-Dataverse-Local' --labels 'fmc-dataverse-dev' --work '_work'
        if ($LASTEXITCODE -ne 0) { throw 'Runner registration failed.' }
    } finally { Pop-Location }
} finally {
    $env:GH_TOKEN = $previousToken
    $credentialLines = $null; $password = $null; $registration = $null
}
# Non-secret paths only. Interactive user is required to reuse this user's PAC/Azure sessions.
@("FMC_AZURE_PYTHON=$AzurePython", "FMC_RELEASE_STATE=$runnerRoot\release-state") | Set-Content -LiteralPath (Join-Path $runnerRoot '.env') -Encoding ASCII
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$startScript = Join-Path $runnerRoot 'start-runner.ps1'
@'
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
& .\run.cmd
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $startScript -Encoding UTF8
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument ('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $startScript) -WorkingDirectory $runnerRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
Register-ScheduledTask -TaskName 'FMC-Dataverse-ReleaseRunner' -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null
Start-ScheduledTask -TaskName 'FMC-Dataverse-ReleaseRunner'
Write-Host "Runner registered. Task: FMC-Dataverse-ReleaseRunner. Artifacts/state: $runnerRoot."
