// ============================================================================
// Module: Role Assignments (RBAC)
// Grants Foundry managed identity access to all dependent resources.
// ============================================================================

param foundryAccountId string
param foundryProjectId string
param searchServiceId string
param cosmosAccountId string
param storageAccountId string
param keyVaultId string
param foundryPrincipalId string
param functionAppPrincipalId string
param deployerObjectId string

@description('GitHub Actions Service Principal Object ID for OIDC deployment')
param githubActionsPrincipalId string = ''

// ---------------------------------------------------------------------------
// Well-Known Role Definition IDs
// ---------------------------------------------------------------------------

var roles = {
  cognitiveServicesUser: '${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/a97b65f3-24c7-4388-baec-2e87135dc908'
  searchIndexContributor: '${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/8ebe5a00-799e-43f5-93ac-243d3dce84a7'
  searchServiceContributor: '${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/7ca78c08-252a-4471-8644-bb5ff32d4ba0'
  cosmosDbContributor: '${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/230815da-be43-4aae-9cb4-875f7bd000aa'
  storageBlobContributor: '${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  keyVaultSecretsUser: '${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6'
}

// ---------------------------------------------------------------------------
// Foundry → AI Search (index read/write for Foundry IQ)
// ---------------------------------------------------------------------------

resource foundrySearchIndexRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchServiceId, foundryPrincipalId, 'SearchIndexContributor')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.searchIndexContributor
    principalId: foundryPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource foundrySearchServiceRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchServiceId, foundryPrincipalId, 'SearchServiceContributor')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.searchServiceContributor
    principalId: foundryPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Foundry → Storage (blob read/write for file uploads)
// ---------------------------------------------------------------------------

resource foundryStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccountId, foundryPrincipalId, 'StorageBlobContributor')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.storageBlobContributor
    principalId: foundryPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Foundry → Key Vault (secrets access)
// ---------------------------------------------------------------------------

resource foundryKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, foundryPrincipalId, 'KeyVaultSecretsUser')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.keyVaultSecretsUser
    principalId: foundryPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Deployer → Cognitive Services User (for agent management)
// ---------------------------------------------------------------------------

resource deployerCogServicesRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccountId, deployerObjectId, 'CognitiveServicesUser')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.cognitiveServicesUser
    principalId: deployerObjectId
    principalType: 'User'
  }
}

// ---------------------------------------------------------------------------
// Deployer → Storage Blob Contributor
// ---------------------------------------------------------------------------

resource deployerStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccountId, deployerObjectId, 'StorageBlobContributor-deployer')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.storageBlobContributor
    principalId: deployerObjectId
    principalType: 'User'
  }
}

// ---------------------------------------------------------------------------
// GitHub Actions → Storage Blob Contributor (for deployment uploads)
// ---------------------------------------------------------------------------

resource githubActionsStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(githubActionsPrincipalId)) {
  name: guid(storageAccountId, githubActionsPrincipalId, 'StorageBlobContributor-github')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.storageBlobContributor
    principalId: githubActionsPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Function App → Foundry (Cognitive Services User)
// ---------------------------------------------------------------------------

resource functionAppFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccountId, functionAppPrincipalId, 'CognitiveServicesUser')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.cognitiveServicesUser
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Function App → AI Search
// ---------------------------------------------------------------------------

resource functionAppSearchRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchServiceId, functionAppPrincipalId, 'SearchIndexContributor')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.searchIndexContributor
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Function App → Cosmos DB
// ---------------------------------------------------------------------------

resource functionAppCosmosRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(cosmosAccountId, functionAppPrincipalId, 'CosmosDbContributor')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.cosmosDbContributor
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Function App → Storage
// ---------------------------------------------------------------------------

resource functionAppStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccountId, functionAppPrincipalId, 'StorageBlobContributor')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.storageBlobContributor
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Function App → Key Vault
// ---------------------------------------------------------------------------

resource functionAppKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, functionAppPrincipalId, 'KeyVaultSecretsUser')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: roles.keyVaultSecretsUser
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}
