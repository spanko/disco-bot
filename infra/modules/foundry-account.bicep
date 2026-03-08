// ============================================================================
// Module: Microsoft Foundry Account, Project, and Model Deployments
// ============================================================================

param accountName string
param projectName string
param location string
param tags object
param enablePublicAccess bool

param primaryModelName string
param primaryModelVersion string
param primaryModelCapacity int
param fallbackModelName string
param fallbackModelVersion string
param fallbackModelCapacity int

// ---------------------------------------------------------------------------
// Foundry Account (top-level AI Services resource)
// ---------------------------------------------------------------------------

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: accountName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    publicNetworkAccess: enablePublicAccess ? 'Enabled' : 'Disabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: enablePublicAccess ? 'Allow' : 'Deny'
    }
    allowProjectManagement: true
    customSubDomainName: accountName
    disableLocalAuth: false
  }
}

// ---------------------------------------------------------------------------
// Foundry Project (isolated workspace for agents)
// ---------------------------------------------------------------------------

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  name: projectName
  parent: foundryAccount
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

// ---------------------------------------------------------------------------
// Primary Model Deployment (GPT-5.2)
// ---------------------------------------------------------------------------

resource primaryModel 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: foundryAccount
  name: primaryModelName
  sku: {
    capacity: primaryModelCapacity
    name: 'GlobalStandard'
  }
  properties: {
    model: {
      name: primaryModelName
      format: 'OpenAI'
      version: primaryModelVersion
    }
  }
}

// ---------------------------------------------------------------------------
// Fallback Model Deployment (GPT-4.1-mini)
// ---------------------------------------------------------------------------

resource fallbackModel 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: foundryAccount
  name: fallbackModelName
  sku: {
    capacity: fallbackModelCapacity
    name: 'GlobalStandard'
  }
  properties: {
    model: {
      name: fallbackModelName
      format: 'OpenAI'
      version: fallbackModelVersion
    }
  }
  dependsOn: [primaryModel] // Sequential deployment to avoid conflicts
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output accountId string = foundryAccount.id
output accountName string = foundryAccount.name
output projectId string = foundryProject.id
output projectName string = foundryProject.name
output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${projectName}'
output principalId string = foundryAccount.identity.principalId
