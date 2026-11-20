// A Flex Consumption function app (.NET 10, isolated worker) with its own plan.
// Flex Consumption bills only for the time a function runs (plus a free monthly grant) and has no always-ready instances here,
// so an idle demo costs nothing. The app uses its managed identity for the storage account; there are no storage keys.

param name string
param location string
param tags object
param storageAccountName string
param appInsightsConnectionString string
@description('Extra app settings as [{ name, value }]. Secrets that are only known after the first deployment are set by deploy.ps1.')
param settings array = []
@description('Upper limit for scale-out. Keeps a runaway caller from running up the bill.')
param maximumInstanceCount int = 10

// Built-in roles on the storage account: the Functions host keeps its state in blobs, queues and tables.
var storageRoles = [
  { id: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b', name: 'Storage Blob Data Owner' }
  { id: '974c5e8b-45b9-4653-ba55-5f855dd0fb88', name: 'Storage Queue Data Contributor' }
  { id: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3', name: 'Storage Table Data Contributor' }
]

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storage
  name: 'default'
}

// The deployment package of the app is stored in this container.
resource deployContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: '${name}-deploy'
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${name}-plan'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deployContainer.name}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: concat([
        { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
      ], settings)
    }
  }
}

resource storageAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in storageRoles: {
  scope: storage
  name: guid(storage.id, app.id, role.id)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role.id)
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}]

output principalId string = app.identity.principalId
output hostName string = app.properties.defaultHostName
output name string = app.name
