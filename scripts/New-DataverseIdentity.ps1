param([string]$Role = 'FM Central BMS Integration')
$ErrorActionPreference = 'Stop'
$credentialDirectory = Join-Path $env:LOCALAPPDATA 'MetasysPoc'
$credentialFile = Join-Path $credentialDirectory 'dataverse-bootstrap.xml'
if (Test-Path -LiteralPath $credentialFile) { throw 'Bootstrap credential already exists. Refusing to create another app.' }
$result = (& pac admin create-service-principal --environment '5abcb0e5-99b2-e51f-aa0e-90d84405798b' --name 'FM Central BMS Integration' --role $Role 2>&1 | Out-String)
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0 -or $result -match '(?im)^Error:') {
    $sanitized = $result -replace '(?im)^.*(secret|password|token).*$','[credential-related output omitted]'
    Write-Output $sanitized
    throw "PAC identity creation failed (exit $exitCode)."
}
# Protect the entire PAC response using Windows DPAPI before parsing it locally.
New-Item -ItemType Directory -Path $credentialDirectory -Force | Out-Null
ConvertTo-SecureString $result -AsPlainText -Force | Export-Clixml -LiteralPath $credentialFile
Write-Output "Created application identity. Encrypted bootstrap details saved to $credentialFile"
