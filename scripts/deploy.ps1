<#
.SYNOPSIS
    Deploys the Discovery Chatbot solution end-to-end.
.DESCRIPTION
    Provisions Azure infrastructure via Bicep, builds and deploys the hosted agent
    container, and optionally publishes to Microsoft Teams.
.PARAMETER Environment
    Target environment: dev, staging, or prod
.PARAMETER ResourceGroup
    Azure resource group name
.PARAMETER Location
    Azure region (default: eastus2)
.PARAMETER SkipInfra
    Skip infrastructure provisioning (deploy agent only)
.PARAMETER PublishTeams
    Publish to Teams after deployment
#>

param(
    [Parameter(Mandatory)][ValidateSet("dev","staging","prod")][string]$Environment,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [string]$Location = "eastus2",
    [switch]$SkipInfra,
    [switch]$PublishTeams
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot | Split-Path -Parent
$infraDir    = Join-Path $projectRoot "infra"
$paramFile   = Join-Path $infraDir "params/$Environment.bicepparam"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Discovery Chatbot Deployment"           -ForegroundColor Cyan
Write-Host " Environment: $Environment"              -ForegroundColor Cyan
Write-Host " Resource Group: $ResourceGroup"         -ForegroundColor Cyan
Write-Host " Location: $Location"                    -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# -----------------------------------------------------------------------
# Prerequisites check
# -----------------------------------------------------------------------
function Assert-Tool($name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "$name is required but not found. Please install it first."
    }
}

Assert-Tool "az"
Assert-Tool "azd"
Assert-Tool "func"

# Ensure logged in
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) { throw "Not logged in to Azure CLI. Run 'az login' first." }
Write-Host "Using subscription: $($account.name) ($($account.id))" -ForegroundColor Gray

# -----------------------------------------------------------------------
# Step 1: Provision Infrastructure
# -----------------------------------------------------------------------
if (-not $SkipInfra) {
    Write-Host "`n[1/4] Provisioning infrastructure..." -ForegroundColor Yellow

    # Create resource group
    az group create --name $ResourceGroup --location $Location --output none

    # Get deployer Object ID
    $deployerOid = (az ad signed-in-user show --query id -o tsv)
    Write-Host "  Deployer OID: $deployerOid" -ForegroundColor Gray

    # Update param file with real OID (sed-style replacement)
    $paramContent = Get-Content $paramFile -Raw
    $paramContent = $paramContent -replace '<YOUR-AAD-OBJECT-ID>', $deployerOid
    # Create temp param file in the infra directory to preserve relative paths
    $tempParam = Join-Path $infraDir "params\deploy-$Environment.bicepparam"
    Set-Content -Path $tempParam -Value $paramContent

    # Deploy Bicep
    $mainBicepPath = Join-Path $infraDir "main.bicep"
    Write-Host "  Deploying Bicep template..." -ForegroundColor Gray

    az deployment group create `
        --resource-group $ResourceGroup `
        --template-file $mainBicepPath `
        --parameters $tempParam `
        --output none

    # Clean up temp param file
    Remove-Item $tempParam -ErrorAction SilentlyContinue

    if ($LASTEXITCODE -ne 0) { throw "Bicep deployment failed" }

    # Retrieve deployment outputs separately
    $deployment = az deployment group show `
        --resource-group $ResourceGroup `
        --name main `
        --query "{properties: {outputs: properties.outputs}}" `
        --output json | ConvertFrom-Json

    # Capture outputs as environment variables
    $outputs = $deployment.properties.outputs
    $env:FOUNDRY_ENDPOINT           = $outputs.FOUNDRY_ENDPOINT.value
    $env:FOUNDRY_ACCOUNT_NAME       = $outputs.FOUNDRY_ACCOUNT_NAME.value
    $env:FOUNDRY_PROJECT_NAME       = $outputs.FOUNDRY_PROJECT_NAME.value
    $env:PRIMARY_MODEL_DEPLOYMENT   = $outputs.PRIMARY_MODEL_DEPLOYMENT.value
    $env:FALLBACK_MODEL_DEPLOYMENT  = $outputs.FALLBACK_MODEL_DEPLOYMENT.value
    $env:AI_SEARCH_ENDPOINT         = $outputs.AI_SEARCH_ENDPOINT.value
    $env:AI_SEARCH_NAME             = $outputs.AI_SEARCH_NAME.value
    $env:COSMOS_ENDPOINT            = $outputs.COSMOS_ENDPOINT.value
    $env:COSMOS_DATABASE            = $outputs.COSMOS_DATABASE.value
    $env:STORAGE_ACCOUNT_NAME       = $outputs.STORAGE_ACCOUNT_NAME.value
    $env:STORAGE_ENDPOINT           = $outputs.STORAGE_ENDPOINT.value
    $env:KEY_VAULT_URI              = $outputs.KEY_VAULT_URI.value
    $env:APP_INSIGHTS_CONNECTION    = $outputs.APP_INSIGHTS_CONNECTION.value

    Write-Host "  Infrastructure deployed successfully" -ForegroundColor Green
    Write-Host "  Foundry endpoint: $($env:FOUNDRY_ENDPOINT)" -ForegroundColor Gray
} else {
    Write-Host "`n[1/4] Skipping infrastructure (--SkipInfra)" -ForegroundColor DarkGray
}

# -----------------------------------------------------------------------
# Step 2: Build and Deploy Agent via azd
# -----------------------------------------------------------------------
Write-Host "`n[2/4] Building and deploying agent..." -ForegroundColor Yellow

Push-Location $projectRoot
try {
    # Initialize azd environment if needed
    $azdEnvExists = Test-Path ".azure\$Environment\.env"
    if (-not $azdEnvExists) {
        Write-Host "  Initializing azd environment..." -ForegroundColor Gray
        azd env new $Environment
    } else {
        azd env select $Environment
    }

    # Configure azd environment
    azd config set defaults.subscription $account.id
    azd config set defaults.location $Location
    azd env set AZURE_SUBSCRIPTION_ID $account.id
    azd env set AZURE_RESOURCE_GROUP $ResourceGroup
    azd env set AZURE_LOCATION $Location

    # Set infrastructure parameters from the already-deployed resources
    azd env set AZURE_ENV_NAME $Environment
    azd env set deployerObjectId $deployerOid
    azd env set prefix "disc"
    azd env set suffix $Environment

    # Use the existing infrastructure (skip infra provisioning)
    azd deploy --no-prompt
    if ($LASTEXITCODE -ne 0) { throw "azd deploy failed" }
    Write-Host "  Agent deployed successfully" -ForegroundColor Green
} finally {
    Pop-Location
}

# -----------------------------------------------------------------------
# Step 3: Deploy Web Chat (static files)
# -----------------------------------------------------------------------
Write-Host "`n[3/4] Deploying web chat interface..." -ForegroundColor Yellow

$storageAccount = $env:STORAGE_ACCOUNT_NAME
if ($storageAccount) {
    # Enable static website hosting on the storage account
    az storage blob service-properties update `
        --account-name $storageAccount `
        --static-website --index-document index.html `
        --auth-mode login --output none

    # Upload web chat files
    az storage blob upload-batch `
        --account-name $storageAccount `
        --source (Join-Path $projectRoot "src/WebChat") `
        --destination '$web' `
        --auth-mode login --output none

    $webUrl = az storage account show --name $storageAccount `
        --query "primaryEndpoints.web" -o tsv
    Write-Host "  Web chat available at: $webUrl" -ForegroundColor Green
} else {
    Write-Host "  Skipped (no storage account configured)" -ForegroundColor DarkGray
}

# -----------------------------------------------------------------------
# Step 4: Publish to Teams (optional)
# -----------------------------------------------------------------------
if ($PublishTeams) {
    Write-Host "`n[4/4] Publishing to Microsoft Teams..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "publish-teams.ps1") -ResourceGroup $ResourceGroup
} else {
    Write-Host "`n[4/4] Skipping Teams publish (use -PublishTeams to enable)" -ForegroundColor DarkGray
}

# -----------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Deployment Complete!"                      -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Foundry Endpoint:  $($env:FOUNDRY_ENDPOINT)"
Write-Host " Web Chat:          $webUrl"
Write-Host " Agent Name:        discovery-bot"
Write-Host " Primary Model:     $($env:PRIMARY_MODEL_DEPLOYMENT)"
Write-Host ""
Write-Host " Next steps:" -ForegroundColor Yellow
Write-Host "   1. Create a discovery context via POST /api/admin/context"
Write-Host "   2. Open the web chat URL (or add ?context=<id> for a specific context)"
Write-Host "   3. Use -PublishTeams to deploy to Teams"
Write-Host ""
