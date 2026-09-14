targetScope = 'resourceGroup'

@description('Globally unique Function App name.')
@minLength(2)
@maxLength(60)
param functionAppName string

@description('Globally unique lowercase Storage account name.')
@minLength(3)
@maxLength(24)
param storageAccountName string

param location string = resourceGroup().location

@secure()
@description('Dataverse application-user connection string. Never commit the value.')
param dataverseConnectionString string

param spoQueueName string = 'spo-ingestion'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource rawContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'spo-raw'
  properties: {
    publicAccess: 'None'
  }
}

resource controlContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'spo-control'
  properties: {
    publicAccess: 'None'
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource ingestionQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: spoQueueName
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${functionAppName}-plan'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: false
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${functionAppName}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      netFrameworkVersion: 'v10.0'
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'SpoStorage'
          value: storageConnectionString
        }
        {
          name: 'SpoQueueName'
          value: spoQueueName
        }
        {
          name: 'DataverseConnectionString'
          value: dataverseConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: insights.properties.ConnectionString
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
    }
  }
  dependsOn: [
    rawContainer
    controlContainer
    ingestionQueue
  ]
}

output functionAppName string = functionApp.name
output finalizeEndpoint string = 'https://${functionApp.properties.defaultHostName}/api/spo/captures/finalize'
output deployedStorageAccountName string = storage.name
output rawContainerName string = rawContainer.name
output controlContainerName string = controlContainer.name
output queueName string = ingestionQueue.name
