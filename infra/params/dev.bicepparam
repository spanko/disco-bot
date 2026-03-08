using '../main.bicep'

param prefix = 'disc'
param suffix = 'dev'
param tags = {
  Environment: 'Development'
  Project: 'DiscoveryChatbot'
  ManagedBy: 'Bicep'
}
param primaryModelName = 'gpt-5.2-chat'
param primaryModelVersion = '2025-12-11'
param primaryModelCapacity = 20
param fallbackModelName = 'gpt-4o-mini'
param fallbackModelVersion = '2024-07-18'
param fallbackModelCapacity = 10
param deployerObjectId = 'd9a4dcc0-50de-4c43-b46b-4d81233e3b1b'
param githubActionsPrincipalId = 'c85d5c94-a280-4055-af97-f2044c93f59c'
param enablePublicAccess = true
param cosmosConsistency = 'Session'
