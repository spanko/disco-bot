// ============================================================================
// Module: Azure Static Web App (Frontend for Discovery Agent)
// ============================================================================

param staticWebAppName string
param location string
param tags object
param functionAppName string

// ---------------------------------------------------------------------------
// Static Web App (Free tier)
// ---------------------------------------------------------------------------

resource staticWebApp 'Microsoft.Web/staticSites@2023-01-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    repositoryUrl: '' // Will be configured manually or via GitHub Actions
    branch: ''
    buildProperties: {
      skipGithubActionWorkflowGeneration: true
    }
    provider: 'None'
  }
}

// ---------------------------------------------------------------------------
// Link to Function App backend (for managed backend integration)
// ---------------------------------------------------------------------------

resource functionAppBackend 'Microsoft.Web/sites@2023-01-01' existing = {
  name: functionAppName
}

// Note: Static Web Apps can be linked to Function Apps for unified deployment
// This would be configured via the Azure Portal or deployment workflow

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output staticWebAppId string = staticWebApp.id
output staticWebAppName string = staticWebApp.name
output staticWebAppUrl string = staticWebApp.properties.defaultHostname
output deploymentToken string = staticWebApp.listSecrets().properties.apiKey
