# Isolated launcher contract tests: mocks dotnet; no SQL or Dataverse calls.
$ErrorActionPreference = 'Stop'
$launcher = Join-Path (Split-Path $PSScriptRoot -Parent) 'Start-DataverseSync.ps1'
$credentialNames = @('Dataverse__ClientId', 'Dataverse__ClientSecret', 'Dataverse__CertificateThumbprint')
$savedCredentials = @{}
foreach ($name in $credentialNames) {
    $savedCredentials[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$global:DataverseLauncherTestCall = $null
$global:DataverseLauncherTestExitCode = 0

function dotnet {
    $global:DataverseLauncherTestCall = [PSCustomObject]@{
        Arguments = @($args)
        ClientId = $env:Dataverse__ClientId
        Secret = $env:Dataverse__ClientSecret
        Certificate = $env:Dataverse__CertificateThumbprint
    }
    $global:LASTEXITCODE = $global:DataverseLauncherTestExitCode
}
function Assert-Launcher([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Output "PASS: $Message"
}

try {
    $env:Dataverse__ClientId = 'existing-client'
    $env:Dataverse__ClientSecret = 'test-only-secret'
    $env:Dataverse__CertificateThumbprint = 'existing-certificate'

    # Omitted ClientId must not enter a credential prompt or dereference null.
    & $launcher -Mode Verify
    $call = $global:DataverseLauncherTestCall
    Assert-Launcher ($call.Arguments -contains '--verify') 'Verify maps to the read-only worker mode'
    Assert-Launcher (-not ($call.Arguments -contains '--Dataverse:DeveloperTokenPython=')) 'Default identity preserves the configured authentication'
    $projectIndex = [Array]::IndexOf($call.Arguments, '--project') + 1
    Assert-Launcher (Test-Path -LiteralPath $call.Arguments[$projectIndex]) 'Launcher resolves the project independently of the current directory'

    foreach ($mode in @('Run', 'Once', 'Provision')) {
        & $launcher -Mode $mode
        $commandFlags = @($global:DataverseLauncherTestCall.Arguments | Where-Object {
            $_ -in @('--verify', '--run-once', '--provision')
        })
        $expected = @{Run = ''; Once = '--run-once'; Provision = '--provision'}[$mode]
        if ($expected) {
            Assert-Launcher ($commandFlags.Count -eq 1 -and $commandFlags[0] -eq $expected) "$mode selects only its intended worker action"
        } else {
            Assert-Launcher ($commandFlags.Count -eq 0) 'Run selects continuous synchronization'
        }
    }

    $applicationId = [Guid]'11111111-2222-3333-4444-555555555555'
    & $launcher -Mode Verify -ClientId $applicationId -CertificateThumbprint 'test-certificate'
    $call = $global:DataverseLauncherTestCall
    Assert-Launcher ($call.ClientId -eq $applicationId.ToString() -and $call.Certificate -eq 'test-certificate' -and [string]::IsNullOrEmpty($call.Secret)) 'Explicit certificate identity is passed without a secret prompt'
    Assert-Launcher (($call.Arguments -contains '--Dataverse:DeveloperTokenPython=') -and ($call.Arguments -contains '--Dataverse:DeveloperTokenScript=')) 'Explicit application identity overrides both checked-in developer token paths'
    Assert-Launcher (@($call.Arguments | Where-Object { $_ -eq '--' }).Count -eq 1 -and $call.Arguments[-1] -eq '--verify') 'Application configuration and command share one dotnet argument separator'
    Assert-Launcher ($env:Dataverse__ClientId -eq 'existing-client' -and $env:Dataverse__ClientSecret -eq 'test-only-secret' -and $env:Dataverse__CertificateThumbprint -eq 'existing-certificate') 'Credential environment is restored after success'

    $global:DataverseLauncherTestExitCode = 17
    $failedAsExpected = $false
    try {
        & $launcher -Mode Once -ClientId $applicationId -CertificateThumbprint 'test-certificate'
    } catch {
        $failedAsExpected = $_.Exception.Message -eq 'Worker exited with code 17'
    }
    Assert-Launcher $failedAsExpected 'Worker failure is propagated to the caller'
    Assert-Launcher ($env:Dataverse__ClientId -eq 'existing-client' -and $env:Dataverse__ClientSecret -eq 'test-only-secret' -and $env:Dataverse__CertificateThumbprint -eq 'existing-certificate') 'Credential environment is restored after failure'

    $global:DataverseLauncherTestCall = $null
    $failedAsExpected = $false
    try {
        & $launcher -CertificateThumbprint 'test-certificate'
    } catch {
        $failedAsExpected = $_.Exception.Message -eq 'CertificateThumbprint requires an explicit ClientId.'
    }
    Assert-Launcher ($failedAsExpected -and $null -eq $global:DataverseLauncherTestCall) 'Incomplete certificate identity fails before worker startup'
} finally {
    foreach ($name in $credentialNames) {
        [Environment]::SetEnvironmentVariable($name, $savedCredentials[$name], 'Process')
    }
    Remove-Variable -Name DataverseLauncherTestCall,DataverseLauncherTestExitCode -Scope Global -ErrorAction SilentlyContinue
}
