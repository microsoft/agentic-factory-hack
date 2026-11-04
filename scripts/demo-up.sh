#!/bin/bash
#
# demo-up.sh - bring the Agentic Factory demo environment up (run inside WSL).
#
# 1. Ensures the resource group and the CORE deployment (Foundry, models, Cosmos, Storage, monitoring)
# 2. Cleans up leftovers from a previous APIM / AI Search instance (orphan role assignments, soft-deleted APIM)
# 3. Deploys the EPHEMERAL deployment (API Management + AI Search + role assignments + Foundry->Search connection)
# 4. Grants the new APIM managed identity access to Cosmos DB data
# 5. Regenerates ../.env (get-keys.sh) and seeds data + APIM proxy APIs (seed-data.sh)
# 6. Runs optional post-up hooks if present (MCP servers, knowledge base)
#
# Usage: scripts/demo-up.sh [-g <resource-group>] [-l <location>] [--skip-seed] [--core-only]
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
INFRA_DIR="$REPO_ROOT/challenge-0/infra"

SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-a39be7cd-9ae9-42cf-b42f-dc6e45a82464}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-tire-factory-demo}"
LOCATION="${LOCATION:-swedencentral}"
SKIP_SEED=false
CORE_ONLY=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        -g|--resource-group) RESOURCE_GROUP="$2"; shift 2 ;;
        -l|--location) LOCATION="$2"; shift 2 ;;
        --skip-seed) SKIP_SEED=true; shift ;;
        --core-only) CORE_ONLY=true; shift ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
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

log "Ensuring CORE deployment (Foundry, models, Cosmos, Storage, monitoring)"
CORE_STATE=$(az deployment group show -g "$RESOURCE_GROUP" -n hack-deployment-core --query properties.provisioningState -o tsv 2>/dev/null || echo "None")
if [ "$CORE_STATE" != "Succeeded" ]; then
    az deployment group create -g "$RESOURCE_GROUP" -n hack-deployment-core \
        --template-file "$INFRA_DIR/azuredeploy.demo-core.json" \
        --parameters location="$LOCATION" -o none
    echo "Core deployed."
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

log "Cleaning up orphaned Cosmos DB data role assignments from a previous APIM instance"
for pid in $(az cosmosdb sql role assignment list -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" \
        --query "[?ends_with(roleDefinitionId, '00000000-0000-0000-0000-000000000002')].principalId" -o tsv); do
    if ! az ad sp show --id "$pid" >/dev/null 2>&1; then
        RA_ID=$(az cosmosdb sql role assignment list -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --query "[?principalId=='$pid'].name | [0]" -o tsv)
        echo "Deleting orphan Cosmos role assignment $RA_ID (principal $pid)"
        az cosmosdb sql role assignment delete -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --role-assignment-id "$RA_ID" --yes || warn "Could not delete $RA_ID"
    fi
done

log "Purging soft-deleted API Management instance with the same name (if any)"
for row in $(az apim deletedservice list --query "[?contains(name,'-apim-')].[name,location]" -o tsv | tr '\t' '|'); do
    name="${row%%|*}"; loc="${row##*|}"
    echo "Purging $name ($loc)"
    az apim deletedservice purge --service-name "$name" --location "$loc" || warn "Purge of $name failed"
done

log "Deploying EPHEMERAL resources (API Management + AI Search) - this takes 5-30 minutes"
az deployment group create -g "$RESOURCE_GROUP" -n hack-deployment-ephemeral \
    --template-file "$INFRA_DIR/azuredeploy.demo-ephemeral.json" \
    --parameters location="$LOCATION" -o none

APIM_NAME=$(az deployment group show -g "$RESOURCE_GROUP" -n hack-deployment-ephemeral --query properties.outputs.apiManagementName.value -o tsv)
APIM_PRINCIPAL=$(az deployment group show -g "$RESOURCE_GROUP" -n hack-deployment-ephemeral --query properties.outputs.apimPrincipalId.value -o tsv)

log "Granting APIM identity ($APIM_PRINCIPAL) the Cosmos DB Built-in Data Contributor role"
EXISTING=$(az cosmosdb sql role assignment list -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" --query "[?principalId=='$APIM_PRINCIPAL'] | length(@)" -o tsv)
if [ "$EXISTING" = "0" ]; then
    az cosmosdb sql role assignment create -a "$COSMOS_NAME" -g "$RESOURCE_GROUP" \
        --role-definition-id 00000000-0000-0000-0000-000000000002 \
        --principal-id "$APIM_PRINCIPAL" --scope "/" -o none
    echo "Role assigned."
else
    echo "Already assigned."
fi

log "Generating .env (get-keys.sh)"
bash "$REPO_ROOT/challenge-0/get-keys.sh" --resource-group "$RESOURCE_GROUP" < /dev/null

if [ "$SKIP_SEED" = false ]; then
    log "Seeding Cosmos DB, Blob Storage and APIM proxy APIs (seed-data.sh)"
    bash "$REPO_ROOT/challenge-0/seed-data.sh"
else
    warn "Skipping data seeding (--skip-seed)"
fi

log "Post-up hooks (optional, added in later phases)"
for hook in "$SCRIPT_DIR/create-apim-mcp-servers.sh" "$SCRIPT_DIR/create-knowledge-base.sh"; do
    if [ -f "$hook" ]; then
        echo "Running $(basename "$hook")"
        bash "$hook"
    else
        echo "No hook: $(basename "$hook") (skipped)"
    fi
done

END=$(date +%s)
log "Demo environment is UP (took $(( (END - START) / 60 )) min). Remember to run scripts/demo-down.sh after the demo!"
echo "APIM: $APIM_NAME"
echo "Next: export \$(grep -v '^#' .env | xargs) && cd challenge-4/agent-workflow && aspire run"
