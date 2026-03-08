#!/usr/bin/env bash
# =============================================================================
# Discovery Chatbot - Deployment Script (Bash)
# Usage: ./deploy.sh --env dev --rg my-resource-group [--location eastus2] [--teams]
# =============================================================================

set -euo pipefail

# Parse arguments
ENV="" RG="" LOCATION="eastus2" TEAMS=false
while [[ $# -gt 0 ]]; do
  case $1 in
    --env)     ENV="$2";      shift 2 ;;
    --rg)      RG="$2";       shift 2 ;;
    --location) LOCATION="$2"; shift 2 ;;
    --teams)   TEAMS=true;     shift   ;;
    *) echo "Unknown arg: $1"; exit 1  ;;
  esac
done

[[ -z "$ENV" || -z "$RG" ]] && { echo "Usage: ./deploy.sh --env dev --rg <name> [--location eastus2] [--teams]"; exit 1; }

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
INFRA_DIR="$PROJECT_ROOT/infra"
PARAM_FILE="$INFRA_DIR/params/$ENV.bicepparam"

echo "========================================"
echo " Discovery Chatbot Deployment"
echo " Environment: $ENV"
echo " Resource Group: $RG"
echo " Location: $LOCATION"
echo "========================================"

# Check prerequisites
for cmd in az azd func; do
  command -v $cmd &>/dev/null || { echo "ERROR: $cmd is required"; exit 1; }
done

# Step 1: Provision infrastructure
echo -e "\n[1/4] Provisioning infrastructure..."
az group create --name "$RG" --location "$LOCATION" --output none

DEPLOYER_OID=$(az ad signed-in-user show --query id -o tsv)
echo "  Deployer OID: $DEPLOYER_OID"

TEMP_PARAM=$(mktemp)
sed "s/<YOUR-AAD-OBJECT-ID>/$DEPLOYER_OID/g" "$PARAM_FILE" > "$TEMP_PARAM"

az deployment group create \
  --resource-group "$RG" \
  --template-file "$INFRA_DIR/main.bicep" \
  --parameters "$TEMP_PARAM" \
  --output none

echo "  Infrastructure deployed ✓"

# Capture outputs
eval "$(az deployment group show --resource-group "$RG" --name deploy-foundry \
  --query 'properties.outputs' -o json 2>/dev/null | \
  jq -r 'to_entries[] | "export \(.key)=\(.value.value)"' 2>/dev/null || true)"

# Step 2: Deploy agent
echo -e "\n[2/4] Building and deploying agent..."
cd "$PROJECT_ROOT"
azd up --environment "$ENV" --no-prompt
echo "  Agent deployed ✓"

# Step 3: Deploy web chat
echo -e "\n[3/4] Deploying web chat..."
if [[ -n "${STORAGE_ACCOUNT_NAME:-}" ]]; then
  az storage blob service-properties update \
    --account-name "$STORAGE_ACCOUNT_NAME" \
    --static-website --index-document index.html \
    --auth-mode login --output none

  az storage blob upload-batch \
    --account-name "$STORAGE_ACCOUNT_NAME" \
    --source "$PROJECT_ROOT/src/WebChat" \
    --destination '$web' \
    --auth-mode login --output none

  WEB_URL=$(az storage account show --name "$STORAGE_ACCOUNT_NAME" \
    --query "primaryEndpoints.web" -o tsv)
  echo "  Web chat: $WEB_URL ✓"
fi

# Step 4: Teams (optional)
if $TEAMS; then
  echo -e "\n[4/4] Publishing to Teams..."
  bash "$PROJECT_ROOT/scripts/publish-teams.sh" --rg "$RG"
fi

echo -e "\n========================================"
echo " Deployment Complete! ✓"
echo "========================================"
echo " Foundry: ${FOUNDRY_ENDPOINT:-unknown}"
echo " Web:     ${WEB_URL:-not deployed}"
echo ""
