#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHALLENGE0_DIR="$SCRIPT_DIR"
REPO_ROOT_DIR="$(cd "$CHALLENGE0_DIR/.." && pwd)"

cd "$CHALLENGE0_DIR"

# Load environment variables from .env in repo root
ENV_FILE="$REPO_ROOT_DIR/.env"
if [ -f "$ENV_FILE" ]; then
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
    echo "✅ Loaded environment variables from $ENV_FILE"
else
    echo "❌ .env file not found at $ENV_FILE. Please run challenge-0/get-keys.sh first."
    exit 1
fi

echo "🚀 Starting data seeding..."

# Install required Python packages
echo "📦 Installing required Python packages..."
pip3 install azure-cosmos --quiet
pip3 install azure-storage-blob 'cffi>=1.17.1' --quiet

# Create Python script to handle the data import
cat > seed_data.py << 'EOF'
import json
import os
from azure.cosmos import CosmosClient, PartitionKey

def load_json_data(file_path):
    """Load data from JSON file"""
    data = []
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = json.load(f)
            # If it's already a list, use it as is
            if isinstance(content, list):
                data = content
            else:
                data = [content]
        print(f"✅ Loaded {len(data)} records from {file_path}")
        return data
    except Exception as e:
        print(f"❌ Error loading {file_path}: {e}")
        return []

def setup_cosmos_db():
    """Set up Cosmos DB database and containers"""
    print("📦 Setting up Cosmos DB...")
    
    # Initialize Cosmos client
    cosmos_client = CosmosClient(os.environ['COSMOS_ENDPOINT'], os.environ['COSMOS_KEY'])
    
    # Create database
    database_name = "FactoryOpsDB"
    try:
        database = cosmos_client.create_database_if_not_exists(id=database_name)
        print(f"✅ Database '{database_name}' ready")
    except Exception as e:
        print(f"❌ Error creating database: {e}")
        return None, None
    
    # Container definitions with partition keys and optional TTL
    containers_config = {
        'Machines': {'partition_key': '/type'},
        'Thresholds': {'partition_key': '/machineType'},
        'Telemetry': {'partition_key': '/machineId', 'ttl': 2592000},  # 30 days TTL
        'KnowledgeBase': {'partition_key': '/machineType'},
        'PartsInventory': {'partition_key': '/category'},
        'Technicians': {'partition_key': '/department'},
        'WorkOrders': {'partition_key': '/status'},
        'MaintenanceHistory': {'partition_key': '/machineId'},
        'MaintenanceWindows': {'partition_key': '/isAvailable'},
        'Suppliers': {'partition_key': '/category'}
    }
    
    container_clients = {}
    for container_name, config in containers_config.items():
        try:
            container = database.create_container_if_not_exists(
                id=container_name,
                partition_key=PartitionKey(path=config['partition_key']),
                default_ttl=config.get('ttl', None)
            )
            container_clients[container_name] = container
            print(f"✅ Container '{container_name}' ready")
        except Exception as e:
            print(f"❌ Error creating container {container_name}: {e}")
    
    return database, container_clients

def seed_cosmos_data(container_clients):
    """Seed data into Cosmos DB containers"""
    print("📦 Seeding Cosmos DB data...")
    
    # Data file mappings (relative to challenge-0 directory)
    data_mappings = {
        'Machines': 'data/machines.json',
        'Thresholds': 'data/thresholds.json',
        'Telemetry': 'data/telemetry-samples.json',
        'KnowledgeBase': 'data/knowledge-base.json',
        'PartsInventory': 'data/parts-inventory.json',
        'Technicians': 'data/technicians.json',
        'WorkOrders': 'data/work-orders.json',
        'MaintenanceHistory': 'data/maintenance-history.json',
        'MaintenanceWindows': 'data/maintenance-windows.json',
        'Suppliers': 'data/suppliers.json'
    }
    
    for container_name, file_path in data_mappings.items():
        if container_name in container_clients:
            data = load_json_data(file_path)
            if data:
                container = container_clients[container_name]
                success_count = 0
                for item in data:
                    try:
                        # Ensure document has an id
                        if 'id' not in item:
                            print(f"⚠️ Item in {container_name} missing 'id' field")
                            continue
                        container.create_item(body=item)
                        success_count += 1
                    except Exception as e:
                        if "Conflict" not in str(e):  # Ignore conflicts (already exists)
                            print(f"⚠️ Error inserting item into {container_name}: {e}")
                print(f"✅ Imported {success_count} items into {container_name}")

def main():
    """Main function to orchestrate the data seeding"""
    # Check required environment variables
    required_vars = ['COSMOS_ENDPOINT', 'COSMOS_KEY']
    missing_vars = [var for var in required_vars if not os.environ.get(var)]
    
    if missing_vars:
        print(f"❌ Missing environment variables: {', '.join(missing_vars)}")
        return
    
    # Set up Cosmos DB
    database, container_clients = setup_cosmos_db()
    if container_clients:
        seed_cosmos_data(container_clients)
    
    print("✅ Data seeding completed successfully!")

if __name__ == "__main__":
    main()
EOF

# Run the Python script
echo "🐍 Running data seeding script..."
python3 seed_data.py

# Clean up
rm seed_data.py

echo "✅ Seeding complete!"

# Upload kb-wiki markdown files to Azure Blob Storage
echo "🚀 Uploading kb-wiki markdown files to Blob Storage..."

# Create Python script to upload markdown files from kb-wiki
cat > seed_blob_wiki.py << 'EOF'
import os
import glob
from azure.storage.blob import BlobServiceClient, ContentSettings
from azure.core.exceptions import ResourceExistsError, AzureError


def short_error(err: Exception) -> str:
    msg = getattr(err, 'message', None) or str(err)
    return msg.splitlines()[0] if msg else err.__class__.__name__

def get_blob_service_client_from_env():
    """Create BlobServiceClient using AZURE_STORAGE_CONNECTION_STRING only."""
    conn = os.environ.get('AZURE_STORAGE_CONNECTION_STRING')
    if not conn:
        raise RuntimeError("Missing AZURE_STORAGE_CONNECTION_STRING in environment.")
    return BlobServiceClient.from_connection_string(conn)

def upload_markdown_files(container_name: str, folder_path: str):
    service_client = get_blob_service_client_from_env()
    container_client = service_client.get_container_client(container_name)
    try:
        container_client.create_container()
        print(f"✅ Created container '{container_name}'")
    except ResourceExistsError:
        print(f"ℹ️ Container '{container_name}' already exists")
    except AzureError as e:
        print(f"⚠️ Could not create container '{container_name}': {short_error(e)}")

    files = glob.glob(os.path.join(folder_path, '*.md'))
    if not files:
        print(f"⚠️ No markdown files found in {folder_path}")
        return

    content_settings = ContentSettings(content_type='text/markdown; charset=utf-8')
    uploaded = 0
    for fpath in files:
        blob_name = os.path.basename(fpath)
        try:
            with open(fpath, 'rb') as f:
                container_client.upload_blob(name=blob_name, data=f, overwrite=True, content_settings=content_settings)
            uploaded += 1
            print(f"✅ Uploaded {blob_name}")
        except Exception as e:
            print(f"❌ Failed to upload {blob_name}: {e}")

    print(f"✅ Completed upload: {uploaded} file(s) to '{container_name}'")

def main():
    # Resolve container and folder (relative to challenge-0 directory)
    container_name = 'machine-wiki' 
    folder_path = os.path.join('data', 'kb-wiki')

    if not os.path.isdir(folder_path):
        raise RuntimeError(f"kb-wiki folder not found at {folder_path}")

    if not os.environ.get('AZURE_STORAGE_CONNECTION_STRING'):
        raise RuntimeError("Missing storage credentials. Set AZURE_STORAGE_CONNECTION_STRING in environment.")

    upload_markdown_files(container_name, folder_path)

if __name__ == '__main__':
    main()
EOF

echo "🐍 Running kb-wiki upload script..."
python3 seed_blob_wiki.py

# Clean up uploader script
rm seed_blob_wiki.py
echo "COSMOS_DATABASE=\"FactoryOpsDB\"" >> ../.env

echo "✅ Blob upload complete!"

# =============================================================================
# Seed API Management (APIM) proxy APIs (Cosmos via Managed Identity)
# =============================================================================

echo "🚀 Seeding API Management (APIM) proxy APIs..."

# APIM seeding requires an Azure CLI login (used by AzureCliCredential).
if ! command -v az >/dev/null 2>&1; then
        echo "⚠️ APIM seeding skipped: Azure CLI (az) not found."
        exit 0
fi

if ! az account show >/dev/null 2>&1; then
        echo "⚠️ APIM seeding skipped: not logged into Azure CLI. Run 'az login' and re-run this script."
        exit 0
fi

# Validate required env vars (they should come from repo-root .env via challenge-0/get-keys.sh)
missing_vars=()
for v in AZURE_SUBSCRIPTION_ID RESOURCE_GROUP APIM_NAME COSMOS_ENDPOINT; do
        if [ -z "${!v}" ]; then
                missing_vars+=("$v")
        fi
done
if [ ${#missing_vars[@]} -ne 0 ]; then
        echo "⚠️ APIM seeding skipped: missing env var(s): ${missing_vars[*]}"
        echo "   Tip: run challenge-0/get-keys.sh to generate the repo-root .env"
        exit 0
fi

echo "📦 Installing required Python packages for APIM..."
pip3 install azure-identity azure-mgmt-apimanagement==4.0.0 --quiet

echo "📝 Generating APIM setup script (Cosmos via Managed Identity)..."
cat > seed_apim_cosmos_mi.py << 'EOF'
import os
from urllib.parse import urlparse

from azure.identity import AzureCliCredential
from azure.mgmt.apimanagement import ApiManagementClient
from azure.mgmt.apimanagement.models import (
        ApiCreateOrUpdateParameter,
        OperationContract,
        ParameterContract,
        Protocol,
        PolicyContract,
        ResponseContract,
)


def require_env(name: str) -> str:
        value = os.environ.get(name)
        if not value:
                raise RuntimeError(f"Missing environment variable: {name}")
        return value


sub_id = require_env("AZURE_SUBSCRIPTION_ID")
rg = require_env("RESOURCE_GROUP")
service = require_env("APIM_NAME")
cosmos_endpoint = require_env("COSMOS_ENDPOINT")  # e.g. https://<account>.documents.azure.com/

# Parse and normalize Cosmos endpoint
parsed = urlparse(cosmos_endpoint)
if not parsed.scheme or not parsed.hostname:
        raise RuntimeError(f"Invalid COSMOS_ENDPOINT: {cosmos_endpoint}")

cosmos_endpoint = f"{parsed.scheme}://{parsed.hostname}/"
# MI resource must be origin without port, slash, or path
resource_attr = f"{parsed.scheme}://{parsed.hostname}"

print(f"ℹ️  Cosmos endpoint: {cosmos_endpoint}")
print(f"ℹ️  MI resource: {resource_attr}")


def policy_query_all(collection: str) -> str:
        return (
                f"""
<policies>
    <inbound>
        <base />
        <set-variable name=\"requestDateString\" value=\"@(DateTime.UtcNow.ToString(&quot;r&quot;))\" />
        <authentication-managed-identity resource=\"{resource_attr}\" output-token-variable-name=\"msi-access-token\" ignore-error=\"false\" />
        <send-request mode=\"new\" response-variable-name=\"cosmosResponse\" timeout=\"30\">
            <set-url>@(\"{cosmos_endpoint}\" + \"dbs/FactoryOpsDB/colls/{collection}/docs\")</set-url>
            <set-method>POST</set-method>
            <set-header name=\"Authorization\" exists-action=\"override\">
                <value>@(\"type=aad&amp;ver=1.0&amp;sig=\" + (string)context.Variables[\"msi-access-token\"])</value>
            </set-header>
            <set-header name=\"x-ms-date\" exists-action=\"override\">
                <value>@(context.Variables.GetValueOrDefault&lt;string&gt;(\"requestDateString\"))</value>
            </set-header>
            <set-header name=\"x-ms-version\" exists-action=\"override\"><value>2018-12-31</value></set-header>
            <set-header name=\"x-ms-documentdb-isquery\" exists-action=\"override\"><value>true</value></set-header>
            <set-header name=\"x-ms-documentdb-query-enablecrosspartition\" exists-action=\"override\"><value>true</value></set-header>
            <set-header name=\"Content-Type\" exists-action=\"override\"><value>application/query+json</value></set-header>
            <set-header name=\"Accept\" exists-action=\"override\"><value>application/json</value></set-header>
            <set-body>@{{
                return JsonConvert.SerializeObject(new {{
                    query = \"SELECT * FROM c\",
                    parameters = new object[0]
                }});
            }}</set-body>
        </send-request>
        <choose>
            <when condition=\"@(((IResponse)context.Variables[&quot;cosmosResponse&quot;]).StatusCode == 200)\">
                <return-response>
                    <set-status code=\"200\" reason=\"OK\" />
                    <set-header name=\"Content-Type\" exists-action=\"override\"><value>application/json</value></set-header>
                    <set-body>@{{
                        var response = ((IResponse)context.Variables[\"cosmosResponse\"]).Body.As&lt;JObject&gt;();
                        return response[\"Documents\"].ToString();
                    }}</set-body>
                </return-response>
            </when>
            <otherwise>
                <return-response>
                    <set-status code=\"502\" reason=\"Cosmos DB Query Failed\" />
                    <set-header name=\"Content-Type\" exists-action=\"override\"><value>application/json</value></set-header>
                    <set-body>@{{ return ((IResponse)context.Variables[\"cosmosResponse\"]).Body.As&lt;string&gt;(); }}</set-body>
                </return-response>
            </otherwise>
        </choose>
    </inbound>
    <backend><base /></backend>
    <outbound><base /></outbound>
    <on-error><base /></on-error>
</policies>
"""
        ).strip()


def policy_query_by_id(collection: str, param_name: str, field: str) -> str:
        return (
                f"""
<policies>
    <inbound>
        <base />
        <set-variable name=\"requestDateString\" value=\"@(DateTime.UtcNow.ToString(&quot;r&quot;))\" />
        <authentication-managed-identity resource=\"{resource_attr}\" output-token-variable-name=\"msi-access-token\" ignore-error=\"false\" />
        <set-variable name=\"{param_name}\" value=\"@(context.Request.MatchedParameters[&quot;{param_name}&quot;])\" />
        <send-request mode=\"new\" response-variable-name=\"cosmosResponse\" timeout=\"30\">
            <set-url>@(\"{cosmos_endpoint}\" + \"dbs/FactoryOpsDB/colls/{collection}/docs\")</set-url>
            <set-method>POST</set-method>
            <set-header name=\"Authorization\" exists-action=\"override\">
                <value>@(\"type=aad&amp;ver=1.0&amp;sig=\" + (string)context.Variables[\"msi-access-token\"])</value>
            </set-header>
            <set-header name=\"x-ms-date\" exists-action=\"override\">
                <value>@(context.Variables.GetValueOrDefault&lt;string&gt;(\"requestDateString\"))</value>
            </set-header>
            <set-header name=\"x-ms-version\" exists-action=\"override\"><value>2018-12-31</value></set-header>
            <set-header name=\"x-ms-documentdb-isquery\" exists-action=\"override\"><value>true</value></set-header>
            <set-header name=\"x-ms-documentdb-query-enablecrosspartition\" exists-action=\"override\"><value>true</value></set-header>
            <set-header name=\"Content-Type\" exists-action=\"override\"><value>application/query+json</value></set-header>
            <set-header name=\"Accept\" exists-action=\"override\"><value>application/json</value></set-header>
            <set-body>@{{
                string v = context.Variables[\"{param_name}\"] as string;
                return JsonConvert.SerializeObject(new {{
                    query = \"SELECT * FROM c WHERE c.{field} = @{param_name}\",
                    parameters = new object[] {{ new {{ name = \"@{param_name}\", value = v }} }}
                }});
            }}</set-body>
        </send-request>
        <choose>
            <when condition=\"@(((IResponse)context.Variables[&quot;cosmosResponse&quot;]).StatusCode == 200)\">
                <return-response>
                    <set-status code=\"200\" reason=\"OK\" />
                    <set-header name=\"Content-Type\" exists-action=\"override\"><value>application/json</value></set-header>
                    <set-body>@{{
                        var response = ((IResponse)context.Variables[\"cosmosResponse\"]).Body.As&lt;JObject&gt;();
                        var docs = response[\"Documents\"] as JArray;
                        return docs.Count > 0 ? docs[0].ToString() : JsonConvert.SerializeObject(new {{ error = \"not found\" }});
                    }}</set-body>
                </return-response>
            </when>
            <otherwise>
                <return-response>
                    <set-status code=\"502\" reason=\"Cosmos DB Query Failed\" />
                    <set-header name=\"Content-Type\" exists-action=\"override\"><value>application/json</value></set-header>
                    <set-body>@{{ return ((IResponse)context.Variables[\"cosmosResponse\"]).Body.As&lt;string&gt;(); }}</set-body>
                </return-response>
            </otherwise>
        </choose>
    </inbound>
    <backend><base /></backend>
    <outbound><base /></outbound>
    <on-error><base /></on-error>
</policies>
"""
        ).strip()


cred = AzureCliCredential()
client = ApiManagementClient(cred, sub_id)


def create_api(api_id: str, display_name: str, description: str, path: str):
        client.api.begin_create_or_update(
                rg,
                service,
                api_id,
                ApiCreateOrUpdateParameter(
                        display_name=display_name,
                        description=description,
                        path=path,
                        protocols=[Protocol.https],
                        subscription_required=True,
                ),
        ).result()


print("📡 Creating Machine API...")
machine_api_id = "machine-api"
create_api(
        machine_api_id,
        display_name="Machine API",
        description="Machines via Cosmos DB (APIM Managed Identity)",
        path="machine",
)

print("📡 Creating List Machines operation...")
client.api_operation.create_or_update(
        rg,
        service,
        machine_api_id,
        "list-machines",
        OperationContract(
                display_name="List Machines",
                description="Retrieves all machines from the factory operations database.",
                method="GET",
                url_template="/",
                template_parameters=[],
                responses=[ResponseContract(status_code=200, description="OK")],
        ),
)
client.api_operation_policy.create_or_update(
        rg,
        service,
        machine_api_id,
        "list-machines",
        "policy",
        parameters=PolicyContract(value=policy_query_all("Machines"), format="rawxml"),
)

print("📡 Creating Get Machine operation...")
client.api_operation.create_or_update(
        rg,
        service,
        machine_api_id,
        "get-machine",
        OperationContract(
                display_name="Get Machine",
                description="Retrieves a specific machine by its unique identifier.",
                method="GET",
                url_template="/{id}",
                template_parameters=[ParameterContract(name="id", type="string", required=True)],
                responses=[
                        ResponseContract(status_code=200, description="OK"),
                        ResponseContract(status_code=404, description="Not Found"),
                ],
        ),
)
client.api_operation_policy.create_or_update(
        rg,
        service,
        machine_api_id,
        "get-machine",
        "policy",
        parameters=PolicyContract(value=policy_query_by_id("Machines", "id", "id"), format="rawxml"),
)

print("✅ APIM Machine API deployed: path=/machine (Cosmos via Managed Identity)")


print("📡 Creating Maintenance API...")
maintenance_api_id = "maintenance-api"
create_api(
        maintenance_api_id,
        display_name="Maintenance API",
        description="Thresholds via Cosmos DB (APIM Managed Identity)",
        path="maintenance",
)

print("📡 Creating List Thresholds operation...")
client.api_operation.create_or_update(
        rg,
        service,
        maintenance_api_id,
        "list-thresholds",
        OperationContract(
                display_name="List Thresholds",
                description="Retrieves all operational thresholds for factory equipment.",
                method="GET",
                url_template="/",
                template_parameters=[],
                responses=[ResponseContract(status_code=200, description="OK")],
        ),
)
client.api_operation_policy.create_or_update(
        rg,
        service,
        maintenance_api_id,
        "list-thresholds",
        "policy",
        parameters=PolicyContract(value=policy_query_all("Thresholds"), format="rawxml"),
)

print("📡 Creating Get Threshold operation...")
client.api_operation.create_or_update(
        rg,
        service,
        maintenance_api_id,
        "get-threshold",
        OperationContract(
                display_name="Get Threshold",
                description="Retrieves operational thresholds for a specific machine type.",
                method="GET",
                url_template="/{machineType}",
                template_parameters=[ParameterContract(name="machineType", type="string", required=True)],
                responses=[
                        ResponseContract(status_code=200, description="OK"),
                        ResponseContract(status_code=404, description="Not Found"),
                ],
        ),
)
client.api_operation_policy.create_or_update(
        rg,
        service,
        maintenance_api_id,
        "get-threshold",
        "policy",
        parameters=PolicyContract(
                value=policy_query_by_id("Thresholds", "machineType", "machineType"),
                format="rawxml",
        ),
)

print("✅ APIM Maintenance API deployed: path=/maintenance (Cosmos via Managed Identity)")
EOF

echo "🐍 Running APIM setup (Managed Identity to Cosmos)..."
python3 seed_apim_cosmos_mi.py

echo "🧹 Cleaning up APIM seeding script..."
rm -f seed_apim_cosmos_mi.py

echo "✅ APIM seeding complete!"
