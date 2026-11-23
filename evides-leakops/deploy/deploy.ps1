<#
.SYNOPSIS
  Deploys the Evides LeakOps demo to its own resource group in Azure and checks that it works.

.DESCRIPTION
  1. Creates the resource group and deploys deploy/main.bicep (what-if first). Everything is new and separate from other demos:
     Microsoft Foundry (resource, project, gpt-5.4 deployment, tracing and the MCP connection), Cosmos DB (serverless), storage,
     Log Analytics, Application Insights, two Flex Consumption function apps (MCP tools, agents), one Basic App Service plan with
     two web apps (fake Waterstoring.nl, workflow host with the operator console) and the role assignments between them.
     Every resource is tagged project=evides-leakops.
  2. Seeds the simulated data into the new Cosmos DB (the key is read for this run only and never written to disk).
  3. Publishes the code of the four apps.
  4. Makes sure the two function keys exist, so Foundry and the agents can call the MCP tools and the workflow host can call the agents.
  5. Registers the six agents in the Foundry project (src/LeakOps.AgentRegistry), so they show up in https://ai.azure.com.
  6. Checks the health of all four apps.

  It is safe to run again: keys that already exist are kept, so nothing has to be re-entered. Nothing secret is written to a file;
  keys live only in the app settings of the apps (and in this script's memory while it runs).

  Cost: about 11.80 EUR a month for the web plan (B1), about 0 EUR for the two function apps (free monthly grant) and for Cosmos
  serverless at this scale, plus a few cents per workflow run for the model. Run deploy/scale-web.ps1 -Sku F1 to drop the web plan to
  free when you are not demoing. deploy/teardown.ps1 deletes everything.

.EXAMPLE
  $env:AZURE_CONFIG_DIR = "$HOME\.azure-macaw"     # only when your Azure CLI default login is another tenant, see the README
  ./evides-leakops/deploy/deploy.ps1 -WhatIfOnly   # show what would be created and stop (the empty resource group is created)
  ./evides-leakops/deploy/deploy.ps1               # deploy
  ./evides-leakops/deploy/deploy.ps1 -SkipPublish  # only the infrastructure and the data
  ./evides-leakops/deploy/deploy.ps1 -WebLocation northeurope   # when the Basic plan has no capacity in the default region
#>
#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ResourceGroup = 'rg-evides-ai-demo',
    [string] $Location = 'swedencentral',
    [ValidateSet('B1', 'F1')] [string] $WebSku = 'B1',
    [string] $WebLocation = 'westeurope',
    [switch] $WhatIfOnly,
    [switch] $SkipInfra,
    [switch] $SkipPublish,
    [switch] $SkipSeed,
    [switch] $RunE2E
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot          # evides-leakops
# Names that must be unique worldwide get this suffix: 6 hex characters derived from the subscription and resource group, so it is
# the same on every run and different for every environment.
$Suffix = (([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("$SubscriptionId/$ResourceGroup")) | ForEach-Object { $_.ToString('x2') }) -join '').Substring(0, 6)
$names = @{
    Mcp          = "leakops-mcp-$Suffix"
    Agents       = "leakops-agents-$Suffix"
    Waterstoring = "leakops-waterstoring-$Suffix"
    Workflow     = "leakops-workflow-$Suffix"
}

# The Azure CLI does not throw when a command fails; this does. (The function is called Az, so the real executable is called by path:
# PowerShell command names are not case sensitive and plain "az" would call this function again.)
$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source
function Az {
    $output = & $azExe @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed:`n$($output | Out-String)" }
    $output
}

# Quiet read: an empty string when the app or the setting does not exist yet.
function Get-AppSetting([string] $kind, [string] $app, [string] $setting) {
    $value = & $azExe $kind config appsettings list --subscription $SubscriptionId -g $ResourceGroup -n $app --query "[?name=='$setting'].value | [0]" -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $value) { return '' }
    return ($value | Out-String).Trim()
}

function New-Secret { [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_') }

# A new app can answer 503 for a minute or two while its host starts; a deployment is simply tried again.
function Invoke-WithRetry([string] $what, [scriptblock] $action, [int] $attempts = 4, [int] $delaySeconds = 45) {
    for ($attempt = 1; ; $attempt++) {
        try { & $action; return }
        catch {
            if ($attempt -ge $attempts) { throw }
            Write-Host "   ...  $what failed (attempt $attempt of $attempts), trying again in $delaySeconds s: $(($_.Exception.Message -split "`n")[0])"
            Start-Sleep -Seconds $delaySeconds
        }
    }
}

function Wait-Until([string] $what, [scriptblock] $test, [int] $seconds = 240) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        try { if (& $test) { Write-Host "   ok   $what"; return } } catch { }
        Start-Sleep -Seconds 6
    }
    throw "Timed out waiting for: $what"
}

Write-Host "== Evides LeakOps deployment" -ForegroundColor Cyan
. (Join-Path $PSScriptRoot 'tenant-guard.ps1'); Assert-LeakOpsTenant $SubscriptionId
$account = Az account show --subscription $SubscriptionId -o json | ConvertFrom-Json
Write-Host "   subscription: $($account.name)  tenant: $($account.tenantId)  user: $($account.user.name)"
Write-Host "   resource group: $ResourceGroup ($Location)   suffix: $Suffix   web plan: $WebSku ($WebLocation)"

Az group create --subscription $SubscriptionId -n $ResourceGroup -l $Location --tags project=evides-leakops demo=evides-ai-foundry -o none | Out-Null

# The person running this script gets the Foundry User role, so the agents can be created and tried in the portal afterwards.
$deployerId = (& $azExe ad signed-in-user show --query id -o tsv 2>$null | Out-String).Trim()
if (-not $deployerId) { Write-Warning 'Could not read your user id: you will have to give yourself the "Foundry User" role on the Foundry resource.' }

# Keys that already exist are kept (they were read from the app settings), so running this again changes nothing.
$waterstoringApiKey = Get-AppSetting webapp $names.Waterstoring 'WATERSTORING_API_KEY'
$operatorKey = Get-AppSetting webapp $names.Workflow 'OPERATOR_KEY'
$mcpKey = Get-AppSetting functionapp $names.Agents 'MCP_AUTH_KEY'
$agentsKey = Get-AppSetting webapp $names.Workflow 'AGENTS_FUNCTION_KEY'
$newOperatorKey = -not $operatorKey
if (-not $waterstoringApiKey) { $waterstoringApiKey = New-Secret }
if (-not $operatorKey) { $operatorKey = New-Secret }
if (-not $mcpKey) { $mcpKey = New-Secret }
if (-not $agentsKey) { $agentsKey = New-Secret }

$template = Join-Path $PSScriptRoot 'main.bicep'
$parameters = Join-Path ([IO.Path]::GetTempPath()) "leakops-params-$([guid]::NewGuid().ToString('N')).json"
@{
    '$schema'      = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
    contentVersion = '1.0.0.0'
    parameters     = @{
        suffix              = @{ value = $Suffix }
        location            = @{ value = $Location }
        webSku              = @{ value = $WebSku }
        webLocation         = @{ value = $WebLocation }
        deployerPrincipalId = @{ value = $deployerId }
        waterstoringApiKey  = @{ value = $waterstoringApiKey }
        operatorKey         = @{ value = $operatorKey }
        mcpKey              = @{ value = $mcpKey }
        agentsKey           = @{ value = $agentsKey }
    }
} | ConvertTo-Json -Depth 5 | Set-Content -Path $parameters -Encoding utf8

try {
    Write-Host "`n== 1. Infrastructure" -ForegroundColor Cyan
    if ($SkipInfra) {
        # Code-only redeploy: the infrastructure is not touched. Names and URLs follow from the suffix (see main.bicep), so nothing is looked up.
        $SkipSeed = $true
        $outputs = [pscustomobject]@{
            mcpUrl                 = [pscustomobject]@{ value = "https://$($names.Mcp).azurewebsites.net" }
            agentsUrl              = [pscustomobject]@{ value = "https://$($names.Agents).azurewebsites.net" }
            waterstoringUrl        = [pscustomobject]@{ value = "https://$($names.Waterstoring).azurewebsites.net" }
            workflowUrl            = [pscustomobject]@{ value = "https://$($names.Workflow).azurewebsites.net" }
            foundryProjectEndpoint = [pscustomobject]@{ value = "https://evides-foundry-$Suffix.services.ai.azure.com/api/projects/evides-leakops" }
            modelDeployment        = [pscustomobject]@{ value = 'gpt-5.4' }
        }
        Write-Host "   skipped (code-only redeploy; the data is not seeded again either)"
    }
    else {
        $whatIf = Az deployment group what-if --subscription $SubscriptionId -g $ResourceGroup --template-file $template --parameters "@$parameters" --only-show-errors
        ($whatIf | Out-String) -split "`n" | Where-Object { $_ -match '^\s*\+ Microsoft|Resource changes' } | ForEach-Object { Write-Host "   $($_.Trim())" }
        if ($WhatIfOnly) { Write-Host "`nWhat-if only: no resources were created (only the empty resource group)."; return }

        $outputs = (Az deployment group create --subscription $SubscriptionId -g $ResourceGroup --template-file $template --parameters "@$parameters" --only-show-errors --query properties.outputs -o json | Out-String | ConvertFrom-Json)
        Write-Host "   ok   resources deployed"
    }
}
finally {
    Remove-Item $parameters -ErrorAction SilentlyContinue   # the parameters file held the keys
}

$url = @{
    Mcp          = $outputs.mcpUrl.value
    Agents       = $outputs.agentsUrl.value
    Waterstoring = $outputs.waterstoringUrl.value
    Workflow     = $outputs.workflowUrl.value
}

if (-not $SkipSeed) {
    Write-Host "`n== 2. Simulated data" -ForegroundColor Cyan
    # The seeder creates the database and containers and loads the simulated network, sensors, stock and scenarios.
    # The Cosmos key is read for this run only: it goes into this process's environment, not into a file.
    $cosmosKey = (Az cosmosdb keys list --subscription $SubscriptionId -g $ResourceGroup -n $outputs.cosmosAccount.value --query primaryMasterKey -o tsv | Out-String).Trim()
    $env:COSMOS_ENDPOINT = $outputs.cosmosEndpoint.value
    $env:COSMOS_KEY = $cosmosKey
    try {
        & dotnet run --project (Join-Path $root 'src/LeakOps.Seeder') -c Release -- --reset-runtime
        if ($LASTEXITCODE -ne 0) { throw 'Seeding failed.' }
    }
    finally {
        Remove-Item Env:COSMOS_KEY -ErrorAction SilentlyContinue
        Remove-Item Env:COSMOS_ENDPOINT -ErrorAction SilentlyContinue
    }
}

if (-not $SkipPublish) {
    Write-Host "`n== 3. Publish the code" -ForegroundColor Cyan
    $work = Join-Path ([IO.Path]::GetTempPath()) "leakops-publish-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        foreach ($app in 'Mcp', 'Agents', 'Waterstoring', 'Workflow') {
            $project = Join-Path $root "src/LeakOps.$app/LeakOps.$app.csproj"
            $out = Join-Path $work $app
            & dotnet publish $project -c Release -o $out --nologo -v quiet | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for LeakOps.$app" }
            $zip = Join-Path $work "$app.zip"
            # CreateFromDirectory with includeBaseDirectory=$false puts the files at the root of the zip, with forward slashes.
            [IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
            Write-Host "   built $app ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
        }

        foreach ($app in 'Mcp', 'Agents') {
            Invoke-WithRetry "deploy $($names[$app])" { Az functionapp deployment source config-zip --subscription $SubscriptionId -g $ResourceGroup -n $names[$app] --src (Join-Path $work "$app.zip") -o none | Out-Null }
            Write-Host "   ok   deployed $($names[$app])"
        }
        foreach ($app in 'Waterstoring', 'Workflow') {
            Invoke-WithRetry "deploy $($names[$app])" { Az webapp deploy --subscription $SubscriptionId -g $ResourceGroup -n $names[$app] --src-path (Join-Path $work "$app.zip") --type zip --clean true -o none | Out-Null }
            Write-Host "   ok   deployed $($names[$app])"
        }
    }
    finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "`n== 4. Function keys" -ForegroundColor Cyan
    # The agents send the mcp_extension system key to the MCP app; the workflow host sends a function key to the agents app.
    # Both values were passed to Bicep (they are in the app settings); here the function hosts are told to accept the same values.
    Wait-Until 'MCP function host is up' { (Invoke-WebRequest "$($url.Mcp)/runtime/webhooks/mcp" -SkipHttpErrorCheck -UseBasicParsing -TimeoutSec 20).StatusCode -in 200, 401, 405 }
    Wait-Until 'agents function host is up' { (Invoke-WebRequest "$($url.Agents)/api/health" -SkipHttpErrorCheck -UseBasicParsing -TimeoutSec 20).StatusCode -in 200, 401 }
    Wait-Until 'mcp_extension key set' { Az functionapp keys set --subscription $SubscriptionId -g $ResourceGroup -n $names.Mcp --key-type systemKeys --key-name mcp_extension --key-value $mcpKey -o none; $true }
    Wait-Until 'agents function key set' { Az functionapp keys set --subscription $SubscriptionId -g $ResourceGroup -n $names.Agents --key-type functionKeys --key-name leakops-workflow --key-value $agentsKey -o none; $true }
}

Write-Host "`n== 5. Foundry agents" -ForegroundColor Cyan
# Creates the six agents in the Foundry project (they then show up in the portal). A new version is only made when the definition
# changed. The sign-in used is the deployer's Azure CLI account, which the Bicep gave the Foundry User role.
$env:AZURE_AI_PROJECT_ENDPOINT = $outputs.foundryProjectEndpoint.value
$env:MCP_SERVER_URL = "$($url.Mcp)/runtime/webhooks/mcp"
$env:MODEL_DEPLOYMENT_NAME = $outputs.modelDeployment.value
$env:AZURE_TENANT_ID = (Az account show --subscription $SubscriptionId --query tenantId -o tsv | Out-String).Trim()
# When the knowledge layer exists (deploy/deploy-knowledge.ps1) the agents are registered with their knowledge tool too.
$searchService = "evides-search-$Suffix"
if ((& $azExe search service show --subscription $SubscriptionId -g $ResourceGroup -n $searchService --query name -o tsv 2>$null | Out-String).Trim() -eq $searchService) {
    $env:KNOWLEDGE_SEARCH_ENDPOINT = "https://$searchService.search.windows.net"
    Write-Host "   knowledge layer found: agents are registered with their knowledge base"
}
Invoke-WithRetry 'register the Foundry agents' {
    & dotnet run --project (Join-Path $root 'src/LeakOps.AgentRegistry') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Registering the agents failed.' }
}

Write-Host "`n== 6. Health" -ForegroundColor Cyan
Wait-Until 'waterstoring' { (Invoke-WebRequest "$($url.Waterstoring)/health" -UseBasicParsing -TimeoutSec 20).StatusCode -eq 200 }
Wait-Until 'workflow host' { (Invoke-WebRequest "$($url.Workflow)/health" -UseBasicParsing -TimeoutSec 20).StatusCode -eq 200 }
Wait-Until 'agents (with the function key)' { (Invoke-WebRequest "$($url.Agents)/api/health" -Headers @{ 'x-functions-key' = $agentsKey } -UseBasicParsing -TimeoutSec 30).StatusCode -eq 200 }
Wait-Until 'agents refuse calls without the key' { (Invoke-WebRequest "$($url.Agents)/api/agents/triage" -Method Post -Body '{}' -ContentType 'application/json' -SkipHttpErrorCheck -UseBasicParsing -TimeoutSec 30).StatusCode -eq 401 }
Wait-Until 'workflow API refuses calls without the operator key' { (Invoke-WebRequest "$($url.Workflow)/api/scenarios" -SkipHttpErrorCheck -UseBasicParsing -TimeoutSec 30).StatusCode -eq 401 }
Wait-Until 'workflow API accepts the operator key' { (Invoke-WebRequest "$($url.Workflow)/api/scenarios" -Headers @{ 'X-Operator-Key' = $operatorKey } -UseBasicParsing -TimeoutSec 30).StatusCode -eq 200 }

Write-Host "`n== Done" -ForegroundColor Green
Write-Host "   Operator console   $($url.Workflow)"
Write-Host "   What residents see $($url.Waterstoring)/evides"
Write-Host "   Foundry project    $($outputs.foundryProjectEndpoint.value)   (open it in https://ai.azure.com, project 'evides-leakops')"
if ($newOperatorKey) {
    Write-Host "   Operator key       $operatorKey   (shown once; keep it safe. Show it again with: az webapp config appsettings list -g $ResourceGroup -n $($names.Workflow) --query ""[?name=='OPERATOR_KEY'].value"" -o tsv)"
}
else {
    Write-Host "   Operator key       unchanged (az webapp config appsettings list -g $ResourceGroup -n $($names.Workflow) --query ""[?name=='OPERATOR_KEY'].value"" -o tsv)"
}

if ($RunE2E) {
    Write-Host "`n== End-to-end check against Azure" -ForegroundColor Cyan
    $env:WORKFLOW_URL = $url.Workflow; $env:WATERSTORING_URL = $url.Waterstoring; $env:AGENTS_URL = $url.Agents
    $env:WATERSTORING_API_KEY = $waterstoringApiKey; $env:OPERATOR_KEY = $operatorKey; $env:AGENTS_FUNCTION_KEY = $agentsKey
    dotnet run (Join-Path $root 'scripts/e2e.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The end-to-end check failed.' }
}
