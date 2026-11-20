<#
.SYNOPSIS
  Deploys the interactive demo website (evides-leakops/demo-site) to Azure Static Web Apps in the LeakOps resource group.

.DESCRIPTION
  1. Makes sure the resource group exists and deploys deploy/demosite.bicep (a Static Web App on the Free tier: 0 EUR).
  2. Reads the deployment token of the site (in memory only, never written to a file) and uploads demo-site/ with the SWA CLI.
  3. Checks that the site answers and serves the slides.

  The site is static: no backend, no keys, simulated data. It is safe to run again; the same content is uploaded again.
  deploy/teardown.ps1 removes it together with the rest of the resource group.

.EXAMPLE
  $env:AZURE_CONFIG_DIR = "$HOME\.azure-macaw"            # only when your Azure CLI default login is another tenant
  ./evides-leakops/deploy/deploy-demosite.ps1 -WhatIfOnly
  ./evides-leakops/deploy/deploy-demosite.ps1
  ./evides-leakops/deploy/deploy-demosite.ps1 -SkipInfra   # only upload new content to the existing site
#>
#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ResourceGroup = 'rg-evides-ai-demo',
    [string] $Location = 'westeurope',
    [switch] $WhatIfOnly,
    [switch] $SkipInfra
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot          # evides-leakops
$siteDir = Join-Path $root 'demo-site'
# Same suffix rule as deploy.ps1, so the site name matches the other resources of this environment.
$Suffix = (([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("$SubscriptionId/$ResourceGroup")) | ForEach-Object { $_.ToString('x2') }) -join '').Substring(0, 6)
$siteName = "leakops-demo-$Suffix"

$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source
function Az {
    $output = & $azExe @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed:`n$($output | Out-String)" }
    $output
}

Write-Host "== Evides LeakOps demo website" -ForegroundColor Cyan
. (Join-Path $PSScriptRoot 'tenant-guard.ps1'); Assert-LeakOpsTenant $SubscriptionId
$account = Az account show --subscription $SubscriptionId -o json | ConvertFrom-Json
Write-Host "   subscription: $($account.name)  user: $($account.user.name)"
Write-Host "   resource group: $ResourceGroup   site: $siteName ($Location, Free)"

if (-not (Test-Path (Join-Path $siteDir 'index.html'))) { throw "No index.html in $siteDir" }

if (-not $SkipInfra) {
    Write-Host "`n== 1. Static Web App" -ForegroundColor Cyan
    Az group create --subscription $SubscriptionId -n $ResourceGroup -l swedencentral --tags project=evides-leakops demo=evides-ai-foundry -o none | Out-Null
    $template = Join-Path $PSScriptRoot 'demosite.bicep'
    $whatIf = Az deployment group what-if --subscription $SubscriptionId -g $ResourceGroup --template-file $template --parameters suffix=$Suffix location=$Location --only-show-errors
    ($whatIf | Out-String) -split "`n" | Where-Object { $_ -match '^\s*[+~=*] Microsoft|Resource changes' } | ForEach-Object { Write-Host "   $($_.Trim())" }
    if ($WhatIfOnly) { Write-Host "`nWhat-if only: nothing was created."; return }
    $url = (Az deployment group create --subscription $SubscriptionId -g $ResourceGroup --template-file $template --parameters suffix=$Suffix location=$Location --only-show-errors --query properties.outputs.url.value -o tsv | Out-String).Trim()
    Write-Host "   ok   $siteName"
}
else {
    $host_ = (Az staticwebapp show --subscription $SubscriptionId -g $ResourceGroup -n $siteName --query defaultHostname -o tsv | Out-String).Trim()
    $url = "https://$host_"
}

Write-Host "`n== 2. Upload the site" -ForegroundColor Cyan
# The token goes into this process's environment only (the SWA CLI reads it from there), not onto the command line or into a file.
$env:SWA_CLI_DEPLOYMENT_TOKEN = (Az staticwebapp secrets list --subscription $SubscriptionId -g $ResourceGroup -n $siteName --query properties.apiKey -o tsv | Out-String).Trim()
try {
    & npx --yes '@azure/static-web-apps-cli@latest' deploy $siteDir --env production --no-use-keychain
    if ($LASTEXITCODE -ne 0) { throw 'The SWA CLI upload failed.' }
}
finally {
    Remove-Item Env:SWA_CLI_DEPLOYMENT_TOKEN -ErrorAction SilentlyContinue
}

Write-Host "`n== 3. Health" -ForegroundColor Cyan
$until = (Get-Date).AddSeconds(180); $ok = $false
while ((Get-Date) -lt $until -and -not $ok) {
    try {
        $page = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 20
        $js = Invoke-WebRequest "$url/js/captured.js" -UseBasicParsing -TimeoutSec 20
        $ok = $page.StatusCode -eq 200 -and $page.Content -match 'Evides LeakOps' -and $js.StatusCode -eq 200
    }
    catch { }
    if (-not $ok) { Start-Sleep -Seconds 6 }
}
if (-not $ok) { throw "The site at $url did not answer correctly within 3 minutes." }
Write-Host "   ok   $url"

Write-Host "`n== Done" -ForegroundColor Green
Write-Host "   Interactive demo   $url"
