#!/bin/bash
#
# reset-demo-data.sh - bring the Cosmos DB demo data back to the freshly seeded state (run inside WSL).
#
# The Challenge 2-4 agents change data: work orders get new statuses / new work orders are created, and the
# scheduler and parts agents write MaintenanceSchedules, PartsOrders and ChatHistories (agent memory).
# This script:
#   - WorkOrders: deletes all documents and re-inserts the originals from challenge-0/data/work-orders.json
#   - MaintenanceSchedules, PartsOrders, ChatHistories: deletes all documents
# Other containers (Machines, Technicians, PartsInventory, ...) are not touched.
#
# Usage: scripts/reset-demo-data.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ -f "$HOME/factory-venv/bin/activate" ]; then
    # shellcheck disable=SC1091
    source "$HOME/factory-venv/bin/activate"
fi

set -a
# shellcheck disable=SC1091
source "$REPO_ROOT/.env"
set +a

export WORK_ORDERS_FILE="$REPO_ROOT/challenge-0/data/work-orders.json"

python -u - <<'PY'
import json
import os

from azure.cosmos import CosmosClient, exceptions

db = CosmosClient(os.environ["COSMOS_ENDPOINT"], os.environ["COSMOS_KEY"]).get_database_client(
    os.environ.get("COSMOS_DATABASE_NAME", "FactoryOpsDB"))


def clear(container_name):
    try:
        container = db.get_container_client(container_name)
        pk_path = container.read()["partitionKey"]["paths"][0].lstrip("/")
    except exceptions.CosmosResourceNotFoundError:
        print(f"  {container_name}: container does not exist (nothing to clear)")
        return None
    docs = list(container.query_items("SELECT * FROM c", enable_cross_partition_query=True))
    for doc in docs:
        container.delete_item(doc["id"], partition_key=doc.get(pk_path))
    print(f"  {container_name}: deleted {len(docs)} document(s)")
    return container


print("Resetting demo data")
work_orders = clear("WorkOrders")
with open(os.environ["WORK_ORDERS_FILE"], encoding="utf-8") as f:
    originals = json.load(f)
for wo in originals:
    work_orders.upsert_item(wo)
print(f"  WorkOrders: restored {len(originals)} seeded work order(s)")

for name in ("MaintenanceSchedules", "PartsOrders", "ChatHistories"):
    clear(name)
print("Done.")
PY
