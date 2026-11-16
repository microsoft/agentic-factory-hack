// Knowledge layer for the LeakOps agents (Foundry IQ): an Azure AI Search service that hosts the knowledge base, an embedding model on the
// existing Foundry resource, a blob container for the knowledge documents, the access rights between them (no keys), and the project
// connection the agents use to reach the knowledge base over MCP.
// Separate from main.bicep on purpose: Search Basic cannot be paused (about 70 EUR a month), so this layer can be added and removed on its own.
// Deploy with deploy/deploy-knowledge.ps1, which also uploads the documents and creates the knowledge sources and the knowledge base.

targetScope = 'resourceGroup'

@description('Same suffix as main.bicep (deploy-knowledge.ps1 derives it from the subscription and resource group).')
@minLength(4)
@maxLength(8)
param suffix string
@description('Region of the search service. Sweden Central and West Europe are flagged as high demand for new search services; use another region that offers agentic retrieval when creation fails (for example francecentral, switzerlandnorth, uksouth).')
param searchLocation string = 'swedencentral'
@description('Object id of the person who deploys and demos: may create the knowledge base and upload the documents.')
param deployerPrincipalId string = ''
@description('Knowledge base for the operational agents (Triage, Diagnostic, Procurement): procedures, playbooks, policy and past incident reviews.')
param opsKnowledgeBase string = 'leakops-ops-kb'
@description('Separate knowledge base for the Communications agent: only the public communication guidelines. Internal documents never reach the agent that writes for residents.')
param commsKnowledgeBase string = 'leakops-comms-kb'
@description('Version of the search API the MCP endpoint of the knowledge base is called with. Pinned after testing: preview versions change.')
param knowledgeApiVersion string = '2026-08-01-preview'
param embeddingModelName string = 'text-embedding-3-small'
param embeddingModelVersion string = '1'
@description('Thousands of tokens per minute. Tiny: the knowledge is a few documents.')
param embeddingCapacity int = 50

var tags = { project: 'evides-leakops', demo: 'evides-ai-foundry' }
var foundryName = 'evides-foundry-${suffix}'
var projectName = 'evides-leakops'
var storageName = 'evidessa${suffix}'
var searchName = 'evides-search-${suffix}'
var knowledgeContainer = 'knowledge'

var storageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var cognitiveServicesUser = 'a97b65f3-24c7-4388-baec-2e87135dc908'
var searchIndexDataReader = '1407120a-92aa-4202-b7e9-c0e197c71c8f'
var searchIndexDataContributor = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
var searchServiceContributor = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'

resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundry
  name: projectName
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storage
  name: 'default'
}

resource knowledgeBlobs 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: knowledgeContainer
  properties: { publicAccess: 'None' }
}

// ---- Azure AI Search: Entra ID only (no API keys), identity for reading the blobs and calling the models ------------

resource search 'Microsoft.Search/searchServices@2025-05-01' = {
  name: searchName
  location: searchLocation
  tags: tags
  sku: { name: 'basic' }
  identity: { type: 'SystemAssigned' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'Default'
    publicNetworkAccess: 'enabled'
    disableLocalAuth: true
    semanticSearch: 'free'
  }
}

// ---- Embedding model on the existing Foundry resource ----------------------------------------------------------------

resource embedding 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: embeddingModelName
  sku: {
    name: 'GlobalStandard'
    capacity: embeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: embeddingModelName
      version: embeddingModelVersion
    }
  }
}

// ---- Access rights ---------------------------------------------------------------------------------------------------

// The search service reads the documents and calls the models (embeddings while indexing, gpt-5.4 for query planning).
resource searchReadsBlobs 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, search.id, storageBlobDataReader)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReader)
    principalId: search.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource searchCallsModels 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: foundry
  name: guid(foundry.id, search.id, cognitiveServicesUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUser)
    principalId: search.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// The agents reach the knowledge base through the Foundry project, which signs in with its own managed identity.
resource projectReadsIndexes 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: search
  name: guid(search.id, foundryProject.id, searchIndexDataReader)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataReader)
    principalId: foundryProject.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource deployerManagesSearch 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  scope: search
  name: guid(search.id, deployerPrincipalId, searchServiceContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributor)
    principalId: deployerPrincipalId
    principalType: 'User'
  }
}

resource deployerWritesIndexes 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  scope: search
  name: guid(search.id, deployerPrincipalId, searchIndexDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributor)
    principalId: deployerPrincipalId
    principalType: 'User'
  }
}

resource deployerUploadsDocuments 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  scope: storage
  name: guid(storage.id, deployerPrincipalId, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: deployerPrincipalId
    principalType: 'User'
  }
}

// ---- Project connections: how a Foundry agent reaches a knowledge base (MCP tool knowledge_base_retrieve) -----------------
// One connection per knowledge base, so every agent only gets the knowledge it needs.

resource opsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-10-01-preview' = {
  parent: foundryProject
  name: 'leakops-kb-ops'
  properties: {
    category: 'RemoteTool'
    authType: 'ProjectManagedIdentity'
    target: 'https://${search.name}.search.windows.net/knowledgebases/${opsKnowledgeBase}/mcp?api-version=${knowledgeApiVersion}'
    isSharedToAll: false
    audience: 'https://search.azure.com/'
    metadata: { ApiType: 'Azure' }
  }
  dependsOn: [ projectReadsIndexes ]
}

resource commsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-10-01-preview' = {
  parent: foundryProject
  name: 'leakops-kb-comms'
  properties: {
    category: 'RemoteTool'
    authType: 'ProjectManagedIdentity'
    target: 'https://${search.name}.search.windows.net/knowledgebases/${commsKnowledgeBase}/mcp?api-version=${knowledgeApiVersion}'
    isSharedToAll: false
    audience: 'https://search.azure.com/'
    metadata: { ApiType: 'Azure' }
  }
  dependsOn: [ projectReadsIndexes ]
}

output searchName string = search.name
output searchEndpoint string = 'https://${search.name}.search.windows.net'
output knowledgeContainer string = knowledgeContainer
output embeddingDeployment string = embedding.name
output opsKnowledgeBase string = opsKnowledgeBase
output commsKnowledgeBase string = commsKnowledgeBase
output opsConnection string = opsConnection.name
output commsConnection string = commsConnection.name
output foundryEndpoint string = 'https://${foundryName}.services.ai.azure.com'
