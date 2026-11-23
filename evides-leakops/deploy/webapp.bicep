// A Linux web app (.NET 10) on an existing App Service plan, with a managed identity.

param name string
param location string
param tags object
param planId string
param appInsightsConnectionString string
@description('Keeps the app loaded between requests. Not available on the free F1 plan.')
param alwaysOn bool = true
@description('App settings as [{ name, value }].')
param settings array = []

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: planId
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: alwaysOn
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: concat([
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
      ], settings)
    }
  }
}

output principalId string = app.identity.principalId
output hostName string = app.properties.defaultHostName
output name string = app.name
