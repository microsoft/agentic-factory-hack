#!/usr/bin/env pwsh
# Copyright (c) Microsoft Corporation.
# SPDX-License-Identifier: MIT
#Requires -Version 7.0

<#
.SYNOPSIS
    Retrieve Azure resource settings and write the repository .env file.
.DESCRIPTION
    Read deployment outputs, discover missing resources, retrieve keys and
    endpoints, and write the environment variables required by the workshop.
.PARAMETER ResourceGroup
    Name of the resource group containing the workshop resources. When omitted,
    the script prompts for the name.
.EXAMPLE
    ./get-keys.ps1 -ResourceGroup my-resource-group
.NOTES
    Requires PowerShell 7+, Azure CLI, and permission to read resource keys.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

#region Functions

function Invoke-AzureCli {
    <#
    .SYNOPSIS
        Run Azure CLI and return trimmed text output.
    .OUTPUTS
        System.String.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        [switch]$AllowFailure
    )

    $CommandOutput = @(& az @Arguments 2>$null)
    $ExitCode = $LASTEXITCODE
    if ($ExitCode -ne 0) {
        if ($AllowFailure) {
            return [string]::Empty
        }

        throw "Azure CLI command failed with exit code ${ExitCode}: az $($Arguments -join ' ')"
    }

    return ($CommandOutput -join [Environment]::NewLine).Trim()
}

function Get-DeploymentOutput {
    <#
    .SYNOPSIS
        Read one value from an Azure deployment output.
    .OUTPUTS
        System.String.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,

        [Parameter(Mandatory = $true)]
        [string]$DeploymentName,

        [Parameter(Mandatory = $true)]
        [string]$OutputName
    )

    return Invoke-AzureCli -AllowFailure -Arguments @(
        'deployment', 'group', 'show',
        '--resource-group', $ResourceGroupName,
        '--name', $DeploymentName,
        '--query', "properties.outputs.${OutputName}.value",
        '--output', 'tsv'
    )
}

function New-EnvironmentLine {
    <#
    .SYNOPSIS
        Format one quoted dotenv assignment.
    .OUTPUTS
        System.String.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [AllowEmptyString()]
        [Parameter(Mandatory = $false)]
        [string]$Value = ''
    )

    if ($Value.Contains("`r") -or $Value.Contains("`n") -or $Value.Contains('"')) {
        throw "The value for $Name cannot be represented safely in the .env file."
    }

    return "${Name}=`"${Value}`""
}

function Invoke-KeyExport {
    <#
    .SYNOPSIS
        Discover workshop resources and create the repository .env file.
    .OUTPUTS
        None.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory = $false)]
        [string]$ResourceGroupName
    )

    if (-not (Get-Command -Name az -CommandType Application -ErrorAction SilentlyContinue)) {
        throw "Azure CLI was not found. Install Azure CLI and run 'az login'."
    }

    $SignedInAccount = Invoke-AzureCli -AllowFailure -Arguments @(
        'account', 'show', '--query', 'id', '--output', 'tsv'
    )
    if ([string]::IsNullOrWhiteSpace($SignedInAccount)) {
        Write-Host "User not signed in to Azure. Starting 'az login'..."
        & az login --use-device-code
        if ($LASTEXITCODE -ne 0) {
            throw "Azure CLI login failed with exit code $LASTEXITCODE."
        }
    }

    if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
        $ResourceGroupName = Read-Host 'Enter the resource group name where the resources are deployed'
    }
    if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
        throw 'Resource group name is required.'
    }

    Write-Host "Getting the deployments in '$ResourceGroupName'..."
    $DeploymentName = Invoke-AzureCli -Arguments @(
        'deployment', 'group', 'list',
        '--resource-group', $ResourceGroupName,
        '--query', "[?contains(name, 'Microsoft.Template') || contains(name, 'azuredeploy') || contains(name, 'hack-deployment') || contains(name, 'lab-services')].{name:name}[0].name",
        '--output', 'tsv'
    )
    if ([string]::IsNullOrWhiteSpace($DeploymentName)) {
        throw "No matching deployment was found in resource group '$ResourceGroupName'."
    }

    Write-Host "Getting output parameters from deployment '$DeploymentName'..."
    $StorageAccountName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'storageAccountName'
    $LogAnalyticsWorkspaceName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'logAnalyticsWorkspaceName'
    $SearchServiceName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'searchServiceName'
    $AiFoundryHubName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'aiFoundryHubName'
    $AiFoundryProjectName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'aiFoundryProjectName'
    $ContainerRegistryName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'containerRegistryName'
    $ApplicationInsightsName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'applicationInsightsName'
    $AcrName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'acrName'
    $AcrUsername = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'acrUsername'
    $AcrPassword = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'acrPassword'
    $SearchServiceEndpoint = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'searchServiceEndpoint'
    $AiFoundryHubEndpoint = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'aiFoundryHubEndpoint'
    $AiFoundryProjectEndpoint = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'aiFoundryProjectEndpoint'

    if ([string]::IsNullOrWhiteSpace($LogAnalyticsWorkspaceName)) {
        $LogAnalyticsWorkspaceName = Read-Host 'No Log Analytics workspace was found. Enter the workspace name, or press Enter to discover it'
    }

    Write-Host 'Discovering resources missing from deployment outputs...'
    if ([string]::IsNullOrWhiteSpace($StorageAccountName)) {
        $StorageAccountName = Invoke-AzureCli -AllowFailure -Arguments @(
            'storage', 'account', 'list', '--resource-group', $ResourceGroupName,
            '--query', '[0].name', '--output', 'tsv'
        )
    }
    if ([string]::IsNullOrWhiteSpace($LogAnalyticsWorkspaceName)) {
        $LogAnalyticsWorkspaceName = Invoke-AzureCli -AllowFailure -Arguments @(
            'monitor', 'log-analytics', 'workspace', 'list',
            '--resource-group', $ResourceGroupName, '--query', '[0].name', '--output', 'tsv'
        )
    }
    if ([string]::IsNullOrWhiteSpace($SearchServiceName)) {
        $SearchServiceName = Invoke-AzureCli -AllowFailure -Arguments @(
            'search', 'service', 'list', '--resource-group', $ResourceGroupName,
            '--query', '[0].name', '--output', 'tsv'
        )
    }
    if ([string]::IsNullOrWhiteSpace($AiFoundryHubName)) {
        $AiFoundryHubName = Invoke-AzureCli -AllowFailure -Arguments @(
            'cognitiveservices', 'account', 'list', '--resource-group', $ResourceGroupName,
            '--query', "[?kind=='AIServices'].name | [0]", '--output', 'tsv'
        )
    }
    if ([string]::IsNullOrWhiteSpace($ContainerRegistryName)) {
        $ContainerRegistryName = Invoke-AzureCli -AllowFailure -Arguments @(
            'acr', 'list', '--resource-group', $ResourceGroupName,
            '--query', '[0].name', '--output', 'tsv'
        )
    }
    if ([string]::IsNullOrWhiteSpace($ApplicationInsightsName)) {
        $ApplicationInsightsName = Invoke-AzureCli -AllowFailure -Arguments @(
            'resource', 'list', '--resource-group', $ResourceGroupName,
            '--resource-type', 'Microsoft.Insights/components',
            '--query', '[0].name', '--output', 'tsv'
        )
    }
    if ([string]::IsNullOrWhiteSpace($AiFoundryProjectName) -and
        -not [string]::IsNullOrWhiteSpace($AiFoundryHubName)) {
        $AiFoundryProjectName = Invoke-AzureCli -AllowFailure -Arguments @(
            'resource', 'list', '--resource-group', $ResourceGroupName,
            '--query', "[?contains(name, 'aiproject')].name | [0]", '--output', 'tsv'
        )
        if ([string]::IsNullOrWhiteSpace($AiFoundryProjectName)) {
            $AiFoundryProjectName = $AiFoundryHubName -replace '-aifoundry-', '-aiproject-'
            Write-Host "Constructed AI project name from hub name: $AiFoundryProjectName"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($LogAnalyticsWorkspaceName)) {
        $LogAnalyticsWorkspaceId = Invoke-AzureCli -AllowFailure -Arguments @(
            'monitor', 'log-analytics', 'workspace', 'show',
            '--resource-group', $ResourceGroupName,
            '--workspace-name', $LogAnalyticsWorkspaceName,
            '--query', 'customerId', '--output', 'tsv'
        )
    }
    else {
        $LogAnalyticsWorkspaceId = ''
    }

    $ApiManagementName = Invoke-AzureCli -AllowFailure -Arguments @(
        'apim', 'list', '--resource-group', $ResourceGroupName,
        '--query', '[0].name', '--output', 'tsv'
    )

    Write-Host 'Getting Cosmos DB service information...'
    $CosmosDbAccountName = Get-DeploymentOutput $ResourceGroupName $DeploymentName 'cosmosDbAccountName'
    if ([string]::IsNullOrWhiteSpace($CosmosDbAccountName)) {
        $CosmosDbAccountName = Invoke-AzureCli -AllowFailure -Arguments @(
            'cosmosdb', 'list', '--resource-group', $ResourceGroupName,
            '--query', '[0].name', '--output', 'tsv'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($CosmosDbAccountName)) {
        $CosmosDbEndpoint = Invoke-AzureCli -AllowFailure -Arguments @(
            'cosmosdb', 'show', '--name', $CosmosDbAccountName,
            '--resource-group', $ResourceGroupName,
            '--query', 'documentEndpoint', '--output', 'tsv'
        )
        $CosmosDbKey = Invoke-AzureCli -AllowFailure -Arguments @(
            'cosmosdb', 'keys', 'list', '--name', $CosmosDbAccountName,
            '--resource-group', $ResourceGroupName,
            '--query', 'primaryMasterKey', '--output', 'tsv'
        )
        if (-not [string]::IsNullOrWhiteSpace($CosmosDbEndpoint) -and
            -not [string]::IsNullOrWhiteSpace($CosmosDbKey)) {
            $CosmosDbConnectionString = "AccountEndpoint=${CosmosDbEndpoint};AccountKey=${CosmosDbKey};"
        }
        else {
            $CosmosDbConnectionString = ''
        }
    }
    else {
        Write-Warning 'No Cosmos DB account was found. You may need to deploy one.'
        $CosmosDbEndpoint = ''
        $CosmosDbKey = ''
        $CosmosDbConnectionString = ''
    }

    Write-Host 'Getting keys from the resources...'
    if (-not [string]::IsNullOrWhiteSpace($StorageAccountName)) {
        $StorageAccountKey = Invoke-AzureCli -AllowFailure -Arguments @(
            'storage', 'account', 'keys', 'list',
            '--account-name', $StorageAccountName,
            '--resource-group', $ResourceGroupName,
            '--query', '[0].value', '--output', 'tsv'
        )
        $StorageAccountConnectionString = "DefaultEndpointsProtocol=https;AccountName=${StorageAccountName};AccountKey=${StorageAccountKey};EndpointSuffix=core.windows.net"
    }
    else {
        Write-Warning 'Storage account not found.'
        $StorageAccountKey = ''
        $StorageAccountConnectionString = ''
    }

    if (-not [string]::IsNullOrWhiteSpace($AiFoundryHubName)) {
        $AiFoundryEndpoint = Invoke-AzureCli -AllowFailure -Arguments @(
            'cognitiveservices', 'account', 'show',
            '--name', $AiFoundryHubName, '--resource-group', $ResourceGroupName,
            '--query', 'properties.endpoint', '--output', 'tsv'
        )
        $AiFoundryKey = Invoke-AzureCli -AllowFailure -Arguments @(
            'cognitiveservices', 'account', 'keys', 'list',
            '--name', $AiFoundryHubName, '--resource-group', $ResourceGroupName,
            '--query', 'key1', '--output', 'tsv'
        )
    }
    else {
        $AiFoundryEndpoint = ''
        $AiFoundryKey = ''
    }
    if ([string]::IsNullOrWhiteSpace($AiFoundryHubName) -or
        [string]::IsNullOrWhiteSpace($AiFoundryEndpoint) -or
        [string]::IsNullOrWhiteSpace($AiFoundryKey)) {
        Write-Warning "Could not resolve the Azure AI Foundry endpoint and key. Challenge 2 variables may be empty. Check resources and key-list permissions in '$ResourceGroupName'."
    }

    if (-not [string]::IsNullOrWhiteSpace($SearchServiceName)) {
        $SearchServiceKey = Invoke-AzureCli -AllowFailure -Arguments @(
            'search', 'admin-key', 'show', '--resource-group', $ResourceGroupName,
            '--service-name', $SearchServiceName,
            '--query', 'primaryKey', '--output', 'tsv'
        )
        if ([string]::IsNullOrWhiteSpace($SearchServiceEndpoint)) {
            $SearchServiceEndpoint = "https://${SearchServiceName}.search.windows.net"
        }
        $SearchServiceEndpoint = "$($SearchServiceEndpoint.TrimEnd('/'))/"
    }
    else {
        Write-Warning 'Search service not found.'
        $SearchServiceKey = ''
        $SearchServiceEndpoint = ''
    }

    if (-not [string]::IsNullOrWhiteSpace($ApplicationInsightsName)) {
        $AppInsightsInstrumentationKey = Invoke-AzureCli -AllowFailure -Arguments @(
            'resource', 'show', '--resource-group', $ResourceGroupName,
            '--name', $ApplicationInsightsName,
            '--resource-type', 'Microsoft.Insights/components',
            '--query', 'properties.InstrumentationKey', '--output', 'tsv'
        )
        $AppInsightsConnectionString = Invoke-AzureCli -AllowFailure -Arguments @(
            'resource', 'show', '--resource-group', $ResourceGroupName,
            '--name', $ApplicationInsightsName,
            '--resource-type', 'Microsoft.Insights/components',
            '--query', 'properties.ConnectionString', '--output', 'tsv'
        )
    }
    else {
        Write-Warning 'Application Insights not found.'
        $AppInsightsInstrumentationKey = ''
        $AppInsightsConnectionString = ''
    }

    if (-not [string]::IsNullOrWhiteSpace($ApiManagementName)) {
        Write-Host 'Getting API Management credentials...'
        $ApimGatewayUrl = Invoke-AzureCli -AllowFailure -Arguments @(
            'apim', 'show', '--name', $ApiManagementName,
            '--resource-group', $ResourceGroupName,
            '--query', 'gatewayUrl', '--output', 'tsv'
        )
        $AccessToken = Invoke-AzureCli -Arguments @(
            'account', 'get-access-token', '--resource', 'https://management.azure.com/',
            '--query', 'accessToken', '--output', 'tsv'
        )
        $SubscriptionId = Invoke-AzureCli -Arguments @(
            'account', 'show', '--query', 'id', '--output', 'tsv'
        )
        $SecretsUri = "https://management.azure.com/subscriptions/${SubscriptionId}/resourceGroups/${ResourceGroupName}/providers/Microsoft.ApiManagement/service/${ApiManagementName}/subscriptions/master/listSecrets?api-version=2024-05-01"
        $ApimSecrets = Invoke-RestMethod -Method Post -Uri $SecretsUri -Headers @{
            Authorization = "Bearer $AccessToken"
        } -ContentType 'application/json' -Body '{}'
        $ApimSubscriptionKey = [string]$ApimSecrets.primaryKey
    }
    else {
        Write-Warning 'API Management not found.'
        $ApimGatewayUrl = ''
        $ApimSubscriptionKey = ''
    }

    if (-not [string]::IsNullOrWhiteSpace($ContainerRegistryName)) {
        Write-Host 'Getting Container Registry credentials...'
        if ([string]::IsNullOrWhiteSpace($AcrUsername) -or [string]::IsNullOrWhiteSpace($AcrPassword)) {
            $AcrUsername = Invoke-AzureCli -AllowFailure -Arguments @(
                'acr', 'credential', 'show', '--name', $ContainerRegistryName,
                '--query', 'username', '--output', 'tsv'
            )
            $AcrPassword = Invoke-AzureCli -AllowFailure -Arguments @(
                'acr', 'credential', 'show', '--name', $ContainerRegistryName,
                '--query', 'passwords[0].value', '--output', 'tsv'
            )
        }
        $AcrLoginServer = Invoke-AzureCli -AllowFailure -Arguments @(
            'acr', 'show', '--name', $ContainerRegistryName,
            '--resource-group', $ResourceGroupName,
            '--query', 'loginServer', '--output', 'tsv'
        )
        if ([string]::IsNullOrWhiteSpace($AcrName)) {
            $AcrName = $ContainerRegistryName
        }
    }
    else {
        Write-Warning 'Container Registry not found.'
        $AcrName = ''
        $AcrUsername = ''
        $AcrPassword = ''
        $AcrLoginServer = ''
    }

    $SubscriptionId = Invoke-AzureCli -Arguments @(
        'account', 'show', '--query', 'id', '--output', 'tsv'
    )
    if (-not [string]::IsNullOrWhiteSpace($AiFoundryHubName) -and
        -not [string]::IsNullOrWhiteSpace($SearchServiceName)) {
        $AzureAiConnectionId = "/subscriptions/${SubscriptionId}/resourceGroups/${ResourceGroupName}/providers/Microsoft.CognitiveServices/accounts/${AiFoundryHubName}/connections/${AiFoundryHubName}-aisearch"
        $AzureAiProjectResourceId = "/subscriptions/${SubscriptionId}/resourceGroups/${ResourceGroupName}/providers/Microsoft.CognitiveServices/accounts/${AiFoundryHubName}/projects/${AiFoundryProjectName}"
    }
    else {
        $AzureAiConnectionId = ''
        $AzureAiProjectResourceId = ''
    }

    if ([string]::IsNullOrWhiteSpace($AiFoundryHubEndpoint) -and
        -not [string]::IsNullOrWhiteSpace($AiFoundryHubName)) {
        $AiFoundryHubEndpoint = "https://ml.azure.com/home?wsid=/subscriptions/${SubscriptionId}/resourceGroups/${ResourceGroupName}/providers/Microsoft.CognitiveServices/accounts/${AiFoundryHubName}"
    }

    if (-not [string]::IsNullOrWhiteSpace($AiFoundryHubName) -and
        ([string]::IsNullOrWhiteSpace($AiFoundryProjectEndpoint) -or
        $AiFoundryProjectEndpoint.Contains('ai.azure.com/build/overview'))) {
        if ([string]::IsNullOrWhiteSpace($AiFoundryProjectName)) {
            $AiFoundryProjectName = $AiFoundryHubName -replace '-aifoundry-', '-aiproject-'
        }
        $AiFoundryProjectEndpoint = "https://${AiFoundryHubName}.services.ai.azure.com/api/projects/${AiFoundryProjectName}"
    }

    if (-not [string]::IsNullOrWhiteSpace($AiFoundryEndpoint)) {
        $AiChatEndpoint = "$($AiFoundryEndpoint.TrimEnd('/'))/openai/deployments/gpt-5.4-mini"
    }
    else {
        $AiChatEndpoint = ''
    }
    if (-not [string]::IsNullOrWhiteSpace($AiFoundryHubName)) {
        $AzureOpenAiEndpoint = "https://${AiFoundryHubName}.openai.azure.com/"
    }
    else {
        $AzureOpenAiEndpoint = $AiFoundryEndpoint
    }

    $EnvironmentLines = @(
        New-EnvironmentLine 'RESOURCE_GROUP' $ResourceGroupName
        New-EnvironmentLine 'AZURE_SUBSCRIPTION_ID' $SubscriptionId
        New-EnvironmentLine 'AZURE_STORAGE_ACCOUNT_NAME' $StorageAccountName
        New-EnvironmentLine 'AZURE_STORAGE_ACCOUNT_KEY' $StorageAccountKey
        New-EnvironmentLine 'AZURE_STORAGE_CONNECTION_STRING' $StorageAccountConnectionString
        New-EnvironmentLine 'LOG_ANALYTICS_WORKSPACE_NAME' $LogAnalyticsWorkspaceName
        New-EnvironmentLine 'LOG_ANALYTICS_WORKSPACE_ID' $LogAnalyticsWorkspaceId
        New-EnvironmentLine 'SEARCH_SERVICE_NAME' $SearchServiceName
        New-EnvironmentLine 'SEARCH_SERVICE_ENDPOINT' $SearchServiceEndpoint
        New-EnvironmentLine 'SEARCH_ADMIN_KEY' $SearchServiceKey
        New-EnvironmentLine 'AZURE_SEARCH_ENDPOINT' $SearchServiceEndpoint
        New-EnvironmentLine 'AZURE_SEARCH_API_KEY' $SearchServiceKey
        New-EnvironmentLine 'AI_FOUNDRY_HUB_NAME' $AiFoundryHubName
        New-EnvironmentLine 'AI_FOUNDRY_PROJECT_NAME' $AiFoundryProjectName
        New-EnvironmentLine 'AI_FOUNDRY_ENDPOINT' $AiFoundryEndpoint
        New-EnvironmentLine 'AI_FOUNDRY_KEY' $AiFoundryKey
        New-EnvironmentLine 'AZURE_OPENAI_CHAT_MODEL' 'gpt-5.4-mini'
        New-EnvironmentLine 'AZURE_AI_CHAT_KEY' $AiFoundryKey
        New-EnvironmentLine 'AZURE_AI_CHAT_ENDPOINT' $AiChatEndpoint
        New-EnvironmentLine 'AZURE_AI_CHAT_MODEL' 'gpt-5.4-mini'
        New-EnvironmentLine 'AZURE_AI_CHAT_MODEL_DEPLOYMENT_NAME' 'gpt-5.4-mini'
        New-EnvironmentLine 'AI_FOUNDRY_HUB_ENDPOINT' $AiFoundryHubEndpoint
        New-EnvironmentLine 'AI_FOUNDRY_PROJECT_ENDPOINT' $AiFoundryProjectEndpoint
        New-EnvironmentLine 'AZURE_AI_PROJECT_ENDPOINT' $AiFoundryProjectEndpoint
        New-EnvironmentLine 'AZURE_AI_PROJECT_RESOURCE_ID' $AzureAiProjectResourceId
        New-EnvironmentLine 'AZURE_AI_CONNECTION_ID' $AzureAiConnectionId
        New-EnvironmentLine 'AZURE_AI_MODEL_DEPLOYMENT_NAME' 'gpt-5.4'
        New-EnvironmentLine 'EMBEDDING_MODEL_DEPLOYMENT_NAME' 'text-embedding-3-large'
        New-EnvironmentLine 'COSMOS_NAME' $CosmosDbAccountName
        New-EnvironmentLine 'COSMOS_DATABASE_NAME' 'FactoryOpsDB'
        New-EnvironmentLine 'COSMOS_ENDPOINT' $CosmosDbEndpoint
        New-EnvironmentLine 'COSMOS_KEY' $CosmosDbKey
        New-EnvironmentLine 'COSMOS_CONNECTION_STRING' $CosmosDbConnectionString
        New-EnvironmentLine 'APIM_NAME' $ApiManagementName
        New-EnvironmentLine 'APIM_GATEWAY_URL' $ApimGatewayUrl
        New-EnvironmentLine 'APIM_SUBSCRIPTION_KEY' $ApimSubscriptionKey
        New-EnvironmentLine 'ACR_NAME' $AcrName
        New-EnvironmentLine 'ACR_USERNAME' $AcrUsername
        New-EnvironmentLine 'ACR_PASSWORD' $AcrPassword
        New-EnvironmentLine 'ACR_LOGIN_SERVER' $AcrLoginServer
        New-EnvironmentLine 'APPLICATION_INSIGHTS_INSTRUMENTATION_KEY' $AppInsightsInstrumentationKey
        New-EnvironmentLine 'APPLICATION_INSIGHTS_CONNECTION_STRING' $AppInsightsConnectionString
        New-EnvironmentLine 'APPLICATIONINSIGHTS_CONNECTION_STRING' $AppInsightsConnectionString
        New-EnvironmentLine 'AZURE_OPENAI_SERVICE_NAME' $AiFoundryHubName
        New-EnvironmentLine 'AZURE_OPENAI_ENDPOINT' $AzureOpenAiEndpoint
        New-EnvironmentLine 'AZURE_OPENAI_KEY' $AiFoundryKey
        New-EnvironmentLine 'AZURE_OPENAI_DEPLOYMENT_NAME' 'gpt-5.4'
        New-EnvironmentLine 'MODEL_DEPLOYMENT_NAME' 'gpt-5.4'
    )

    $EnvironmentPath = Join-Path (Split-Path -Parent $PSScriptRoot) '.env'
    $EnvironmentLines | Set-Content -LiteralPath $EnvironmentPath -Encoding utf8
    Write-Host "Keys and properties were stored in '$EnvironmentPath'." -ForegroundColor Green

    Write-Host ''
    Write-Host '=== Configuration Summary ==='
    Write-Host "Storage Account: $StorageAccountName"
    Write-Host "Log Analytics Workspace: $LogAnalyticsWorkspaceName"
    Write-Host "Search Service: $SearchServiceName"
    Write-Host "API Management: $ApiManagementName"
    Write-Host "AI Foundry Hub: $AiFoundryHubName"
    Write-Host "AI Foundry Project: $AiFoundryProjectName"
    Write-Host "AI Foundry Hub Endpoint: $AiFoundryHubEndpoint"
    Write-Host "AI Foundry Project Endpoint: $AiFoundryProjectEndpoint"
    Write-Host "Container Registry: $ContainerRegistryName"
    Write-Host "Application Insights: $ApplicationInsightsName"
    if (-not [string]::IsNullOrWhiteSpace($CosmosDbAccountName)) {
        Write-Host "Cosmos DB: $CosmosDbAccountName"
    }
    else {
        Write-Host 'Cosmos DB: NOT FOUND - You may need to deploy this service'
    }

    $MissingServices = @()
    if ([string]::IsNullOrWhiteSpace($StorageAccountName)) { $MissingServices += 'Storage' }
    if ([string]::IsNullOrWhiteSpace($SearchServiceName)) { $MissingServices += 'Search' }
    if ([string]::IsNullOrWhiteSpace($AiFoundryHubName)) { $MissingServices += 'AI-Foundry' }
    if ([string]::IsNullOrWhiteSpace($ApiManagementName)) { $MissingServices += 'API-Management' }
    if ([string]::IsNullOrWhiteSpace($ContainerRegistryName)) { $MissingServices += 'Container-Registry' }
    if ($MissingServices.Count -gt 0) {
        Write-Warning "Missing services: $($MissingServices -join ' ')"
        Write-Warning 'You may need to deploy these services or check the deployment template.'
    }
}

#endregion Functions

#region Main Execution

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-KeyExport -ResourceGroupName $ResourceGroup
        exit 0
    }
    catch {
        Write-Error -ErrorAction Continue "get-keys.ps1 failed: $($_.Exception.Message)"
        exit 1
    }
}

#endregion Main Execution
