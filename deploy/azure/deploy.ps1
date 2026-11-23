# Deploys PyroLens to Azure with main.bicep. Run after `az login`.
#
#   ./deploy/azure/deploy.ps1 -Phase infra   # identity, Key Vault, PostgreSQL, Container Apps environment
#   (store the FIRMS key: see the command this script prints)
#   ./deploy/azure/deploy.ps1 -Phase apps    # API + map and the two scheduled jobs
#
# Re-running either phase is safe: Bicep deployments are declarative.
param(
    [ValidateSet('infra', 'apps')] [string] $Phase = 'infra',
    [string] $ResourceGroup = 'rg-pyrolens',
    [string] $Location = 'swedencentral',
    [string] $ImageTag = 'latest'
)

$ErrorActionPreference = 'Stop'
$template = Join-Path $PSScriptRoot 'main.bicep'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed" }
    return $output
}

if ($Phase -eq 'infra') {
    foreach ($ns in 'Microsoft.App', 'Microsoft.DBforPostgreSQL', 'Microsoft.OperationalInsights', 'Microsoft.KeyVault', 'Microsoft.ManagedIdentity') {
        Write-Host "Registering resource provider $ns"
        Invoke-Az provider register --namespace $ns --wait --output none
    }
    Invoke-Az group create --name $ResourceGroup --location $Location --output none
}

$me = Invoke-Az ad signed-in-user show --query id --output tsv

Write-Host "Deploying phase '$Phase' to $ResourceGroup ($Location)..."
$result = Invoke-Az deployment group create `
    --resource-group $ResourceGroup `
    --template-file $template `
    --parameters adminPrincipalId=$me deployApps=$($Phase -eq 'apps') `
        apiImage="ghcr.io/nikolhs123/pyrolens-api:$ImageTag" `
        workerImage="ghcr.io/nikolhs123/pyrolens-worker:$ImageTag" `
    --query properties.outputs --output json | ConvertFrom-Json

if ($Phase -eq 'infra') {
    Write-Host ""
    Write-Host "Infrastructure ready. Store your FIRMS MAP_KEY in Key Vault, then run -Phase apps:"
    Write-Host "  az keyvault secret set --vault-name $($result.keyVaultName.value) --name firms-map-key --value <YOUR_MAP_KEY>"
} else {
    Write-Host ""
    Write-Host "PyroLens is deployed: $($result.apiUrl.value)"
}
