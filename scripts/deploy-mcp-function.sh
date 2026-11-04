#!/bin/bash
#
# deploy-mcp-function.sh - publish the MCP server (challenge-1/mcp-functions) to the Azure Functions app that
# the core template creates, and wire it into Foundry. Replaces the API Management MCP servers.
#
# 1. Builds a zip with the code and Linux/Python 3.12 dependencies (no remote build needed)
# 2. Deploys it to the Flex Consumption function app
# 3. Reads the `mcp_extension` system key and smoke-tests the MCP endpoint (tools/list + tools/call)
# 4. Creates the Foundry project connections machine-data-connection and maintenance-data-connection
#    (RemoteTool, header x-functions-key) - the names the agent scripts already use
# 5. Writes MACHINE_MCP_SERVER_ENDPOINT, MAINTENANCE_MCP_SERVER_ENDPOINT, MCP_AUTH_HEADER, MCP_AUTH_KEY to .env
#
# Usage: scripts/deploy-mcp-function.sh [--env-only]
#   --env-only  skip build/deploy/connections and only restore the .env values (get-keys.sh regenerates .env
#               from scratch, so demo-up.sh calls this to bring them back). The function app and the project
#               connections are permanent and survive demo-down.sh.
#
# Idempotent. Run inside WSL.
#
set -euo pipefail

ENV_ONLY=false
[ "${1:-}" = "--env-only" ] && ENV_ONLY=true

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"
SRC_DIR="$REPO_ROOT/challenge-1/mcp-functions"

if [ -f "$HOME/factory-venv/bin/activate" ]; then
    # shellcheck disable=SC1091
    source "$HOME/factory-venv/bin/activate"
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

APP_NAME=$(az functionapp list -g "$RESOURCE_GROUP" --query "[?contains(name,'-mcp-')].name | [0]" -o tsv)
[ -n "$APP_NAME" ] || { echo "No MCP function app in $RESOURCE_GROUP. Deploy the core template first (scripts/demo-up.sh)."; exit 1; }
HOST=$(az resource show -g "$RESOURCE_GROUP" -n "$APP_NAME" --resource-type Microsoft.Web/sites --query properties.defaultHostName -o tsv)
[ -n "$HOST" ] || { echo "Could not read the host name of $APP_NAME"; exit 1; }
MCP_URL="https://${HOST}/runtime/webhooks/mcp"

set_env() {
    local key="$1" value="$2"
    if grep -q "^${key}=" "$ENV_FILE"; then
        sed -i "s|^${key}=.*|${key}=\"${value}\"|" "$ENV_FILE"
    else
        echo "${key}=\"${value}\"" >> "$ENV_FILE"
    fi
}

if [ "$ENV_ONLY" = false ]; then
echo "==> Building package for $APP_NAME"
BUILD_DIR=$(mktemp -d)
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$SRC_DIR/function_app.py" "$SRC_DIR/host.json" "$SRC_DIR/requirements.txt" "$BUILD_DIR/"
pip install --quiet --target "$BUILD_DIR/.python_packages/lib/site-packages" \
    --platform manylinux2014_x86_64 --implementation cp --python-version 3.12 --only-binary=:all: \
    -r "$SRC_DIR/requirements.txt"
python - "$BUILD_DIR" "$BUILD_DIR/../mcp-function.zip" <<'PY'
import os
import sys
import zipfile

src, target = sys.argv[1], os.path.abspath(sys.argv[2])
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk(src):
        dirs[:] = [d for d in dirs if d != "__pycache__"]
        for name in files:
            full = os.path.join(root, name)
            zf.write(full, os.path.relpath(full, src))
print("zip:", target, f"{os.path.getsize(target) / 1_000_000:.1f} MB")
PY
ZIP_FILE="$BUILD_DIR/../mcp-function.zip"

echo "==> Deploying to $APP_NAME (Flex Consumption)"
# 'az functionapp deploy --type zip' returns HTTP 415 on this Flex app; config-zip works
az functionapp deployment source config-zip -g "$RESOURCE_GROUP" -n "$APP_NAME" --src "$ZIP_FILE" -o none
rm -f "$ZIP_FILE"
fi

echo "==> Waiting for the MCP extension key"
KEY=""
for attempt in $(seq 1 30); do
    KEY=$(az functionapp keys list -g "$RESOURCE_GROUP" -n "$APP_NAME" --query systemKeys.mcp_extension -o tsv 2>/dev/null || true)
    [ -n "$KEY" ] && [ "$KEY" != "None" ] && break
    echo "    attempt $attempt: key not available yet"
    sleep 10
done
[ -n "$KEY" ] && [ "$KEY" != "None" ] || { echo "mcp_extension key not found - did the function host start? (az functionapp logstream / Application Insights)"; exit 1; }

if [ "$ENV_ONLY" = false ]; then
echo "==> Smoke test $MCP_URL"
H=(-H "x-functions-key: $KEY" -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream")
for attempt in $(seq 1 12); do
    LIST=$(curl -s -m 60 "$MCP_URL" "${H[@]}" -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' || true)
    if echo "$LIST" | grep -q "get-machine"; then break; fi
    echo "    attempt $attempt: tools not listed yet (cold start / indexing)"
    sleep 15
done
echo "$LIST" | grep -q "get-machine" || { echo "tools/list failed: $LIST"; exit 1; }
echo "    tools: $(echo "$LIST" | grep -o '"name":"[a-z-]*"' | tr '\n' ' ')"
CALL=$(curl -s -m 90 "$MCP_URL" "${H[@]}" -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get-machine","arguments":{"id":"machine-001"}}}')
echo "$CALL" | grep -q "machine-001" || { echo "tools/call get-machine failed: $CALL"; exit 1; }
echo "    get-machine(machine-001): OK"
CALL=$(curl -s -m 90 "$MCP_URL" "${H[@]}" -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get-threshold","arguments":{"machineType":"banbury_mixer"}}}')
echo "$CALL" | grep -q "power_consumption" || { echo "tools/call get-threshold failed: $CALL"; exit 1; }
echo "    get-threshold(banbury_mixer): OK (all metrics returned)"

echo "==> Foundry project connections"
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)
for NAME in machine-data-connection maintenance-data-connection; do
    BODY=$(jq -n --arg name "$NAME" --arg target "$MCP_URL" --arg key "$KEY" '{
        name: $name,
        type: "Microsoft.MachineLearningServices/workspaces/connections",
        properties: {authType: "CustomKeys", category: "RemoteTool", target: $target, isSharedToAll: true,
                     credentials: {keys: {"x-functions-key": $key}}, metadata: {type: "custom_MCP"}}}')
    CODE=$(curl -s -o /tmp/conn-out.json -w "%{http_code}" -X PUT \
        "https://management.azure.com${AZURE_AI_PROJECT_RESOURCE_ID}/connections/${NAME}?api-version=2025-10-01-preview" \
        -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "$BODY")
    [ "$CODE" = "200" ] || [ "$CODE" = "201" ] || { echo "connection $NAME failed (HTTP $CODE): $(cat /tmp/conn-out.json)"; exit 1; }
    echo "    $NAME: HTTP $CODE"
done
fi

set_env MACHINE_MCP_SERVER_ENDPOINT "$MCP_URL"
set_env MAINTENANCE_MCP_SERVER_ENDPOINT "$MCP_URL"
set_env MCP_AUTH_HEADER "x-functions-key"
set_env MCP_AUTH_KEY "$KEY"
echo "==> Done. .env updated (MACHINE_/MAINTENANCE_MCP_SERVER_ENDPOINT -> $MCP_URL)"
