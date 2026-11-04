#!/bin/bash
#
# demo-down.sh - stop the billing for the Agentic Factory demo (run inside WSL).
#
# Deletes the hourly-billed services and the role assignments bound to their managed identities:
#   - API Management (only exists after demo-up.sh --with-apim; the soft-deleted instance is purged)
#   - AI Search, but ONLY when it is a paid tier (basic/standard). The Free tier costs nothing, so it is kept
#     (use --delete-search to remove it anyway; demo-up.sh recreates it).
# Kept (no cost when idle): Foundry + models, Cosmos DB serverless, Storage, Log Analytics, App Insights,
# the Azure Functions MCP server (Flex Consumption).
#
# Usage: scripts/demo-down.sh [-g <resource-group>] [-y] [--no-purge] [--delete-search]
#
set -euo pipefail

SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-a39be7cd-9ae9-42cf-b42f-dc6e45a82464}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-tire-factory-demo}"
ASSUME_YES=false
PURGE=true
DELETE_SEARCH=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        -g|--resource-group) RESOURCE_GROUP="$2"; shift 2 ;;
        -y|--yes) ASSUME_YES=true; shift ;;
        --no-purge) PURGE=false; shift ;;
        --delete-search) DELETE_SEARCH=true; shift ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

log() { echo -e "\n\033[1;34m==> $*\033[0m"; }
warn() { echo -e "\033[1;33m[warn] $*\033[0m"; }

az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login --tenant 05baed4d-e049-45c7-b47d-c99c66fdb589 --use-device-code"; exit 1; }
az account set --subscription "$SUBSCRIPTION_ID"

APIM_NAME=$(az apim list -g "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || true)
APIM_LOC=$(az apim list -g "$RESOURCE_GROUP" --query "[0].location" -o tsv 2>/dev/null || true)
SEARCH_NAME=$(az search service list -g "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || true)
SEARCH_SKU=$(az search service list -g "$RESOURCE_GROUP" --query "[0].sku.name" -o tsv 2>/dev/null || true)

if [ -n "$SEARCH_NAME" ] && [ "$SEARCH_SKU" = "free" ] && [ "$DELETE_SEARCH" = false ]; then
    echo "AI Search $SEARCH_NAME is on the Free tier (no cost) - keeping it (use --delete-search to remove)."
    SEARCH_NAME=""
fi

echo "Resource group : $RESOURCE_GROUP"
echo "API Management : ${APIM_NAME:-<none>}"
echo "AI Search      : ${SEARCH_NAME:-<none to delete>}"

if [ -z "$APIM_NAME" ] && [ -z "$SEARCH_NAME" ]; then
    echo "Nothing to delete."
else
    if [ "$ASSUME_YES" = false ]; then
        read -r -p "Delete the resources above? (y/N) " reply
        [[ "$reply" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 0; }
    fi

    COSMOS_NAME=$(az cosmosdb list -g "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || true)
    FOUNDRY_NAME=$(az cognitiveservices account list -g "$RESOURCE_GROUP" --query "[?kind=='AIServices'].name | [0]" -o tsv 2>/dev/null || true)

    if [ -n "$APIM_NAME" ] && [ -n "$COSMOS_NAME" ]; then
        log "Removing the Cosmos DB data role of the APIM identity"
        APIM_PRINCIPAL=$(az apim show -n "$APIM_NAME" -g "$RESOURCE_GROUP" --query identity.principalId -o tsv)
        RA_ID=$(az cosmosdb sql role assignment list -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --query "[?principalId=='$APIM_PRINCIPAL'].name | [0]" -o tsv)
        if [ -n "$RA_ID" ]; then
            az cosmosdb sql role assignment delete -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --role-assignment-id "$RA_ID" --yes || warn "Could not delete Cosmos role assignment"
        fi
    fi

    if [ -n "$SEARCH_NAME" ] && [ -n "$FOUNDRY_NAME" ]; then
        log "Removing the role assignment of the Search identity on the Foundry account"
        SEARCH_PRINCIPAL=$(az search service show -n "$SEARCH_NAME" -g "$RESOURCE_GROUP" --query identity.principalId -o tsv)
        FOUNDRY_ID=$(az cognitiveservices account show -n "$FOUNDRY_NAME" -g "$RESOURCE_GROUP" --query id -o tsv)
        az role assignment delete --assignee "$SEARCH_PRINCIPAL" --scope "$FOUNDRY_ID" || warn "Could not delete Search role assignment"
    fi

    if [ -n "$SEARCH_NAME" ]; then
        log "Deleting AI Search $SEARCH_NAME"
        az search service delete -n "$SEARCH_NAME" -g "$RESOURCE_GROUP" --yes
    fi

    if [ -n "$APIM_NAME" ]; then
        log "Deleting API Management $APIM_NAME (can take several minutes)"
        az apim delete -n "$APIM_NAME" -g "$RESOURCE_GROUP" --yes -o none
        # The CLI can return before the service is really gone; wait (max ~15 min) before purging the soft-deleted copy
        for attempt in $(seq 1 30); do
            az apim show -n "$APIM_NAME" -g "$RESOURCE_GROUP" -o none 2>/dev/null || break
            echo "    still deleting (check $attempt)..."
            sleep 30
        done
        if [ "$PURGE" = true ]; then
            log "Purging soft-deleted API Management $APIM_NAME"
            az apim deletedservice purge --service-name "$APIM_NAME" --location "$APIM_LOC" || warn "Purge failed (it may not be soft-deleted)"
        fi
    fi
fi

log "Resources left in $RESOURCE_GROUP"
az resource list -g "$RESOURCE_GROUP" --query "[].{name:name,type:type}" -o table
echo
APIM_LEFT=$(az resource list -g "$RESOURCE_GROUP" --query "[?type=='Microsoft.ApiManagement/service'] | length(@)" -o tsv)
SEARCH_PAID_LEFT=$(az search service list -g "$RESOURCE_GROUP" --query "[?sku.name!='free'] | length(@)" -o tsv 2>/dev/null || echo 0)
echo "Hourly-billed services remaining: API Management=$APIM_LEFT, paid AI Search=$SEARCH_PAID_LEFT (Free Search costs nothing)"
