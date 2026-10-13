targetScope = 'subscription'

param location string
param resourceGroupTags object = {}
param resourceGroupName string
param userGroupId string

var resourceSuffix string = take(uniqueString(userGroupId), 12)

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
  tags: resourceGroupTags
}

module contributorAccess './resource-group-contributor.bicep' = {
  name: 'resource-group-contributor'
  scope: resourceGroup
  params: {
    userGroupId: userGroupId
  }
}

module labServices './lab-services.bicep' = {
  name: 'lab-services'
  scope: resourceGroup
  params: {
    location: location
    resourceSuffix: resourceSuffix
    userGroupId: userGroupId
  }
}
