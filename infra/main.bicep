// ============================================================================
// Discovery Chatbot - Main Infrastructure Orchestrator
// ============================================================================

targetScope = 'resourceGroup'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@description('Resource naming prefix')
param prefix string

@description('Environment suffix (dev, staging, prod)')
param suffix string

@description('Azure region')
param location string = resourceGroup().location

@description('Tags for all resources')
param tags object = {}

@description('Primary model name')
param primaryModelName string = 'gpt-5.2'

@description('Primary model version')
param primaryModelVersion string = '2025-01-01'

@description('Primary model capacity (thousands TPM)')
param primaryModelCapacity int = 50

@description('Fallback model name')
param fallbackModelName string = 'gpt-4.1-mini'

@description('Fallback model version')
param fallbackModelVersion string = '2024-07-18'

@description('Fallback model capacity')
param fallbackModelCapacity int = 30

@description('Deployer AAD Object ID for RBAC')
param deployerObjectId string

@description('GitHub Actions Service Principal Object ID for CI/CD')
param githubActionsPrincipalId string = ''

@description('Enable public network access')
param enablePublicAccess bool = true

@description('Cosmos DB consistency level')
@allowed(['Session', 'BoundedStaleness', 'Strong', 'ConsistentPrefix', 'Eventual'])
param cosmosConsistency string = 'Session'

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var baseName = '${prefix}${suffix}'
var uniqueSuffix = substring(uniqueString(resourceGroup().id, baseName), 0, 6)

// ---------------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------------

module foundry 'modules/foundry-account.bicep' = {
  name: 'deploy-foundry'
  params: {
    accountName: '${baseName}-foundry-${uniqueSuffix}'
    projectName: '${baseName}-project'
    location: location
    tags: tags
    enablePublicAccess: enablePublicAccess
    primaryModelName: primaryModelName
    primaryModelVersion: primaryModelVersion
    primaryModelCapacity: primaryModelCapacity
    fallbackModelName: fallbackModelName
    fallbackModelVersion: fallbackModelVersion
    fallbackModelCapacity: fallbackModelCapacity
  }
}

module aiSearch 'modules/ai-search.bicep' = {
  name: 'deploy-ai-search'
  params: {
    searchServiceName: '${baseName}-search-${uniqueSuffix}'
    location: location
    tags: tags
    sku: 'basic'
  }
}

module cosmos 'modules/cosmos-db.bicep' = {
  name: 'deploy-cosmos'
  params: {
    accountName: '${baseName}-cosmos-${uniqueSuffix}'
    location: location
    tags: tags
    consistencyLevel: cosmosConsistency
    databaseName: 'discovery'
    containers: [
      { name: 'knowledge-items', partitionKeyPath: '/relatedContextId' }
      { name: 'discovery-sessions', partitionKeyPath: '/contextId' }
      { name: 'questionnaires', partitionKeyPath: '/questionnaireId' }
      { name: 'user-profiles', partitionKeyPath: '/userId' }
    ]
  }
}

module storage 'modules/storage.bicep' = {
  name: 'deploy-storage'
  params: {
    storageAccountName: replace('${baseName}stor${uniqueSuffix}', '-', '')
    location: location
    tags: tags
    containers: [ 'uploads', 'questionnaires', 'exports' ]
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'deploy-keyvault'
  params: {
    keyVaultName: '${baseName}-kv-${uniqueSuffix}'
    location: location
    tags: tags
    deployerObjectId: deployerObjectId
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'deploy-appinsights'
  params: {
    appInsightsName: '${baseName}-insights-${uniqueSuffix}'
    workspaceName: '${baseName}-logs-${uniqueSuffix}'
    location: location
    tags: tags
  }
}

module functionApp 'modules/function-app.bicep' = {
  name: 'deploy-function-app'
  params: {
    functionAppName: '${baseName}-func-${uniqueSuffix}'
    appServicePlanName: '${baseName}-plan-${uniqueSuffix}'
    storageAccountName: replace('${baseName}func${uniqueSuffix}', '-', '')
    dataStorageAccountName: storage.outputs.storageAccountName
    appInsightsConnectionString: appInsights.outputs.connectionString
    location: location
    tags: tags
    additionalCorsOrigins: [
      'https://${baseName}-web-${uniqueSuffix}.azurestaticapps.net'
    ]
  }
}

module botService 'modules/bot-service.bicep' = {
  name: 'deploy-bot-service'
  params: {
    botName: '${baseName}-bot-${uniqueSuffix}'
    location: 'global'
    tags: tags
    functionAppEndpoint: 'https://${functionApp.outputs.functionAppHostName}'
    appInsightsKey: appInsights.outputs.instrumentationKey
  }
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'deploy-static-web-app'
  params: {
    staticWebAppName: '${baseName}-web-${uniqueSuffix}'
    location: location
    tags: tags
    functionAppName: functionApp.outputs.functionAppName
  }
}

module rbac 'modules/role-assignments.bicep' = {
  name: 'deploy-rbac'
  params: {
    foundryAccountId: foundry.outputs.accountId
    foundryProjectId: foundry.outputs.projectId
    searchServiceId: aiSearch.outputs.searchServiceId
    cosmosAccountId: cosmos.outputs.accountId
    storageAccountId: storage.outputs.storageAccountId
    keyVaultId: keyVault.outputs.keyVaultId
    foundryPrincipalId: foundry.outputs.principalId
    functionAppPrincipalId: functionApp.outputs.functionAppPrincipalId
    deployerObjectId: deployerObjectId
    githubActionsPrincipalId: githubActionsPrincipalId
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output FOUNDRY_ENDPOINT string = foundry.outputs.projectEndpoint
output FOUNDRY_ACCOUNT_NAME string = foundry.outputs.accountName
output FOUNDRY_PROJECT_NAME string = foundry.outputs.projectName
output PRIMARY_MODEL_DEPLOYMENT string = primaryModelName
output FALLBACK_MODEL_DEPLOYMENT string = fallbackModelName
output AI_SEARCH_ENDPOINT string = aiSearch.outputs.searchEndpoint
output AI_SEARCH_NAME string = aiSearch.outputs.searchServiceName
output COSMOS_ENDPOINT string = cosmos.outputs.endpoint
output COSMOS_DATABASE string = 'discovery'
output STORAGE_ACCOUNT_NAME string = storage.outputs.storageAccountName
output STORAGE_ENDPOINT string = storage.outputs.blobEndpoint
output KEY_VAULT_URI string = keyVault.outputs.vaultUri
output APP_INSIGHTS_CONNECTION string = appInsights.outputs.connectionString
output BOT_NAME string = botService.outputs.botName
output FUNCTION_APP_NAME string = functionApp.outputs.functionAppName
output FUNCTION_APP_HOSTNAME string = functionApp.outputs.functionAppHostName
output STATIC_WEB_APP_URL string = staticWebApp.outputs.staticWebAppUrl
output STATIC_WEB_APP_NAME string = staticWebApp.outputs.staticWebAppName
