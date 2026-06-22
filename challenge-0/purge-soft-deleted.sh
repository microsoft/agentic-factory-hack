#!/bin/bash
# Script to purge all soft-deleted Azure resources
# Usage: ./purge-soft-deleted.sh [--resource-group <name>]

set -e

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --resource-group|-g)
            RESOURCE_GROUP="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

if [ -z "$RESOURCE_GROUP" ]; then
    echo "Usage: ./purge-soft-deleted.sh --resource-group <resource-group-name>"
    exit 1
fi

echo "=== Purging soft-deleted resources for resource group: $RESOURCE_GROUP ==="

# Get location from resource group
LOCATION=$(az group show --name "$RESOURCE_GROUP" --query location -o tsv 2>/dev/null || echo "")

# 1. Purge soft-deleted Key Vaults
echo ""
echo ">>> Checking for soft-deleted Key Vaults..."
DELETED_VAULTS=$(az keyvault list-deleted --query "[?properties.vaultId && contains(properties.vaultId, '$RESOURCE_GROUP')].name" -o tsv 2>/dev/null || echo "")

if [ -n "$DELETED_VAULTS" ]; then
    for vault in $DELETED_VAULTS; do
        echo "Purging Key Vault: $vault"
        az keyvault purge --name "$vault" --no-wait || echo "  Failed to purge $vault"
    done
else
    echo "  No soft-deleted Key Vaults found."
fi

# Also check all deleted vaults in the location
if [ -n "$LOCATION" ]; then
    echo ">>> Checking all soft-deleted Key Vaults in location: $LOCATION..."
    DELETED_VAULTS_LOC=$(az keyvault list-deleted --query "[?properties.location=='$LOCATION'].name" -o tsv 2>/dev/null || echo "")
    if [ -n "$DELETED_VAULTS_LOC" ]; then
        for vault in $DELETED_VAULTS_LOC; do
            echo "Purging Key Vault: $vault"
            az keyvault purge --name "$vault" --location "$LOCATION" --no-wait 2>/dev/null || echo "  Failed to purge $vault (may already be purged)"
        done
    fi
fi

# 2. Purge soft-deleted Cognitive Services accounts (includes Azure OpenAI)
echo ""
echo ">>> Checking for soft-deleted Cognitive Services accounts..."
DELETED_COGNITIVE=$(az cognitiveservices account list-deleted --query "[?contains(id, '$RESOURCE_GROUP')].{name:name, location:location}" -o json 2>/dev/null || echo "[]")

if [ "$DELETED_COGNITIVE" != "[]" ] && [ -n "$DELETED_COGNITIVE" ]; then
    echo "$DELETED_COGNITIVE" | jq -r '.[] | "\(.name)|\(.location)"' | while IFS='|' read -r name location; do
        if [ -n "$name" ] && [ -n "$location" ]; then
            echo "Purging Cognitive Services account: $name in $location"
            az cognitiveservices account purge --name "$name" --resource-group "$RESOURCE_GROUP" --location "$location" 2>/dev/null || echo "  Failed to purge $name"
        fi
    done
else
    echo "  No soft-deleted Cognitive Services accounts found."
fi

# Also list all deleted cognitive services in case RG match didn't work
echo ">>> Checking all soft-deleted Cognitive Services accounts..."
ALL_DELETED_COGNITIVE=$(az cognitiveservices account list-deleted -o json 2>/dev/null || echo "[]")
if [ "$ALL_DELETED_COGNITIVE" != "[]" ] && [ -n "$ALL_DELETED_COGNITIVE" ]; then
    echo "Found soft-deleted Cognitive Services:"
    echo "$ALL_DELETED_COGNITIVE" | jq -r '.[] | "  - \(.name) (location: \(.location))"'
    
    # Try to purge each one
    echo "$ALL_DELETED_COGNITIVE" | jq -r '.[] | "\(.name)|\(.location)"' | while IFS='|' read -r name location; do
        if [ -n "$name" ] && [ -n "$location" ]; then
            echo "Purging Cognitive Services account: $name in $location"
            az cognitiveservices account purge --name "$name" --resource-group "$RESOURCE_GROUP" --location "$location" 2>/dev/null || echo "  Note: May need different resource group for $name"
        fi
    done
fi

# 3. Purge soft-deleted API Management instances
echo ""
echo ">>> Checking for soft-deleted API Management instances..."
DELETED_APIM=$(az apim deletedservice list --query "[?contains(serviceId, '$RESOURCE_GROUP')].{name:name, location:location}" -o json 2>/dev/null || echo "[]")

if [ "$DELETED_APIM" != "[]" ] && [ -n "$DELETED_APIM" ]; then
    echo "$DELETED_APIM" | jq -r '.[] | "\(.name)|\(.location)"' | while IFS='|' read -r name location; do
        if [ -n "$name" ] && [ -n "$location" ]; then
            echo "Purging API Management: $name in $location"
            az apim deletedservice purge --service-name "$name" --location "$location" 2>/dev/null || echo "  Failed to purge $name"
        fi
    done
else
    echo "  No soft-deleted API Management instances found."
fi

# Also list all deleted APIM
echo ">>> Checking all soft-deleted API Management instances..."
ALL_DELETED_APIM=$(az apim deletedservice list -o json 2>/dev/null || echo "[]")
if [ "$ALL_DELETED_APIM" != "[]" ] && [ -n "$ALL_DELETED_APIM" ]; then
    echo "Found soft-deleted API Management instances:"
    echo "$ALL_DELETED_APIM" | jq -r '.[] | "  - \(.name) (location: \(.location))"'
    
    echo "$ALL_DELETED_APIM" | jq -r '.[] | "\(.name)|\(.location)"' | while IFS='|' read -r name location; do
        if [ -n "$name" ] && [ -n "$location" ]; then
            echo "Purging API Management: $name in $location"
            az apim deletedservice purge --service-name "$name" --location "$location" --no-wait 2>/dev/null || echo "  Failed to purge $name"
        fi
    done
fi

# 4. Purge soft-deleted App Configuration stores
echo ""
echo ">>> Checking for soft-deleted App Configuration stores..."
DELETED_APPCONFIG=$(az appconfig list-deleted --query "[?contains(configurationStoreId, '$RESOURCE_GROUP')].{name:name, location:location}" -o json 2>/dev/null || echo "[]")

if [ "$DELETED_APPCONFIG" != "[]" ] && [ -n "$DELETED_APPCONFIG" ]; then
    echo "$DELETED_APPCONFIG" | jq -r '.[] | "\(.name)|\(.location)"' | while IFS='|' read -r name location; do
        if [ -n "$name" ] && [ -n "$location" ]; then
            echo "Purging App Configuration: $name in $location"
            az appconfig purge --name "$name" --location "$location" --yes 2>/dev/null || echo "  Failed to purge $name"
        fi
    done
else
    echo "  No soft-deleted App Configuration stores found."
fi

echo ""
echo "=== Soft-delete purge completed ==="
echo "Note: Some purge operations run asynchronously and may take a few minutes to complete."
