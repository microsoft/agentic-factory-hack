import json
import logging
import os

import azure.functions as func
from azure.cosmos import CosmosClient
from azure.identity import DefaultAzureCredential

# MCP server on Azure Functions that replaces the two API Management MCP servers of Challenge 1:
#   get-machine(id)                 -> machine document
#   get-threshold(machineType)      -> ALL thresholds of a machine type (one entry per metric)
# Endpoint: https://<function-app>.azurewebsites.net/runtime/webhooks/mcp  (header: x-functions-key = mcp_extension system key)
app = func.FunctionApp()

_cosmos_client: CosmosClient | None = None


def _database():
    # Created lazily and reused across invocations. The function app's managed identity needs the
    # Cosmos DB Built-in Data Contributor role (assigned in the core template).
    global _cosmos_client
    if _cosmos_client is None:
        _cosmos_client = CosmosClient(os.environ["COSMOS_ENDPOINT"], credential=DefaultAzureCredential())
    return _cosmos_client.get_database_client(os.environ.get("COSMOS_DATABASE_NAME", "FactoryOpsDB"))


def _arguments(context: str) -> dict:
    # The MCP trigger passes {"arguments": {...}, ...} as a JSON string
    return json.loads(context).get("arguments", {})


@app.mcp_tool_trigger(
    arg_name="context",
    tool_name="get-machine",
    description="Gets details about a specific machine by id (type, status, maintenance history)",
    tool_properties=json.dumps([
        {"propertyName": "id", "propertyType": "string", "description": "The machine id, e.g. machine-001", "isRequired": True},
    ]),
)
def get_machine(context: str) -> str:
    machine_id = _arguments(context).get("id")
    if not machine_id:
        return json.dumps({"error": "id is required"})

    items = list(_database().get_container_client("Machines").query_items(
        query="SELECT * FROM c WHERE c.id = @id",
        parameters=[{"name": "@id", "value": machine_id}],
        enable_cross_partition_query=True,
    ))
    logging.info("get-machine %s -> %d result(s)", machine_id, len(items))
    # indent=2 mirrors the pretty-printed JSON the former API Management policy returned
    return json.dumps(items[0] if items else {"error": "not found"}, indent=2)


@app.mcp_tool_trigger(
    arg_name="context",
    tool_name="get-threshold",
    description="Gets all metric thresholds (warning and critical) for a machine type, one entry per metric",
    tool_properties=json.dumps([
        {"propertyName": "machineType", "propertyType": "string", "description": "The machine type, e.g. tire_curing_press", "isRequired": True},
    ]),
)
def get_threshold(context: str) -> str:
    machine_type = _arguments(context).get("machineType")
    if not machine_type:
        return json.dumps({"error": "machineType is required"})

    items = list(_database().get_container_client("Thresholds").query_items(
        query="SELECT * FROM c WHERE c.machineType = @machineType",
        parameters=[{"name": "@machineType", "value": machine_type}],
        partition_key=machine_type,
    ))
    logging.info("get-threshold %s -> %d result(s)", machine_type, len(items))
    return json.dumps(items if items else {"error": "not found"}, indent=2)
