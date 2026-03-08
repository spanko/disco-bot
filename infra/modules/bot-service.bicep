// ============================================================================
// Module: Azure Bot Service (Teams channel bridge)
// ============================================================================

param botName string
param location string
param tags object
param foundryEndpoint string
param appInsightsKey string
param tenantId string = tenant().tenantId

resource bot 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botName
  location: location
  tags: tags
  sku: {
    name: 'S1'
  }
  kind: 'azurebot'
  properties: {
    displayName: 'Discovery Bot'
    description: 'Conversational discovery and questionnaire bot'
    endpoint: '${foundryEndpoint}/api/messages'
    msaAppType: 'SingleTenant'
    msaAppId: guid(botName)
    msaAppTenantId: tenantId
    developerAppInsightKey: appInsightsKey
    publicNetworkAccess: 'Enabled'
  }
}

// Enable Teams channel
resource teamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: bot
  name: 'MsTeamsChannel'
  location: location
  properties: {
    channelName: 'MsTeamsChannel'
    properties: {
      isEnabled: true
      enableCalling: false
    }
  }
}

// Enable Web Chat channel
resource webChatChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: bot
  name: 'WebChatChannel'
  location: location
  properties: {
    channelName: 'WebChatChannel'
    properties: {
      sites: [
        {
          siteName: 'Default'
          isEnabled: true
          isWebChatSpeechEnabled: false
          isWebchatPreviewEnabled: true
        }
      ]
    }
  }
}

output botName string = bot.name
output botId string = bot.id
