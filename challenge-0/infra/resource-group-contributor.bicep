targetScope = 'resourceGroup'

metadata name = 'Resource Group Contributor Access'
metadata description = 'Assigns the Contributor role to a Microsoft Entra group at resource-group scope.'

@description('Microsoft Entra object ID of the group that receives Contributor access.')
param userGroupId string

var contributorRoleDefinitionId string = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b24988ac-6180-42a0-ab88-20f7382dd24c'
)

resource contributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, userGroupId, contributorRoleDefinitionId)
  properties: {
    roleDefinitionId: contributorRoleDefinitionId
    principalId: userGroupId
    principalType: 'Group'
  }
}
