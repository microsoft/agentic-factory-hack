#!/bin/bash
#
# create-apim-mcp-servers.sh - expose the Machine and Maintenance APIs as MCP servers in API Management
# (scripted version of Challenge 1, Task 2.2) and write the endpoints into the repo-root .env.
#
# Idempotent. Run inside WSL after demo-up.sh (demo-up.sh calls it as a post-up hook).
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"
API_VERSION="2025-09-01-preview"

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

BASE_URL="https://management.azure.com/subscriptions/${AZURE_SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.ApiManagement/service/${APIM_NAME}"

set_env() {
    local key="$1" value="$2"
    if grep -q "^${key}=" "$ENV_FILE"; then
        sed -i "s|^${key}=.*|${key}=\"${value}\"|" "$ENV_FILE"
    else
        echo "${key}=\"${value}\"" >> "$ENV_FILE"
    fi
}

create_mcp_server() {
    local server_id="$1" display_name="$2" description="$3" backing_api="$4" backing_op="$5" tool_desc="$6"

    echo "==> MCP server '$server_id' (API $backing_api, operation $backing_op)"
    az rest --method put --url "${BASE_URL}/apis/${server_id}?api-version=${API_VERSION}" \
        --headers "If-Match=*" "Content-Type=application/json" \
        --body "{\"properties\":{\"type\":\"mcp\",\"path\":\"${server_id}\",\"displayName\":\"${display_name}\",\"description\":\"${description}\",\"protocols\":[\"https\"]}}" \
        --query "properties.provisioningState" -o tsv >/dev/null

    # The server is created asynchronously; wait until the tool sub-resource can be added
    local op_id="/subscriptions/${AZURE_SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.ApiManagement/service/${APIM_NAME}/apis/${backing_api}/operations/${backing_op}"
    local attempt
    for attempt in 1 2 3 4 5 6 7 8 9 10; do
        if az rest --method put --url "${BASE_URL}/apis/${server_id}/tools/${backing_op}?api-version=${API_VERSION}" \
            --headers "If-Match=*" "Content-Type=application/json" \
            --body "{\"properties\":{\"displayName\":\"${backing_op}\",\"description\":\"${tool_desc}\",\"operationId\":\"${op_id}\"}}" \
            -o none 2>/tmp/mcp-tool-err.log; then
            echo "    tool '${backing_op}' added"
            return 0
        fi
        echo "    waiting for MCP server to be ready (attempt $attempt)..."
        sleep 15
    done
    cat /tmp/mcp-tool-err.log
    return 1
}

create_mcp_server "get-machine-data" "Get Machine Data" "Gets details about a specific machine" \
    "machine-api" "get-machine" "Gets details about a specific machine by id (type, status, maintenance history)"

# The workshop's get-threshold policy returns only the FIRST threshold of a machine type (docs[0]), so agents
# cannot validate machines with several metrics. Patch it to return all thresholds (idempotent, only this operation).
echo "==> Patching get-threshold policy to return all thresholds of a machine type"
POLICY_URL="${BASE_URL}/apis/maintenance-api/operations/get-threshold/policies/policy"
az rest --method get --url "${POLICY_URL}?api-version=2024-05-01&format=rawxml" --query properties.value -o tsv > /tmp/get-threshold-policy.xml
if grep -q 'docs\[0\]\.ToString()' /tmp/get-threshold-policy.xml; then
    sed -i 's/docs\[0\]\.ToString()/docs.ToString()/' /tmp/get-threshold-policy.xml
    jq -Rs '{properties:{format:"rawxml",value:.}}' /tmp/get-threshold-policy.xml > /tmp/get-threshold-policy.json
    az rest --method put --url "${POLICY_URL}?api-version=2024-05-01" --headers "If-Match=*" --body @/tmp/get-threshold-policy.json -o none
    echo "    policy patched"
else
    echo "    already patched (or unexpected policy) - skipped"
fi

create_mcp_server "get-maintenance-data" "Get Maintenance Data" "Gets maintenance data such as thresholds for maintenance alerts" \
    "maintenance-api" "get-threshold" "Gets all metric thresholds (warning and critical) for a machine type, one entry per metric"

set_env MACHINE_MCP_SERVER_ENDPOINT "${APIM_GATEWAY_URL}/get-machine-data/mcp"
set_env MAINTENANCE_MCP_SERVER_ENDPOINT "${APIM_GATEWAY_URL}/get-maintenance-data/mcp"

echo "==> .env updated:"
grep -E '^(MACHINE|MAINTENANCE)_MCP_SERVER_ENDPOINT=' "$ENV_FILE"
