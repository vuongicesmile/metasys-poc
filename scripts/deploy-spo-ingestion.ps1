[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [Parameter(Mandatory)] [string] $ResourceGroupName,
    [Parameter(Mandatory)] [string] $Location,
    [Parameter(Mandatory)] [string] $FunctionAppName,
    [Parameter(Mandatory)] [string] $StorageAccountName
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$template = Join-Path $repository 'infra\spo-ingestion\main.bicep'
$publishFolder = Join-Path $repository '.artifacts\spo-function-publish'
$package = Join-Path $repository '.artifacts\spo-function.zip'
$connectionString = $env:SPO_DATAVERSE_CONNECTION_STRING

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is required and was not found on PATH.'
}
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw 'Set SPO_DATAVERSE_CONNECTION_STRING to the Dataverse application-user connection string.'
}
if ($StorageAccountName -notmatch '^[a-z0-9]{3,24}$') {
    throw 'StorageAccountName must contain 3-24 lowercase letters or digits.'
}

function Assert-NativeCommand([string] $Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

az account set --subscription $SubscriptionId
Assert-NativeCommand 'Select Azure subscription'
$selected = az account show --query id -o tsv
Assert-NativeCommand 'Read selected Azure subscription'
if ($selected -ne $SubscriptionId) {
    throw "Azure CLI selected subscription '$selected', expected '$SubscriptionId'."
}

az group create --name $ResourceGroupName --location $Location --output none
Assert-NativeCommand 'Create or resolve resource group'
az deployment group create `
    --resource-group $ResourceGroupName `
    --name spo-ingestion `
    --template-file $template `
    --parameters functionAppName=$FunctionAppName storageAccountName=$StorageAccountName dataverseConnectionString=$connectionString `
    --output none
Assert-NativeCommand 'Deploy SPO Azure resources'

if (Test-Path -LiteralPath $publishFolder) {
    $resolved = [IO.Path]::GetFullPath($publishFolder)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath((Join-Path $repository '.artifacts')), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $publishFolder -Force | Out-Null
dotnet publish (Join-Path $repository 'SPO.Ingestion.Functions\SPO.Ingestion.Functions.csproj') -c Release -o $publishFolder
Assert-NativeCommand 'Publish SPO Function project'

if (Test-Path -LiteralPath $package) {
    Remove-Item -LiteralPath $package -Force
}
Compress-Archive -Path (Join-Path $publishFolder '*') -DestinationPath $package -CompressionLevel Optimal
az functionapp deployment source config-zip `
    --resource-group $ResourceGroupName `
    --name $FunctionAppName `
    --src $package `
    --output none
Assert-NativeCommand 'Deploy SPO Function package'

$endpoint = az deployment group show `
    --resource-group $ResourceGroupName `
    --name spo-ingestion `
    --query properties.outputs.finalizeEndpoint.value `
    --output tsv
Assert-NativeCommand 'Read SPO deployment output'

Write-Host "SPO Function deployed: $FunctionAppName"
Write-Host "Finalize endpoint: $endpoint"
Write-Host 'Retrieve the function key securely in Azure Portal; do not commit it.'
