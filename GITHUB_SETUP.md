# GitHub Actions OIDC Setup Instructions

## Overview
This document provides instructions for configuring GitHub repository secrets to enable automated deployment via GitHub Actions with OIDC authentication.

## Azure Resources Created

The following Azure resources have been created for OIDC authentication:

- **App Registration Name**: GitHub-DiscoveryAgent-Deploy
- **Application (Client) ID**: fd968326-106b-4ccb-9e69-7d91b8d475e9
- **Tenant ID**: 41231c11-d3b4-48d4-81c5-dacf4245a6c1
- **Subscription ID**: 951c802d-200d-4380-9006-2999df2218d9
- **Service Principal Object ID**: c85d5c94-a280-4055-af97-f2044c93f59c
- **Federated Credential**: GitHub-Main-Branch (for repo:spanko/disco-bot:ref:refs/heads/main)

## Required GitHub Secrets

You need to configure the following secrets in your GitHub repository:

### 1. Navigate to Repository Settings
Go to: https://github.com/spanko/disco-bot/settings/secrets/actions

### 2. Add New Repository Secrets

Click "New repository secret" and add each of the following:

| Secret Name | Value |
|-------------|-------|
| `AZURE_CLIENT_ID` | `fd968326-106b-4ccb-9e69-7d91b8d475e9` |
| `AZURE_TENANT_ID` | `41231c11-d3b4-48d4-81c5-dacf4245a6c1` |
| `AZURE_SUBSCRIPTION_ID` | `951c802d-200d-4380-9006-2999df2218d9` |

### Important Notes:
- **DO NOT** create an `AZURE_CLIENT_SECRET` - OIDC uses federated credentials instead
- These secrets allow GitHub Actions to authenticate to Azure using OIDC tokens
- The federated credential is already configured to trust the main branch of spanko/disco-bot

## What Happens Next

Once you configure these secrets and push code to the main branch (or any changes to src/, infra/, or the workflow file):

1. GitHub Actions will trigger the deployment workflow
2. The workflow will:
   - Build the .NET 9 Function App
   - Publish and create a deployment package
   - Authenticate to Azure using OIDC (no secrets stored!)
   - Upload the package to blob storage (discdevstor3xr5ve/deployments/package.zip)
   - Restart the Function App to load the new code
   - Verify the deployment by checking the health endpoint

## Infrastructure Changes Made

The following infrastructure updates have been made to support run-from-package deployment:

### 1. Storage Account ([infra/modules/storage.bicep](infra/modules/storage.bicep))
- Added `deployments` container for storing Function App packages

### 2. Function App ([infra/modules/function-app.bicep](infra/modules/function-app.bicep))
- Added `WEBSITE_RUN_FROM_PACKAGE` setting pointing to the blob storage package
- Added `WEBSITE_RUN_FROM_PACKAGE_BLOB_MI_RESOURCE_ID` for managed identity authentication

### 3. Main Infrastructure ([infra/main.bicep](infra/main.bicep))
- Updated function app module to pass storage account name for deployment package access

### 4. RBAC ([infra/modules/role-assignments.bicep](infra/modules/role-assignments.bicep))
- Function App already has Storage Blob Contributor role (includes read access to deployment packages)

## Workflow File

The deployment workflow is located at: [.github/workflows/deploy-function.yml](.github/workflows/deploy-function.yml)

It triggers on:
- Pushes to the main branch affecting src/, infra/, or the workflow file itself
- Manual workflow dispatch from the GitHub Actions UI

## Testing the Deployment

After configuring the secrets, you can test the deployment by:

1. Making a small change to the code (e.g., update a comment in [src/DiscoveryAgent/Functions/ConversationFunction.cs](src/DiscoveryAgent/Functions/ConversationFunction.cs))
2. Committing and pushing to the main branch
3. Monitoring the workflow at: https://github.com/spanko/disco-bot/actions
4. Checking the Function App health at: https://discdev-func-3xr5ve.azurewebsites.net/api/health

## Troubleshooting

### If deployment fails with authentication errors:
- Verify all three secrets are configured correctly in GitHub
- Check that the secret names match exactly (case-sensitive)
- Ensure the federated credential is configured for the correct repository and branch

### If the Function App doesn't start:
- Check the Application Insights logs
- Verify the package was uploaded to blob storage
- Check Function App logs via Azure Portal

### If you need to update infrastructure:
- Update the Bicep files in the infra/ directory
- The workflow will automatically deploy infrastructure changes when you push to main

## Security Benefits of OIDC

Using OIDC instead of traditional secrets provides:
- **No secret rotation needed** - tokens are short-lived (1 hour)
- **Instant revocation** - delete the federated credential to immediately block access
- **Audit trail** - all authentication events are logged in Azure AD
- **Reduced attack surface** - no long-lived credentials stored in GitHub
- **Compliance** - meets modern security standards for cloud deployments
