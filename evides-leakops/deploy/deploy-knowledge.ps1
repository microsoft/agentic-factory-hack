<#
.SYNOPSIS
  Adds (or removes) the knowledge layer of the Evides LeakOps demo: Foundry IQ knowledge bases on Azure AI Search.

.DESCRIPTION
  1. Deploys deploy/knowledge.bicep: an Azure AI Search service (Basic, Entra ID only), an embedding model on the Foundry resource, a blob
     container for the documents, the access rights between them and the two project connections the agents use (no keys anywhere).
  2. Uploads the documents of evides-leakops/knowledge (ops/ and comms/) to the blob container. All of them are simulated.
  3. Creates two knowledge sources (one per folder) and two knowledge bases with the search REST API (Entra token of the signed-in user):
       leakops-ops-kb    the operational documents, for Triage, Diagnostic and Procurement
       leakops-comms-kb  the public communication guidelines only, for Communications
     gpt-5.4 plans the queries (agentic retrieval); the documents are indexed with the embedding model.
  4. Waits until the documents are indexed and runs a test question against both knowledge bases.
  5. Registers the agents again with the knowledge tool (src/LeakOps.AgentRegistry, environment variable KNOWLEDGE_SEARCH_ENDPOINT).

  Cost: Azure AI Search Basic is about 70 EUR a month (about 0.10 EUR an hour) and cannot be paused, so use -Remove when you are not demoing
  (the documents stay in git, the whole layer is rebuilt in a few minutes). The embedding and query-planning tokens cost cents.
  -Remove deletes the search service, the embedding deployment, the project connections and the blob container, and registers the agents
  again without knowledge, so the demo keeps working with the data tools only.

.EXAMPLE
  $env:AZURE_CONFIG_DIR = "$HOME\.azure-macaw"        # only when your Azure CLI default login is another tenant
  ./evides-leakops/deploy/deploy-knowledge.ps1 -WhatIfOnly
  ./evides-leakops/deploy/deploy-knowledge.ps1
  ./evides-leakops/deploy/deploy-knowledge.ps1 -SearchLocation francecentral   # when the search service cannot be created in Sweden Central
  ./evides-leakops/deploy/deploy-knowledge.ps1 -Remove
#>
#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ResourceGroup = 'rg-evides-ai-demo',
    [string] $SearchLocation = 'swedencentral',
    # Preview API: pinned after testing. The same version is used for the knowledge base MCP endpoint in the project connections.
    [string] $ApiVersion = '2026-08-01-preview',
    [switch] $WhatIfOnly,
    [switch] $SkipInfra,
    [switch] $SkipRegister,
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot          # evides-leakops
$Suffix = (([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("$SubscriptionId/$ResourceGroup")) | ForEach-Object { $_.ToString('x2') }) -join '').Substring(0, 6)
$searchName = "evides-search-$Suffix"
$foundryName = "evides-foundry-$Suffix"
$storageName = "evidessa$Suffix"
$projectName = 'evides-leakops'
$searchEndpoint = "https://$searchName.search.windows.net"
$foundryEndpoint = "https://$foundryName.cognitiveservices.azure.com"
$container = 'knowledge'
$embedding = 'text-embedding-3-small'
$model = 'gpt-5.4'

$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source
function Az {
    $output = & $azExe @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed:`n$($output | Out-String)" }
    $output
}

function Invoke-WithRetry([string] $what, [scriptblock] $action, [int] $attempts = 6, [int] $delaySeconds = 30) {
    for ($attempt = 1; ; $attempt++) {
        try { return (& $action) }
        catch {
            if ($attempt -ge $attempts) { throw }
            Write-Host "   ...  $what failed (attempt $attempt of $attempts), trying again in $delaySeconds s: $(($_.Exception.Message -split "`n")[0])"
            Start-Sleep -Seconds $delaySeconds
        }
    }
}

# The agents are registered with the project endpoint, the MCP URL and (when the knowledge layer exists) the search endpoint.
function Register-Agents([bool] $withKnowledge) {
    $projectEndpoint = "https://$foundryName.services.ai.azure.com/api/projects/$projectName"
    $mcpApp = "leakops-mcp-$Suffix"
    $env:AZURE_AI_PROJECT_ENDPOINT = $projectEndpoint
    $env:MCP_SERVER_URL = "https://$mcpApp.azurewebsites.net/runtime/webhooks/mcp"
    $env:MODEL_DEPLOYMENT_NAME = $model
    $env:AZURE_TENANT_ID = (Az account show --subscription $SubscriptionId --query tenantId -o tsv | Out-String).Trim()
    if ($withKnowledge) { $env:KNOWLEDGE_SEARCH_ENDPOINT = $searchEndpoint; $env:KNOWLEDGE_API_VERSION = $ApiVersion } else { Remove-Item Env:KNOWLEDGE_SEARCH_ENDPOINT -ErrorAction SilentlyContinue }
    $extra = if ($withKnowledge) { @() } else { @('--no-knowledge') }
    Invoke-WithRetry 'register the Foundry agents' {
        & dotnet run --project (Join-Path $root 'src/LeakOps.AgentRegistry') -c Release -- @extra | ForEach-Object { Write-Host "   $_" }
        if ($LASTEXITCODE -ne 0) { throw 'Registering the agents failed.' }
    } 3 20
}

Write-Host "== Evides LeakOps knowledge layer (Foundry IQ)" -ForegroundColor Cyan
$account = Az account show --subscription $SubscriptionId -o json | ConvertFrom-Json
Write-Host "   subscription: $($account.name)  user: $($account.user.name)"
Write-Host "   resource group: $ResourceGroup   search service: $searchName ($SearchLocation, Basic)"

# ---------------------------------------------------------------------------------------------------------------------
if ($Remove) {
    Write-Host "`n== Remove" -ForegroundColor Cyan
    # The agents must stop pointing at the knowledge bases first, otherwise a run would fail on a connection that is gone.
    if (-not $SkipRegister) { Register-Agents $false; Write-Host '   ok   agents registered without knowledge' }
    $projectId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.CognitiveServices/accounts/$foundryName/projects/$projectName"
    foreach ($connection in 'leakops-kb-ops', 'leakops-kb-comms', 'leakops-kb') {
        & $azExe rest --method delete --url "https://management.azure.com$projectId/connections/${connection}?api-version=2025-10-01-preview" 2>$null | Out-Null
    }
    & $azExe search service delete --subscription $SubscriptionId -g $ResourceGroup -n $searchName --yes 2>$null | Out-Null
    & $azExe cognitiveservices account deployment delete --subscription $SubscriptionId -g $ResourceGroup -n $foundryName --deployment-name $embedding 2>$null | Out-Null
    & $azExe storage container delete --account-name $storageName -n $container --auth-mode login 2>$null | Out-Null
    Write-Host "   ok   search service, embedding deployment, connections and container removed (the documents stay in git)"
    return
}

# ---------------------------------------------------------------------------------------------------------------------
$deployerId = (& $azExe ad signed-in-user show --query id -o tsv 2>$null | Out-String).Trim()
if (-not $deployerId) { Write-Warning 'Could not read your user id: you will have to give yourself the Search and Storage roles that knowledge.bicep assigns.' }

if (-not $SkipInfra) {
    Write-Host "`n== 1. Infrastructure" -ForegroundColor Cyan
    $template = Join-Path $PSScriptRoot 'knowledge.bicep'
    $parameters = @("suffix=$Suffix", "searchLocation=$SearchLocation", "knowledgeApiVersion=$ApiVersion", "deployerPrincipalId=$deployerId")
    $whatIf = Az deployment group what-if --subscription $SubscriptionId -g $ResourceGroup --template-file $template --parameters @parameters --only-show-errors
    ($whatIf | Out-String) -split "`n" | Where-Object { $_ -match '^\s*\+ Microsoft|Resource changes' } | ForEach-Object { Write-Host "   $($_.Trim())" }
    if ($WhatIfOnly) { Write-Host "`nWhat-if only: nothing was created."; return }
    Az deployment group create --subscription $SubscriptionId -g $ResourceGroup -n leakops-knowledge --template-file $template --parameters @parameters --only-show-errors -o none | Out-Null
    Write-Host "   ok   search service, embedding model, container, access rights and project connections"
}

Write-Host "`n== 2. Documents" -ForegroundColor Cyan
# New role assignments need a minute or two before they work.
foreach ($folder in 'ops', 'comms') {
    $source = Join-Path $root "knowledge/$folder"
    Invoke-WithRetry "upload $folder" {
        Az storage blob upload-batch --account-name $storageName -d $container -s $source --destination-path $folder --auth-mode login --overwrite --only-show-errors -o none | Out-Null
    } | Out-Null
    Write-Host "   ok   $folder ($(@(Get-ChildItem $source -File).Count) documents)"
}

Write-Host "`n== 3. Knowledge sources and knowledge bases" -ForegroundColor Cyan
$searchToken = (Az account get-access-token --resource https://search.azure.com --query accessToken -o tsv | Out-String).Trim()
$headers = @{ Authorization = "Bearer $searchToken" }
function Put-Search([string] $path, $body) {
    Invoke-WithRetry "create $path" {
        Invoke-RestMethod -Method Put -Uri "$searchEndpoint/${path}?api-version=$ApiVersion" -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 12) | Out-Null
    } | Out-Null
    Write-Host "   ok   $path"
}
$storageId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Storage/storageAccounts/$storageName"
function New-BlobSource([string] $name, [string] $folder, [string] $description) {
    Put-Search "knowledgesources/$name" @{
        name = $name; kind = 'azureBlob'; description = $description
        azureBlobParameters = @{
            connectionString = "ResourceId=$storageId"; containerName = $container; folderPath = $folder; isADLSGen2 = $false
            ingestionParameters = @{
                identity = $null; contentExtractionMode = 'minimal'
                embeddingModel = @{ kind = 'azureOpenAI'; azureOpenAIParameters = @{ resourceUri = $foundryEndpoint; deploymentId = $embedding; modelName = $embedding } }
            }
        }
    }
}
New-BlobSource 'leakops-ops-docs' 'ops' 'Operational documents: isolation SOP, repair method, flushing procedure, procurement policy, false-alarm playbook and past incident reviews (simulated).'
New-BlobSource 'leakops-comms-docs' 'comms' 'Public communication guidelines for resident notices (simulated).'

# gpt-5.4 plans the queries (agentic retrieval). The agent gets the passages with references and writes the answer itself (extractiveData).
$planner = @(@{ kind = 'azureOpenAI'; azureOpenAIParameters = @{ resourceUri = $foundryEndpoint; deploymentId = $model; modelName = $model } })
Put-Search 'knowledgebases/leakops-ops-kb' @{
    name = 'leakops-ops-kb'; knowledgeSources = @(@{ name = 'leakops-ops-docs' }); models = $planner; outputMode = 'extractiveData'; retrievalReasoningEffort = @{ kind = 'low' }
    description = 'Operational knowledge for the Evides LeakOps agents: SOPs, repair method, procurement policy, false-alarm playbook, past incident reviews. All simulated.'
    retrievalInstructions = 'Use the leakops-ops-docs knowledge source for every question. The documents are internal guidance and past incident reviews; the data tools stay the source for live numbers.'
}
Put-Search 'knowledgebases/leakops-comms-kb' @{
    name = 'leakops-comms-kb'; knowledgeSources = @(@{ name = 'leakops-comms-docs' }); models = $planner; outputMode = 'extractiveData'; retrievalReasoningEffort = @{ kind = 'low' }
    description = 'Public communication guidelines for resident notices. Simulated.'
    retrievalInstructions = 'Use the leakops-comms-docs knowledge source for every question.'
}

Write-Host "`n== 4. Indexing and test questions" -ForegroundColor Cyan
foreach ($source in 'leakops-ops-docs', 'leakops-comms-docs') {
    $until = (Get-Date).AddMinutes(10); $done = $false
    while ((Get-Date) -lt $until -and -not $done) {
        try {
            $status = Invoke-RestMethod -Uri "$searchEndpoint/knowledgesources/$source/status?api-version=$ApiVersion" -Headers $headers
            $last = $status.lastSynchronizationState
            if ($null -eq $status.currentSynchronizationState -and $null -ne $last) {
                if ($last.itemsUpdatesFailed -gt 0) { throw "${source}: $($last.itemsUpdatesFailed) documents failed to index: $($last.errors | ConvertTo-Json -Compress)" }
                $done = $true; Write-Host "   ok   $source indexed ($($last.itemsUpdatesProcessed) documents)"
            }
        } catch { if ($_.Exception.Message -like '*failed to index*') { throw } }
        if (-not $done) { Start-Sleep -Seconds 6 }
    }
    if (-not $done) { throw "Timed out waiting for $source to be indexed." }
}
function Test-Retrieve([string] $knowledgeBase, [string] $question, [string] $expectedDocument) {
    $body = @{ messages = @(@{ role = 'user'; content = @(@{ type = 'text'; text = $question }) }) } | ConvertTo-Json -Depth 8
    $result = Invoke-WithRetry "ask $knowledgeBase" {
        Invoke-RestMethod -Method Post -Uri "$searchEndpoint/knowledgebases/$knowledgeBase/retrieve?api-version=$ApiVersion" -Headers $headers -ContentType 'application/json' -Body $body
    } 3 15
    $documents = @($result.references | ForEach-Object { ($_.blobUrl -split '/')[-1] } | Select-Object -Unique)
    if ($documents -notcontains $expectedDocument) { throw "$knowledgeBase did not return $expectedDocument for '$question'. It returned: $($documents -join ', ')" }
    Write-Host "   ok   $knowledgeBase answers with $($documents -join ', ')"
}
Test-Retrieve 'leakops-ops-kb' 'What must an isolation plan include when a DN300 valve has not been operated for two years?' '01-isolation-sop-trunk-mains.md'
Test-Retrieve 'leakops-ops-kb' 'At what amount does a purchase order need approval?' '05-procurement-policy.md'
Test-Retrieve 'leakops-comms-kb' 'How should I write a public notice for a water disruption?' '04-resident-communication-guidelines.md'

if (-not $SkipRegister) {
    Write-Host "`n== 5. Foundry agents" -ForegroundColor Cyan
    Register-Agents $true
}

Write-Host "`n== Done" -ForegroundColor Green
Write-Host "   Search service     $searchEndpoint"
Write-Host "   Knowledge bases    leakops-ops-kb (Triage, Diagnostic, Procurement), leakops-comms-kb (Communications)"
Write-Host "   In the portal      https://ai.azure.com > project 'evides-leakops' > Knowledge, and Build > Agents > TriageAgent > Tools"
Write-Host "   Not demoing?       ./evides-leakops/deploy/deploy-knowledge.ps1 -Remove   (Search Basic costs about 70 EUR a month)"
