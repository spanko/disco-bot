<#
.SYNOPSIS
    Tears down all Discovery Chatbot resources.
.PARAMETER ResourceGroup
    Resource group to delete
.PARAMETER Confirm
    Skip confirmation prompt
#>
param(
    [Parameter(Mandatory)][string]$ResourceGroup,
    [switch]$Confirm
)

if (-not $Confirm) {
    $response = Read-Host "This will DELETE all resources in '$ResourceGroup'. Type the resource group name to confirm"
    if ($response -ne $ResourceGroup) {
        Write-Host "Aborted." -ForegroundColor Yellow
        return
    }
}

Write-Host "Deleting resource group: $ResourceGroup..." -ForegroundColor Red
az group delete --name $ResourceGroup --yes --no-wait
Write-Host "Deletion initiated (runs in background)." -ForegroundColor Green
