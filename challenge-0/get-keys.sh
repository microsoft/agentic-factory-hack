#!/bin/bash
# Resolve paths relative to this script (so it works no matter where you run it from)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_OUT="$SCRIPT_DIR/../.env"
#
# This script will retrieve necessary keys and properties from Azure Resources 
# deployed using "Deploy to Azure" button and will store them in a file named
# ".env" in the parent directory.

# Login to Azure
if [ -z "$(az account show)" ]; then
  echo "User not signed in Azure. Signin to Azure using 'az login' command."
  az login --use-device-code
fi

# Get the resource group name from the script parameter named resource-group
resourceGroupName=""

# Parse named parameters
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --resource-group) resourceGroupName="$2"; shift ;;
        *) echo "Unknown parameter passed: $1"; exit 1 ;;
    esac
    shift
done

# Check if resourceGroupName is provided
if [ -z "$resourceGroupName" ]; then
    echo "Enter the resource group name where the resources are deployed:"
    read resourceGroupName
fi

# Get resource group deployments, find deployments starting with 'Microsoft.Template' and sort them by timestamp
echo "Getting the deployments in '$resourceGroupName'..."
deploymentName=$(az deployment group list --resource-group $resourceGroupName --query "[?contains(name, 'Microsoft.Template') || contains(name, 'azuredeploy') || contains(name, 'hack-deployment')].{name:name}[0].name" --output tsv)
if [ $? -ne 0 ]; then
    echo "Error occurred while fetching deployments. Exiting..."
    exit 1
fi

# Get output parameters from last deployment using Azure CLI queries instead of jq
echo "Getting the output parameters from the last deployment '$deploymentName' in '$resourceGroupName'..."

# Extract the resource names directly using Azure CLI queries
echo "Extracting the resource names from the deployment outputs..."
storageAccountName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.storageAccountName.value" -o tsv 2>/dev/null || echo "")
logAnalyticsWorkspaceName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.logAnalyticsWorkspaceName.value" -o tsv 2>/dev/null || echo "")
if [ -z "$logAnalyticsWorkspaceName" ]; then
    echo "No Log Analytics workspace found. Please enter the workspace name manually:"
    read logAnalyticsWorkspaceName
fi
if [ -n "$logAnalyticsWorkspaceName" ]; then
    logAnalyticsWorkspaceId=$(az monitor log-analytics workspace show --resource-group $resourceGroupName --workspace-name $logAnalyticsWorkspaceName --query customerId -o tsv 2>/dev/null || echo "")
    if [ -n "$logAnalyticsWorkspaceId" ]; then
        echo "Retrieved Log Analytics workspace ID: $logAnalyticsWorkspaceId"
    fi
else
    logAnalyticsWorkspaceId=""
fi
searchServiceName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.searchServiceName.value" -o tsv 2>/dev/null || echo "")
aiFoundryHubName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.aiFoundryHubName.value" -o tsv 2>/dev/null || echo "")
aiFoundryProjectName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.aiFoundryProjectName.value" -o tsv 2>/dev/null || echo "")
containerRegistryName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.containerRegistryName.value" -o tsv 2>/dev/null || echo "")
applicationInsightsName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.applicationInsightsName.value" -o tsv 2>/dev/null || echo "")

# Get ACR credentials from deployment outputs
acrName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.acrName.value" -o tsv 2>/dev/null || echo "")
acrUsername=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.acrUsername.value" -o tsv 2>/dev/null || echo "")
acrPassword=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.acrPassword.value" -o tsv 2>/dev/null || echo "")

# Extract endpoint URLs
searchServiceEndpoint=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.searchServiceEndpoint.value" -o tsv 2>/dev/null || echo "")
aiFoundryHubEndpoint=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.aiFoundryHubEndpoint.value" -o tsv 2>/dev/null || echo "")
aiFoundryProjectEndpoint=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.aiFoundryProjectEndpoint.value" -o tsv 2>/dev/null || echo "")



# If deployment outputs are empty, try to discover resources by type
if [ -z "$storageAccountName" ] || [ -z "$logAnalyticsWorkspaceName" ] || [ -z "$containerRegistryName" ]; then
    echo "Some deployment outputs not found, discovering missing resources by type..."
    
    if [ -z "$storageAccountName" ]; then
        storageAccountName=$(az storage account list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")
    fi
    
    if [ -z "$logAnalyticsWorkspaceName" ]; then
        echo "Discovering Log Analytics workspace..."
        logAnalyticsWorkspaceName=$(az monitor log-analytics workspace list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")
        if [ -n "$logAnalyticsWorkspaceName" ]; then
            echo "Found Log Analytics workspace: $logAnalyticsWorkspaceName"
        fi
    fi
    
    if [ -z "$searchServiceName" ]; then
        searchServiceName=$(az search service list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")
    fi
    
    if [ -z "$aiFoundryHubName" ]; then
        aiFoundryHubName=$(az cognitiveservices account list --resource-group $resourceGroupName --query "[?kind=='AIServices'].name | [0]" -o tsv 2>/dev/null || echo "")
    fi
    
    if [ -z "$containerRegistryName" ]; then
        containerRegistryName=$(az acr list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")
    fi
    
    if [ -z "$applicationInsightsName" ]; then
        applicationInsightsName=$(az resource list --resource-group $resourceGroupName --resource-type "Microsoft.Insights/components" --query "[0].name" -o tsv 2>/dev/null || echo "")
    fi
fi

# Discover API Management service
apiManagementName=$(az apim list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")

# Get Cosmos DB service information (better retrieval)
echo "Getting Cosmos DB service information..."
cosmosDbAccountName=$(az deployment group show --resource-group $resourceGroupName --name $deploymentName --query "properties.outputs.cosmosDbAccountName.value" -o tsv 2>/dev/null || echo "")
if [ -z "$cosmosDbAccountName" ]; then
    cosmosDbAccountName=$(az cosmosdb list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")
fi

if [ -n "$cosmosDbAccountName" ]; then
    cosmosDbEndpoint=$(az cosmosdb show --name $cosmosDbAccountName --resource-group $resourceGroupName --query documentEndpoint -o tsv 2>/dev/null || echo "")
    cosmosDbKey=$(az cosmosdb keys list --name $cosmosDbAccountName --resource-group $resourceGroupName --query primaryMasterKey -o tsv 2>/dev/null || echo "")
    
    # Construct the connection string properly
    if [ -n "$cosmosDbEndpoint" ] && [ -n "$cosmosDbKey" ]; then
        cosmosDbConnectionString="AccountEndpoint=${cosmosDbEndpoint};AccountKey=${cosmosDbKey};"
    else
        cosmosDbConnectionString=""
    fi
else
    echo "Warning: No Cosmos DB account found in resource group. You may need to deploy one."
    cosmosDbEndpoint=""
    cosmosDbKey=""
    cosmosDbConnectionString=""
fi

# Get the keys from the resources
echo "Getting the keys from the resources..."

# Storage account
if [ -n "$storageAccountName" ]; then
    storageAccountKey=$(az storage account keys list --account-name $storageAccountName --resource-group $resourceGroupName --query "[0].value" -o tsv 2>/dev/null || echo "")
    # Construct the connection string in the correct format
    storageAccountConnectionString="DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${storageAccountKey};EndpointSuffix=core.windows.net"
else
    echo "Warning: Storage account not found"
    storageAccountKey=""
    storageAccountConnectionString=""
fi

# AI Foundry/Cognitive Services
if [ -n "$aiFoundryHubName" ]; then
    aiFoundryEndpoint=$(az cognitiveservices account show --name $aiFoundryHubName --resource-group $resourceGroupName --query properties.endpoint -o tsv 2>/dev/null || echo "")
    aiFoundryKey=$(az cognitiveservices account keys list --name $aiFoundryHubName --resource-group $resourceGroupName --query key1 -o tsv 2>/dev/null || echo "")
else
    echo "Warning: AI Foundry Hub not found"
    aiFoundryEndpoint=""
    aiFoundryKey=""
fi

# Warning-only: Challenge 2 uses AI endpoint/key, but we still generate .env for the rest of the workshop.
if [ -z "$aiFoundryHubName" ] || [ -z "$aiFoundryEndpoint" ] || [ -z "$aiFoundryKey" ]; then
    echo "⚠️  Could not resolve Azure AI Foundry (Cognitive Services) endpoint/key." >&2
    echo "    Challenge 2 vars AZURE_AI_CHAT_ENDPOINT / AZURE_AI_CHAT_KEY may be empty." >&2
    echo "    Troubleshooting:" >&2
    echo "    - Confirm resources exist in resource group: $resourceGroupName" >&2
    echo "    - Ensure you have access to the AI resource (keys list permissions)" >&2
fi

# Search service
if [ -n "$searchServiceName" ]; then
    searchServiceKey=$(az search admin-key show --resource-group $resourceGroupName --service-name $searchServiceName --query primaryKey -o tsv 2>/dev/null || echo "")
    if [ -z "$searchServiceEndpoint" ]; then
        searchServiceEndpoint="https://${searchServiceName}.search.windows.net"
    fi
else
    echo "Warning: Search service not found"
    searchServiceKey=""
    searchServiceEndpoint=""
fi

# Application Insights
if [ -n "$applicationInsightsName" ]; then
    appInsightsInstrumentationKey=$(az resource show --resource-group $resourceGroupName --name $applicationInsightsName --resource-type "Microsoft.Insights/components" --query properties.InstrumentationKey -o tsv 2>/dev/null || echo "")
    appInsightsConnectionString=$(az resource show --resource-group $resourceGroupName --name $applicationInsightsName --resource-type "Microsoft.Insights/components" --query properties.ConnectionString -o tsv 2>/dev/null || echo "")
else
    echo "Warning: Application Insights not found"
    appInsightsInstrumentationKey=""
    appInsightsConnectionString=""
fi

# API Management
if [ -n "$apiManagementName" ]; then
    echo "Getting API Management credentials..."
    apimGatewayUrl=$(az apim show --name $apiManagementName --resource-group $resourceGroupName --query gatewayUrl -o tsv 2>/dev/null || echo "")
    # Get subscription keys (primary key from default subscription)
    TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)
    SUB=$(az account show --query id --output tsv)
    apimSubscriptionKey=$(curl -X POST \
        -H "Authorization: Bearer $TOKEN" \
        -d "" \
        "https://management.azure.com/subscriptions/$SUB/resourceGroups/$resourceGroupName/providers/Microsoft.ApiManagement/service/$apiManagementName/subscriptions/master/listSecrets?api-version=2024-05-01" \
        | jq '.primaryKey' | sed 's/"//g' 2>/dev/null || echo "")

else
    echo "Warning: API Management not found"
    apimGatewayUrl=""
    apimSubscriptionKey=""
fi

# Container Registry (ACR)
if [ -n "$containerRegistryName" ]; then
    echo "Getting Container Registry credentials..."
    # Use deployment outputs first, then fallback to direct queries
    if [ -z "$acrUsername" ] || [ -z "$acrPassword" ]; then
        acrUsername=$(az acr credential show --name $containerRegistryName --query username -o tsv 2>/dev/null || echo "")
        acrPassword=$(az acr credential show --name $containerRegistryName --query passwords[0].value -o tsv 2>/dev/null || echo "")
    fi
    acrLoginServer=$(az acr show --name $containerRegistryName --resource-group $resourceGroupName --query loginServer -o tsv 2>/dev/null || echo "")
    if [ -z "$acrName" ]; then
        acrName="$containerRegistryName"
    fi
else
    echo "Warning: Container Registry not found"
    acrUsername=""
    acrPassword=""
    acrLoginServer=""
    acrName=""
fi

# Get Azure AI Search connection ID
# Note: The 'az cognitiveservices account connection' command is not available in all Azure CLI versions
# We'll construct the connection ID manually later in the script
echo "Skipping Azure AI Search connection query (will construct manually)..."
azureAIConnectionId=""


if [ -z "$storageAccountName" ] || [ -z "$aiFoundryProjectName" ]; then
    if [ -z "$storageAccountName" ]; then
        echo "Deployment outputs not found, discovering resources by type..."
    fi
    if [ -z "$aiFoundryProjectName" ]; then
        echo "AI Foundry Project Name not found in deployment outputs, attempting discovery..."
    fi
    
    storageAccountName=$(az storage account list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")
    searchServiceName=$(az search service list --resource-group $resourceGroupName --query "[0].name" -o tsv 2>/dev/null || echo "")
    aiFoundryHubName=$(az cognitiveservices account list --resource-group $resourceGroupName --query "[?kind=='AIServices'].name | [0]" -o tsv 2>/dev/null || echo "")
    applicationInsightsName=$(az resource list --resource-group $resourceGroupName --resource-type "Microsoft.Insights/components" --query "[0].name" -o tsv 2>/dev/null || echo "")
    
    # Try to discover AI Foundry project name if missing
    if [ -z "$aiFoundryProjectName" ] && [ -n "$aiFoundryHubName" ]; then
        echo "Attempting to discover AI Foundry project for hub: $aiFoundryHubName"
        # Look for resources with 'aiproject' in the name using direct resource listing
        aiFoundryProjectName=$(az resource list --resource-group $resourceGroupName --query "[?contains(name, 'aiproject')].name | [0]" -o tsv 2>/dev/null || echo "")
        if [ -n "$aiFoundryProjectName" ]; then
            echo "Found AI project resource: $aiFoundryProjectName"
        else
            # Fallback: construct expected project name based on hub name pattern
            # Hub: {prefix}-aifoundry-{suffix} -> Project: {prefix}-aiproject-{suffix}
            aiFoundryProjectName=$(echo "$aiFoundryHubName" | sed 's/-aifoundry-/-aiproject-/')
            echo "Constructed AI project name from hub name: $aiFoundryProjectName"
        fi
    fi
fi

# Construct Azure AI Search connection ID directly
if [ -n "$aiFoundryHubName" ] && [ -n "$searchServiceName" ]; then
    echo "Constructing Azure AI Search connection ID..."
    
    # Get subscription ID
    subscriptionId=$(az account show --query id -o tsv 2>/dev/null || echo "")
    
    if [ -n "$subscriptionId" ]; then
        # Construct the connection ID based on the pattern: aiFoundryHubName + "-aisearch"
        # Pattern: /subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{aiFoundryHub}/connections/{aiFoundryHub}-aisearch
        azureAIConnectionId="/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.CognitiveServices/accounts/${aiFoundryHubName}/connections/${aiFoundryHubName}-aisearch"
        echo "Constructed connection ID: $azureAIConnectionId"
        
        # Construct the AI Project resource ID
        # Pattern: /subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{aiFoundryHub}/projects/{aiFoundryProject}
        azureAIProjectResourceId="/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.CognitiveServices/accounts/${aiFoundryHubName}/projects/${aiFoundryProjectName}"
        echo "Constructed AI Project resource ID: $azureAIProjectResourceId"
    else
        echo "Warning: Could not get subscription ID"
            azureAIProjectResourceId=""
        azureAIConnectionId=""
    fi
else
    echo "Warning: Cannot construct Azure AI connection ID - AI Foundry Hub or Search Service not found"
    azureAIConnectionId=""
fi

# Note: AI Foundry Project Endpoint construction is handled later in the script

# Overwrite the existing .env file
if [ -f "$ENV_OUT" ]; then
    rm "$ENV_OUT"
fi

# Store the keys and properties in a file
echo "Storing the keys and properties in '.env' file..."

# Get subscription ID
subscriptionId=$(az account show --query id -o tsv 2>/dev/null || echo "")

# Azure resource group and subscription
echo "RESOURCE_GROUP=\"$resourceGroupName\"" >> "$ENV_OUT"
echo "AZURE_SUBSCRIPTION_ID=\"$subscriptionId\"" >> "$ENV_OUT"

# Azure Storage (with both naming conventions)
echo "AZURE_STORAGE_ACCOUNT_NAME=\"$storageAccountName\"" >> "$ENV_OUT"
echo "AZURE_STORAGE_ACCOUNT_KEY=\"$storageAccountKey\"" >> "$ENV_OUT"
echo "AZURE_STORAGE_CONNECTION_STRING=\"$storageAccountConnectionString\"" >> "$ENV_OUT"

# Other Azure services
echo "LOG_ANALYTICS_WORKSPACE_NAME=\"$logAnalyticsWorkspaceName\"" >> "$ENV_OUT"
echo "LOG_ANALYTICS_WORKSPACE_ID=\"$logAnalyticsWorkspaceId\"" >> "$ENV_OUT"
echo "SEARCH_SERVICE_NAME=\"$searchServiceName\"" >> "$ENV_OUT"
echo "SEARCH_SERVICE_ENDPOINT=\"$searchServiceEndpoint\"" >> "$ENV_OUT"
echo "SEARCH_ADMIN_KEY=\"$searchServiceKey\"" >> "$ENV_OUT"

# Azure Search variables for document processor compatibility
echo "AZURE_SEARCH_ENDPOINT=\"$searchServiceEndpoint\"" >> "$ENV_OUT"
echo "AZURE_SEARCH_API_KEY=\"$searchServiceKey\"" >> "$ENV_OUT"

echo "AI_FOUNDRY_HUB_NAME=\"$aiFoundryHubName\"" >> "$ENV_OUT"
echo "AI_FOUNDRY_PROJECT_NAME=\"$aiFoundryProjectName\"" >> "$ENV_OUT"
echo "AI_FOUNDRY_ENDPOINT=\"$aiFoundryEndpoint\"" >> "$ENV_OUT"
echo "AI_FOUNDRY_KEY=\"$aiFoundryKey\"" >> "$ENV_OUT"

# The new packages required OpenAI Standard for env variables
echo "AZURE_OPENAI_CHAT_MODEL=\"gpt-4o-mini\"" >> "$ENV_OUT"

# RepairPlannerAgent (Challenge 2) environment variables
# For chat, use the same key as the AI Foundry/Cognitive Services account.
echo "AZURE_AI_CHAT_KEY=\"$aiFoundryKey\"" >> "$ENV_OUT"

# Chat endpoint is derived from the Cognitive Services endpoint.
# Expected final format:
#   https://<resource>.cognitiveservices.azure.com/openai/deployments/gpt-4o-mini
if [ -n "$aiFoundryEndpoint" ]; then
    aiChatBaseEndpoint=${aiFoundryEndpoint%/}
    echo "AZURE_AI_CHAT_ENDPOINT=\"${aiChatBaseEndpoint}/openai/deployments/gpt-4o-mini\"" >> "$ENV_OUT"
else
    echo "AZURE_AI_CHAT_ENDPOINT=\"\"" >> "$ENV_OUT"
fi
echo "AZURE_AI_CHAT_MODEL=\"$azureAIChatModel\"" >> "$ENV_OUT"
# Constant for the workshop (placed after the endpoint for readability)
echo "AZURE_AI_CHAT_MODEL_DEPLOYMENT_NAME=\"gpt-4o-mini\"" >> "$ENV_OUT"
# Construct AI Foundry Hub Endpoint if missing
if [ -z "$aiFoundryHubEndpoint" ] && [ -n "$aiFoundryHubName" ]; then
    echo "Constructing AI Foundry Hub Endpoint..."
    subscriptionId=$(az account show --query id -o tsv 2>/dev/null || echo "")
    if [ -n "$subscriptionId" ]; then
        aiFoundryHubEndpoint="https://ml.azure.com/home?wsid=/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.CognitiveServices/accounts/${aiFoundryHubName}"
        echo "Constructed hub endpoint: $aiFoundryHubEndpoint"
    fi
fi
echo "AI_FOUNDRY_HUB_ENDPOINT=\"$aiFoundryHubEndpoint\"" >> "$ENV_OUT"

# Construct AI Foundry Project Endpoint if not found in deployment outputs
if [ -z "$aiFoundryProjectEndpoint" ] && [ -n "$aiFoundryHubName" ]; then
    echo "Constructing AI Foundry Project Endpoint..."
    
    # Ensure we have the project name
    if [ -z "$aiFoundryProjectName" ]; then
        echo "Attempting to discover AI Foundry project..."
        # Look for resources with 'aiproject' in the name
        aiFoundryProjectName=$(az resource list --resource-group $resourceGroupName --query "[?contains(name, 'aiproject')].name | [0]" -o tsv 2>/dev/null || echo "")
        if [ -n "$aiFoundryProjectName" ]; then
            echo "Found AI project resource: $aiFoundryProjectName"
        else
            # Fallback: construct expected project name based on hub name pattern
            aiFoundryProjectName=$(echo "$aiFoundryHubName" | sed 's/-aifoundry-/-aiproject-/')
            echo "Constructed AI project name from hub pattern: $aiFoundryProjectName"
        fi
    fi
    
    # Construct the correct AI Foundry project endpoint
    if [ -n "$aiFoundryHubName" ] && [ -n "$aiFoundryProjectName" ]; then
        aiFoundryProjectEndpoint="https://${aiFoundryHubName}.services.ai.azure.com/api/projects/${aiFoundryProjectName}"
        echo "Constructed AI Foundry project endpoint: $aiFoundryProjectEndpoint"
    fi
elif [ -n "$aiFoundryProjectEndpoint" ] && [[ "$aiFoundryProjectEndpoint" == *"ai.azure.com/build/overview"* ]]; then
    # If we got a web UI URL from deployment outputs, convert it to API endpoint
    echo "Converting web UI URL to API endpoint..."
    
    # Ensure we have the project name for URL construction
    if [ -z "$aiFoundryProjectName" ]; then
        # Look for resources with 'aiproject' in the name
        aiFoundryProjectName=$(az resource list --resource-group $resourceGroupName --query "[?contains(name, 'aiproject')].name | [0]" -o tsv 2>/dev/null || echo "")
        if [ -z "$aiFoundryProjectName" ] && [ -n "$aiFoundryHubName" ]; then
            # Fallback: construct from hub name
            aiFoundryProjectName=$(echo "$aiFoundryHubName" | sed 's/-aifoundry-/-aiproject-/')
            echo "Constructed AI project name for URL conversion: $aiFoundryProjectName"
        fi
    fi
    
    if [ -n "$aiFoundryHubName" ] && [ -n "$aiFoundryProjectName" ]; then
        aiFoundryProjectEndpoint="https://${aiFoundryHubName}.services.ai.azure.com/api/projects/${aiFoundryProjectName}"
        echo "Converted to API endpoint: $aiFoundryProjectEndpoint"
    fi
fi
echo "AI_FOUNDRY_PROJECT_ENDPOINT=\"$aiFoundryProjectEndpoint\"" >> "$ENV_OUT"
echo "AZURE_AI_PROJECT_ENDPOINT=\"$aiFoundryProjectEndpoint\"" >> "$ENV_OUT"
echo "AZURE_AI_PROJECT_RESOURCE_ID=\"$azureAIProjectResourceId\"" >> "$ENV_OUT"
echo "AZURE_AI_CONNECTION_ID=\"$azureAIConnectionId\"" >> "$ENV_OUT"
echo "AZURE_AI_MODEL_DEPLOYMENT_NAME=\"gpt-4.1\"" >> "$ENV_OUT"
echo "EMBEDDING_MODEL_DEPLOYMENT_NAME=\"text-embedding-3-large\"" >> "$ENV_OUT"
# Azure Cosmos DB
echo "COSMOS_NAME=\"$cosmosDbAccountName\"" >> "$ENV_OUT"
echo "COSMOS_DATABASE_NAME=\"FactoryOpsDB\"" >> "$ENV_OUT"
echo "COSMOS_ENDPOINT=\"$cosmosDbEndpoint\"" >> "$ENV_OUT"
echo "COSMOS_KEY=\"$cosmosDbKey\"" >> "$ENV_OUT"
echo "COSMOS_CONNECTION_STRING=\"$cosmosDbConnectionString\"" >> "$ENV_OUT"

# API Management
echo "APIM_NAME=\"$apiManagementName\"" >> "$ENV_OUT"
echo "APIM_GATEWAY_URL=\"$apimGatewayUrl\"" >> "$ENV_OUT"
echo "APIM_SUBSCRIPTION_KEY=\"$apimSubscriptionKey\"" >> "$ENV_OUT"

# Container Registry (ACR)
echo "ACR_NAME=\"$acrName\"" >> "$ENV_OUT"
echo "ACR_USERNAME=\"$acrUsername\"" >> "$ENV_OUT"
echo "ACR_PASSWORD=\"$acrPassword\"" >> "$ENV_OUT"
echo "ACR_LOGIN_SERVER=\"$acrLoginServer\"" >> "$ENV_OUT"

# Application Insights
echo "APPLICATION_INSIGHTS_INSTRUMENTATION_KEY=\"$appInsightsInstrumentationKey\"" >> "$ENV_OUT"
echo "APPLICATION_INSIGHTS_CONNECTION_STRING=\"$appInsightsConnectionString\"" >> "$ENV_OUT"
echo "APPLICATIONINSIGHTS_CONNECTION_STRING=\"$appInsightsConnectionString\"" >> "$ENV_OUT"

# For backward compatibility, also set OpenAI-style variables pointing to AI Foundry
echo "AZURE_OPENAI_SERVICE_NAME=\"$aiFoundryHubName\"" >> "$ENV_OUT"
# Construct correct Azure OpenAI endpoint format (.openai.azure.com instead of .cognitiveservices.azure.com)
if [ -n "$aiFoundryHubName" ]; then
    azureOpenAIEndpoint="https://${aiFoundryHubName}.openai.azure.com/"
else
    azureOpenAIEndpoint="$aiFoundryEndpoint"
fi
echo "AZURE_OPENAI_ENDPOINT=\"$azureOpenAIEndpoint\"" >> "$ENV_OUT"
echo "AZURE_OPENAI_KEY=\"$aiFoundryKey\"" >> "$ENV_OUT"
echo "AZURE_OPENAI_DEPLOYMENT_NAME=\"gpt-4.1\"" >> "$ENV_OUT"
echo "MODEL_DEPLOYMENT_NAME=\"gpt-4.1\"" >> "$ENV_OUT"

echo "Keys and properties are stored in '.env' file successfully."

# Display summary of what was configured
echo ""
echo "=== Configuration Summary ==="
echo "Storage Account: $storageAccountName"
echo "Log Analytics Workspace: $logAnalyticsWorkspaceName"
echo "Search Service: $searchServiceName"
echo "API Management: $apiManagementName"
echo "AI Foundry Hub: $aiFoundryHubName"
echo "AI Foundry Project: $aiFoundryProjectName"
echo "AI Foundry Hub Endpoint: $aiFoundryHubEndpoint"
echo "AI Foundry Project Endpoint: $aiFoundryProjectEndpoint"
echo "Container Registry: $containerRegistryName"
echo "Application Insights: $applicationInsightsName"

if [ -n "$cosmosDbAccountName" ]; then
    echo "Cosmos DB: $cosmosDbAccountName"
else
    echo "Cosmos DB: NOT FOUND - You may need to deploy this service"
fi
echo "Environment file created: $ENV_OUT"

# Show what needs to be deployed
missing_services=""
if [ -z "$storageAccountName" ]; then missing_services="$missing_services Storage"; fi
if [ -z "$searchServiceName" ]; then missing_services="$missing_services Search"; fi
if [ -z "$aiFoundryHubName" ]; then missing_services="$missing_services AI-Foundry"; fi
if [ -z "$apiManagementName" ]; then missing_services="$missing_services API-Management"; fi
if [ -z "$containerRegistryName" ]; then missing_services="$missing_services Container-Registry"; fi

if [ -n "$missing_services" ]; then
    echo ""
    echo "⚠️  Missing services:$missing_services"
    echo "You may need to deploy these services manually or check your deployment template."
fi