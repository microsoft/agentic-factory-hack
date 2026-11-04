#!/bin/bash
#
# demo-up.sh - bring the Agentic Factory demo environment up (run inside WSL).
#
# Default (no API Management):
# 1. Ensures the resource group and the CORE deployment (Foundry, models, Cosmos, Storage, monitoring and the
#    Azure Functions MCP server - all permanent and ~free when idle)
# 2. Cleans up leftovers of a previous AI Search instance, then deploys the EPHEMERAL deployment (AI Search)
# 3. Regenerates ../.env (get-keys.sh) and restores the MCP values (deploy-mcp-function.sh --env-only)
# 4. Rebuilds the Foundry IQ knowledge base on the new Search service (create-knowledge-base.sh)
#
# Optional:
#   --with-apim      also deploy API Management (BasicV2, $0.27/hour!) with the Machine/Maintenance REST APIs and
#                    MCP servers, only to SHOW the "API Management as AI gateway" story. The agents do not use it.
#   --seed           (re)seed Cosmos DB + Blob Storage. Not needed normally: the data is permanent. Done
#                    automatically when the core deployment is created for the first time.
#   --redeploy-mcp   rebuild and redeploy the MCP function code and refresh the project connections
#
# Usage: scripts/demo-up.sh [-g <resource-group>] [-l <location>] [--search-location <region>]
#                           [--with-apim] [--seed] [--redeploy-mcp] [--core-only]
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
INFRA_DIR="$REPO_ROOT/challenge-0/infra"

SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-a39be7cd-9ae9-42cf-b42f-dc6e45a82464}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-tire-factory-demo}"
LOCATION="${LOCATION:-swedencentral}"
SEARCH_LOCATION="${SEARCH_LOCATION:-francecentral}"   # swedencentral had no AI Search capacity on 2026-11-04
WITH_APIM=false
SEED=false
REDEPLOY_MCP=false
CORE_ONLY=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        -g|--resource-group) RESOURCE_GROUP="$2"; shift 2 ;;
        -l|--location) LOCATION="$2"; shift 2 ;;
        --search-location) SEARCH_LOCATION="$2"; shift 2 ;;
        --with-apim) WITH_APIM=true; shift ;;
        --seed) SEED=true; shift ;;
        --redeploy-mcp) REDEPLOY_MCP=true; shift ;;
        --core-only) CORE_ONLY=true; shift ;;
        -h|--help) sed -n '2,25p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

log() { echo -e "\n\033[1;34m==> $*\033[0m"; }
warn() { echo -e "\033[1;33m[warn] $*\033[0m"; }

# Python packages are installed into a venv (Ubuntu 24.04 blocks system-wide pip)
if [ -f "$HOME/factory-venv/bin/activate" ]; then
    # shellcheck disable=SC1091
    source "$HOME/factory-venv/bin/activate"
fi
export PATH="$HOME/.aspire/bin:$PATH"

START=$(date +%s)

log "Checking Azure login and subscription"
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login --tenant 05baed4d-e049-45c7-b47d-c99c66fdb589 --use-device-code"; exit 1; }
az account set --subscription "$SUBSCRIPTION_ID"
az account show --query "{subscription:name,user:user.name}" -o tsv

log "Ensuring resource group $RESOURCE_GROUP"
az group create -n "$RESOURCE_GROUP" -l "$LOCATION" --tags purpose=demo owner=alon.havivi@macaw.nl -o none

log "Ensuring CORE deployment (Foundry, models, Cosmos, Storage, monitoring, MCP function app)"
CORE_STATE=$(az deployment group show -g "$RESOURCE_GROUP" -n hack-deployment-core --query properties.provisioningState -o tsv 2>/dev/null || echo "None")
if [ "$CORE_STATE" != "Succeeded" ]; then
    az deployment group create -g "$RESOURCE_GROUP" -n hack-deployment-core \
        --template-file "$INFRA_DIR/azuredeploy.demo-core.json" \
        --parameters location="$LOCATION" -o none
    echo "Core deployed (first time): the data, the MCP function code and the connections need to be created."
    SEED=true
    REDEPLOY_MCP=true
else
    echo "Core already deployed (hack-deployment-core: Succeeded)."
fi

if [ "$CORE_ONLY" = true ]; then
    log "Core only requested - done."
    exit 0
fi

COSMOS_NAME=$(az cosmosdb list -g "$RESOURCE_GROUP" --query "[0].name" -o tsv)
FOUNDRY_NAME=$(az cognitiveservices account list -g "$RESOURCE_GROUP" --query "[?kind=='AIServices'].name | [0]" -o tsv)
FOUNDRY_ID=$(az cognitiveservices account show -n "$FOUNDRY_NAME" -g "$RESOURCE_GROUP" --query id -o tsv)

log "Cleaning up orphaned role assignments from a previous AI Search instance"
ORPHANS=$(az role assignment list --scope "$FOUNDRY_ID" --query "[?principalName=='' && roleDefinitionName=='Cognitive Services User'].id" -o tsv || true)
for id in $ORPHANS; do
    echo "Deleting orphan role assignment $id"
    az role assignment delete --ids "$id" || warn "Could not delete $id"
done

log "Deploying EPHEMERAL resources (AI Search) - this takes about 5-10 minutes"
# A just-deleted Search service name is blocked for a few minutes ("ServiceDeleting"): retry with backoff
for attempt in 1 2 3 4 5 6 7 8; do
    if OUT=$(az deployment group create -g "$RESOURCE_GROUP" -n hack-deployment-ephemeral \
            --template-file "$INFRA_DIR/azuredeploy.demo-ephemeral.json" \
            --parameters location="$LOCATION" searchLocation="$SEARCH_LOCATION" -o none 2>&1); then
        break
    fi
    if echo "$OUT" | grep -q "ServiceDeleting" && [ "$attempt" -lt 8 ]; then
        warn "Search name is still being released (attempt $attempt/8), retrying in 90 s"
        sleep 90
    else
        echo "$OUT"
        exit 1
    fi
done

APIM_PRINCIPAL=""
if [ "$WITH_APIM" = true ]; then
    log "Cleaning up leftovers of a previous API Management instance"
    for pid in $(az cosmosdb sql role assignment list -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" \
            --query "[?ends_with(roleDefinitionId, '00000000-0000-0000-0000-000000000002')].principalId" -o tsv); do
        if ! az ad sp show --id "$pid" >/dev/null 2>&1; then
            RA_ID=$(az cosmosdb sql role assignment list -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --query "[?principalId=='$pid'].name | [0]" -o tsv)
            echo "Deleting orphan Cosmos role assignment $RA_ID (principal $pid)"
            az cosmosdb sql role assignment delete -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --role-assignment-id "$RA_ID" --yes || warn "Could not delete $RA_ID"
        fi
    done
    for row in $(az apim deletedservice list --query "[?contains(name,'-apim-')].[name,location]" -o tsv | tr '\t' '|'); do
        name="${row%%|*}"; loc="${row##*|}"
        echo "Purging soft-deleted $name ($loc)"
        az apim deletedservice purge --service-name "$name" --location "$loc" || warn "Purge of $name failed"
    done

    log "Deploying API Management (optional, 5-30 minutes, \$0.27/hour until demo-down.sh)"
    # Deployment name must NOT contain "hack-deployment": get-keys.sh would pick it (newest match) and find no outputs
    az deployment group create -g "$RESOURCE_GROUP" -n demo-optional-apim \
        --template-file "$INFRA_DIR/azuredeploy.demo-apim.json" \
        --parameters location="$LOCATION" -o none
    APIM_PRINCIPAL=$(az deployment group show -g "$RESOURCE_GROUP" -n demo-optional-apim --query properties.outputs.apimPrincipalId.value -o tsv)

    log "Granting the APIM identity ($APIM_PRINCIPAL) the Cosmos DB Built-in Data Contributor role"
    EXISTING=$(az cosmosdb sql role assignment list -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --query "[?principalId=='$APIM_PRINCIPAL'] | length(@)" -o tsv)
    if [ "$EXISTING" = "0" ]; then
        az cosmosdb sql role assignment create -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" \
            --role-definition-id 00000000-0000-0000-0000-000000000002 \
            --principal-id "$APIM_PRINCIPAL" --scope "/" -o none
    else
        echo "Already assigned."
    fi
fi

log "Generating .env (get-keys.sh)"
bash "$REPO_ROOT/challenge-0/get-keys.sh" --resource-group "$RESOURCE_GROUP" < /dev/null

if [ "$SEED" = true ] || [ "$WITH_APIM" = true ]; then
    # seed-data.sh also creates the APIM REST APIs when APIM exists. Work orders are partitioned by status,
    # so reset them to the seeded statuses first to avoid duplicates.
    [ "$SEED" = true ] || bash "$SCRIPT_DIR/reset-demo-data.sh"
    log "Seeding Cosmos DB, Blob Storage$([ "$WITH_APIM" = true ] && echo ' and APIM REST APIs') (seed-data.sh)"
    bash "$REPO_ROOT/challenge-0/seed-data.sh"
else
    echo "Skipping data seeding (data is permanent; use --seed to force)."
fi

if [ "$REDEPLOY_MCP" = true ]; then
    log "Deploying the MCP function code and Foundry connections"
    bash "$SCRIPT_DIR/deploy-mcp-function.sh"
else
    log "Restoring the MCP values in .env (function app and Foundry connections are permanent)"
    bash "$SCRIPT_DIR/deploy-mcp-function.sh" --env-only
fi

log "Rebuilding the Foundry IQ knowledge base on the new AI Search service"
bash "$SCRIPT_DIR/create-knowledge-base.sh"

if [ "$WITH_APIM" = true ]; then
    log "Creating the optional API Management MCP servers (show-only)"
    bash "$SCRIPT_DIR/create-apim-mcp-servers.sh"
fi

END=$(date +%s)
log "Demo environment is UP (took $(( (END - START) / 60 )) min). Remember to run scripts/demo-down.sh after the demo!"
echo "Next: scripts/start-workflow.sh (and scripts/reset-demo-data.sh before the real demo)"
