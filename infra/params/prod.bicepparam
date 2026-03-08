using '../main.bicep'

param prefix = 'disc'
param suffix = 'prod'
param tags = {
  Environment: 'Production'
  Project: 'DiscoveryChatbot'
  ManagedBy: 'Bicep'
}
param primaryModelName = 'gpt-5.2'
param primaryModelVersion = '2025-01-01'
param primaryModelCapacity = 100
param fallbackModelName = 'gpt-4.1-mini'
param fallbackModelVersion = '2024-07-18'
param fallbackModelCapacity = 50
param deployerObjectId = '<YOUR-AAD-OBJECT-ID>'
param enablePublicAccess = false
param cosmosConsistency = 'Session'
