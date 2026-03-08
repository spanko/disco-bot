<#
.SYNOPSIS
    Publishes the Discovery Bot agent to Microsoft Teams via Foundry portal flow.
.PARAMETER ResourceGroup
    Azure resource group containing the bot resources
#>

param(
    [Parameter(Mandatory)][string]$ResourceGroup
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing agent to Microsoft Teams..." -ForegroundColor Yellow

# Get the Foundry account and project names from resource group
$foundryAccount = az resource list --resource-group $ResourceGroup `
    --resource-type "Microsoft.CognitiveServices/accounts" `
    --query "[?kind=='AIServices'].name" -o tsv | Select-Object -First 1

$foundryProject = az resource list --resource-group $ResourceGroup `
    --resource-type "Microsoft.CognitiveServices/accounts/projects" `
    --query "[0].name" -o tsv

$botName = az resource list --resource-group $ResourceGroup `
    --resource-type "Microsoft.BotService/botServices" `
    --query "[0].name" -o tsv

if (-not $foundryAccount -or -not $botName) {
    throw "Could not find Foundry account or Bot Service in resource group $ResourceGroup"
}

Write-Host "  Foundry Account: $foundryAccount"
Write-Host "  Bot Service: $botName"

# Verify Teams channel is enabled
$channels = az bot show --name $botName --resource-group $ResourceGroup `
    --query "properties.enabledChannels" -o json | ConvertFrom-Json

if ($channels -notcontains "MsTeamsChannel") {
    Write-Host "  Enabling Teams channel..." -ForegroundColor Yellow
    az bot msteams create --name $botName --resource-group $ResourceGroup --output none
}

Write-Host "  Teams channel is enabled" -ForegroundColor Green

# Output the Teams deep link for sideloading
Write-Host ""
Write-Host "  To complete Teams deployment:" -ForegroundColor Yellow
Write-Host "    1. Open https://ai.azure.com and navigate to your project"
Write-Host "    2. Select the 'discovery-bot' agent"
Write-Host "    3. Click 'Publish' > 'Publish to Teams and Microsoft 365 Copilot'"
Write-Host "    4. Follow the guided flow to create the Teams app package"
Write-Host ""
Write-Host "  Alternatively, use the Foundry SDK to publish programmatically:"
Write-Host "    az rest --method POST --url ""https://$foundryAccount.services.ai.azure.com/api/projects/$foundryProject/applications"" ..."
Write-Host ""
