// The interactive demo website: a static site (no backend, no keys) on Azure Static Web Apps, Free tier. Costs nothing.
// It lives in the same resource group as the LeakOps demo. Deploy with deploy/deploy-demosite.ps1, which also uploads the site.

targetScope = 'resourceGroup'

@description('Short unique suffix (deploy-demosite.ps1 derives it from the subscription and resource group, same as deploy.ps1).')
@minLength(4)
@maxLength(8)
param suffix string
@description('Static Web Apps are only available in a few regions; this is the closest one to Sweden Central.')
@allowed([ 'westeurope', 'westus2', 'centralus', 'eastus2', 'eastasia' ])
param location string = 'westeurope'

var tags = { project: 'evides-leakops', demo: 'evides-ai-foundry' }

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'leakops-demo-${suffix}'
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    provider: 'None' // content is uploaded with a deployment token, not pulled from a repository
    stagingEnvironmentPolicy: 'Disabled'
    allowConfigFileUpdates: true
  }
}

output name string = site.name
output url string = 'https://${site.properties.defaultHostname}'
