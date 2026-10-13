#!/usr/bin/env pwsh
# Copyright (c) Microsoft Corporation.
# SPDX-License-Identifier: MIT
#Requires -Version 7.0

<#
.SYNOPSIS
    Seed factory data into Cosmos DB, Blob Storage, and API Management.
.DESCRIPTION
    Load the repository-root .env file, install Python SDK dependencies, seed
    FactoryOpsDB, upload the machine wiki, and deploy APIM proxy APIs.
    APIM setup is skipped with a warning when Azure CLI login or settings are missing.
.PARAMETER PythonCommand
    Python 3 executable to use for both pip installation and SDK scripts.
.EXAMPLE
    .\challenge-0\seed-data.ps1
.EXAMPLE
    .\challenge-0\seed-data.ps1 -PythonCommand python3
.NOTES
    Requires PowerShell 7+, Python 3 with pip, and the credentials from get-keys.sh.
    Azure CLI is required only for API Management setup.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateNotNullOrEmpty()]
    [string]$PythonCommand = 'python'
)

$ErrorActionPreference = 'Stop'
# Native failures are checked explicitly, including the optional Azure CLI login probe.
$PSNativeCommandUseErrorActionPreference = $false

#region Functions

function Import-SeedEnvironment {
    <#
    .SYNOPSIS
        Load literal KEY=VALUE settings without executing shell code.
    .OUTPUTS
        None.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ".env file not found at $Path. Run challenge-0/get-keys.sh first."
    }

    $LineNumber = 0
    foreach ($Line in Get-Content -LiteralPath $Path) {
        $LineNumber++
        $Entry = $Line.Trim()
        if (-not $Entry -or $Entry.StartsWith('#')) {
            continue
        }
        if ($Entry -notmatch '^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') {
            throw "Invalid environment assignment at ${Path}:$LineNumber."
        }

        $Name = $Matches[1]
        $Value = $Matches[2].Trim()
        if ($Value.StartsWith('"') -or $Value.StartsWith("'")) {
            $Quote = $Value.Substring(0, 1)
            if ($Value.Length -lt 2 -or -not $Value.EndsWith($Quote)) {
                throw "Unterminated quoted value at ${Path}:$LineNumber."
            }
            $Value = $Value.Substring(1, $Value.Length - 2)
        }
        [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
    }
    Write-Host "Loaded environment variables from $Path" -ForegroundColor Green
}

function Invoke-SeedPython {
    <#
    .SYNOPSIS
        Run an embedded Python script and always remove its temporary file.
    .OUTPUTS
        None.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    $TemporaryFile = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($TemporaryFile, $Source, [Text.UTF8Encoding]::new($false))
        & $Command $TemporaryFile
        if ($LASTEXITCODE -ne 0) {
            throw "Python seeding failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $TemporaryFile -Force
    }
}

function Invoke-FactoryDataSeed {
    <#
    .SYNOPSIS
        Orchestrate factory data seeding using the same Python SDKs as seed-data.sh.
    .OUTPUTS
        None.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command
    )

    $EnvFile = Join-Path (Split-Path -Parent $PSScriptRoot) '.env'
    Import-SeedEnvironment -Path $EnvFile
    if (-not (Get-Command -Name $Command -CommandType Application -ErrorAction SilentlyContinue)) {
        throw "Python executable '$Command' not found. Install Python 3 or specify -PythonCommand."
    }

    foreach ($Name in @('COSMOS_ENDPOINT', 'COSMOS_KEY', 'AZURE_STORAGE_CONNECTION_STRING')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($Name))) {
            throw "Missing environment variable: $Name."
        }
    }

    Push-Location -LiteralPath $PSScriptRoot
    try {
        Write-Host 'Installing required Python packages...'
        & $Command -m pip install azure-cosmos azure-storage-blob 'cffi>=1.17.1' --quiet
        if ($LASTEXITCODE -ne 0) {
            throw "Python package installation failed with exit code $LASTEXITCODE."
        }
        & $Command -c 'import _cffi_backend; import azure.storage.blob'
        if ($LASTEXITCODE -ne 0) {
            throw 'Python dependency validation failed. Re-run with a supported Python installation.'
        }

        Write-Host 'Seeding Cosmos DB data...'
        Invoke-SeedPython -Command $Command -Source @'
import json
import os
from pathlib import Path

from azure.cosmos import CosmosClient, PartitionKey
from azure.cosmos.exceptions import CosmosResourceExistsError


containers_config = {
    "Machines": ("/type", "machines.json", None),
    "Thresholds": ("/machineType", "thresholds.json", None),
    "Telemetry": ("/machineId", "telemetry-samples.json", 2592000),
    "KnowledgeBase": ("/machineType", "knowledge-base.json", None),
    "PartsInventory": ("/category", "parts-inventory.json", None),
    "Technicians": ("/department", "technicians.json", None),
    "WorkOrders": ("/status", "work-orders.json", None),
    "MaintenanceHistory": ("/machineId", "maintenance-history.json", None),
    "MaintenanceWindows": ("/isAvailable", "maintenance-windows.json", None),
    "Suppliers": ("/category", "suppliers.json", None),
}

with CosmosClient(os.environ["COSMOS_ENDPOINT"], os.environ["COSMOS_KEY"]) as client:
    database = client.create_database_if_not_exists(id="FactoryOpsDB")
    print("Database 'FactoryOpsDB' ready")
    containers = {}
    for name, (partition_key, filename, ttl) in containers_config.items():
        containers[name] = database.create_container_if_not_exists(
            id=name, partition_key=PartitionKey(path=partition_key), default_ttl=ttl
        )
        print(f"Container '{name}' ready")
    for name, (_, filename, _) in containers_config.items():
        container = containers[name]
        file_path = Path("data") / filename
        with file_path.open(encoding="utf-8") as source:
            content = json.load(source)
        items = content if isinstance(content, list) else [content]
        print(f"Loaded {len(items)} records from {file_path}")
        imported = 0
        for item in items:
            if "id" not in item:
                raise ValueError(f"Item in {name} missing 'id' field")
            try:
                container.create_item(body=item)
                imported += 1
            except CosmosResourceExistsError:
                pass  # Existing items are left unchanged when re-running the script.
        print(f"Imported {imported} items into {name}")
print("Cosmos DB seeding complete!")
'@

        Write-Host 'Uploading kb-wiki markdown files to Blob Storage...'
        Invoke-SeedPython -Command $Command -Source @'
import os
from pathlib import Path

from azure.core.exceptions import ResourceExistsError
from azure.storage.blob import BlobServiceClient, ContentSettings


folder = Path("data") / "kb-wiki"
if not folder.is_dir():
    raise RuntimeError(f"kb-wiki folder not found at {folder}")

with BlobServiceClient.from_connection_string(
    os.environ["AZURE_STORAGE_CONNECTION_STRING"]
) as service:
    container = service.get_container_client("machine-wiki")
    try:
        container.create_container()
        print("Created container 'machine-wiki'")
    except ResourceExistsError:
        print("Container 'machine-wiki' already exists")

    files = list(folder.glob("*.md"))
    if not files:
        raise RuntimeError(f"No markdown files found in {folder}")
    settings = ContentSettings(content_type="text/markdown; charset=utf-8")
    for file_path in files:
        with file_path.open("rb") as source:
            container.upload_blob(
                name=file_path.name, data=source, overwrite=True, content_settings=settings
            )
        print(f"Uploaded {file_path.name}")
    print(f"Completed upload: {len(files)} file(s) to 'machine-wiki'")
'@

        # Keep the database setting written by the Bash script, without duplicate entries.
        $EnvContent = [IO.File]::ReadAllText($EnvFile)
        $DatabaseAssignment = 'COSMOS_DATABASE="FactoryOpsDB"'
        if ($EnvContent -match '(?m)^(?:export\s+)?COSMOS_DATABASE\s*=') {
            $EnvContent = $EnvContent -replace '(?m)^(?:export\s+)?COSMOS_DATABASE\s*=[^\r\n]*', $DatabaseAssignment
        }
        else {
            if ($EnvContent.Length -gt 0 -and -not $EnvContent.EndsWith("`n")) {
                $EnvContent += [Environment]::NewLine
            }
            $EnvContent += $DatabaseAssignment + [Environment]::NewLine
        }
        [IO.File]::WriteAllText($EnvFile, $EnvContent, [Text.UTF8Encoding]::new($false))

        Write-Host 'Seeding API Management proxy APIs...'
        if (-not (Get-Command -Name az -CommandType Application -ErrorAction SilentlyContinue)) {
            Write-Warning 'APIM seeding skipped: Azure CLI (az) not found.'
            return
        }
        & az account show --output none 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "APIM seeding skipped: not logged into Azure CLI. Run 'az login' and re-run this script."
            return
        }

        $MissingVariables = @(
            foreach ($Name in @('AZURE_SUBSCRIPTION_ID', 'RESOURCE_GROUP', 'APIM_NAME', 'COSMOS_ENDPOINT')) {
                if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($Name))) {
                    $Name
                }
            }
        )
        if ($MissingVariables.Count -gt 0) {
            Write-Warning "APIM seeding skipped: missing env var(s): $($MissingVariables -join ', '). Run challenge-0/get-keys.sh."
            return
        }

        Write-Host 'Installing required Python packages for APIM...'
        & $Command -m pip install azure-identity azure-mgmt-apimanagement==4.0.0 --quiet
        if ($LASTEXITCODE -ne 0) {
            throw "APIM package installation failed with exit code $LASTEXITCODE."
        }

        Invoke-SeedPython -Command $Command -Source @'
import os
from urllib.parse import urlparse

from azure.identity import AzureCliCredential
from azure.mgmt.apimanagement import ApiManagementClient
from azure.mgmt.apimanagement.models import (
    ApiCreateOrUpdateParameter,
    OperationContract,
    ParameterContract,
    PolicyContract,
    Protocol,
    ResponseContract,
)


parsed = urlparse(os.environ["COSMOS_ENDPOINT"])
if not parsed.scheme or not parsed.hostname:
    raise RuntimeError("Invalid COSMOS_ENDPOINT")
cosmos_endpoint = f"{parsed.scheme}://{parsed.hostname}/"
resource_attr = f"{parsed.scheme}://{parsed.hostname}"
print(f"Cosmos endpoint: {cosmos_endpoint}")
print(f"MI resource: {resource_attr}")


def policy_query_all(collection: str) -> str:
    return f"""
<policies>
    <inbound>
        <base />
        <set-variable name="requestDateString" value="@(DateTime.UtcNow.ToString(&quot;r&quot;))" />
        <authentication-managed-identity resource="{resource_attr}" output-token-variable-name="msi-access-token" ignore-error="false" />
        <send-request mode="new" response-variable-name="cosmosResponse" timeout="30">
            <set-url>@("{cosmos_endpoint}" + "dbs/FactoryOpsDB/colls/{collection}/docs")</set-url>
            <set-method>POST</set-method>
            <set-header name="Authorization" exists-action="override">
                <value>@("type=aad&amp;ver=1.0&amp;sig=" + (string)context.Variables["msi-access-token"])</value>
            </set-header>
            <set-header name="x-ms-date" exists-action="override">
                <value>@(context.Variables.GetValueOrDefault&lt;string&gt;("requestDateString"))</value>
            </set-header>
            <set-header name="x-ms-version" exists-action="override"><value>2018-12-31</value></set-header>
            <set-header name="x-ms-documentdb-isquery" exists-action="override"><value>true</value></set-header>
            <set-header name="x-ms-documentdb-query-enablecrosspartition" exists-action="override"><value>true</value></set-header>
            <set-header name="Content-Type" exists-action="override"><value>application/query+json</value></set-header>
            <set-header name="Accept" exists-action="override"><value>application/json</value></set-header>
            <set-body>@{{
                return JsonConvert.SerializeObject(new {{
                    query = "SELECT * FROM c",
                    parameters = new object[0]
                }});
            }}</set-body>
        </send-request>
        <choose>
            <when condition="@(((IResponse)context.Variables[&quot;cosmosResponse&quot;]).StatusCode == 200)">
                <return-response>
                    <set-status code="200" reason="OK" />
                    <set-header name="Content-Type" exists-action="override"><value>application/json</value></set-header>
                    <set-body>@{{
                        var response = ((IResponse)context.Variables["cosmosResponse"]).Body.As&lt;JObject&gt;();
                        return response["Documents"].ToString();
                    }}</set-body>
                </return-response>
            </when>
            <otherwise>
                <return-response>
                    <set-status code="502" reason="Cosmos DB Query Failed" />
                    <set-header name="Content-Type" exists-action="override"><value>application/json</value></set-header>
                    <set-body>@{{ return ((IResponse)context.Variables["cosmosResponse"]).Body.As&lt;string&gt;(); }}</set-body>
                </return-response>
            </otherwise>
        </choose>
    </inbound>
    <backend><base /></backend>
    <outbound><base /></outbound>
    <on-error><base /></on-error>
</policies>
""".strip()


def policy_query_by_id(collection: str, param_name: str, field: str) -> str:
    return f"""
<policies>
    <inbound>
        <base />
        <set-variable name="requestDateString" value="@(DateTime.UtcNow.ToString(&quot;r&quot;))" />
        <authentication-managed-identity resource="{resource_attr}" output-token-variable-name="msi-access-token" ignore-error="false" />
        <set-variable name="{param_name}" value="@(context.Request.MatchedParameters[&quot;{param_name}&quot;])" />
        <send-request mode="new" response-variable-name="cosmosResponse" timeout="30">
            <set-url>@("{cosmos_endpoint}" + "dbs/FactoryOpsDB/colls/{collection}/docs")</set-url>
            <set-method>POST</set-method>
            <set-header name="Authorization" exists-action="override">
                <value>@("type=aad&amp;ver=1.0&amp;sig=" + (string)context.Variables["msi-access-token"])</value>
            </set-header>
            <set-header name="x-ms-date" exists-action="override">
                <value>@(context.Variables.GetValueOrDefault&lt;string&gt;("requestDateString"))</value>
            </set-header>
            <set-header name="x-ms-version" exists-action="override"><value>2018-12-31</value></set-header>
            <set-header name="x-ms-documentdb-isquery" exists-action="override"><value>true</value></set-header>
            <set-header name="x-ms-documentdb-query-enablecrosspartition" exists-action="override"><value>true</value></set-header>
            <set-header name="Content-Type" exists-action="override"><value>application/query+json</value></set-header>
            <set-header name="Accept" exists-action="override"><value>application/json</value></set-header>
            <set-body>@{{
                string v = context.Variables["{param_name}"] as string;
                return JsonConvert.SerializeObject(new {{
                    query = "SELECT * FROM c WHERE c.{field} = @{param_name}",
                    parameters = new object[] {{ new {{ name = "@{param_name}", value = v }} }}
                }});
            }}</set-body>
        </send-request>
        <choose>
            <when condition="@(((IResponse)context.Variables[&quot;cosmosResponse&quot;]).StatusCode == 200)">
                <return-response>
                    <set-status code="200" reason="OK" />
                    <set-header name="Content-Type" exists-action="override"><value>application/json</value></set-header>
                    <set-body>@{{
                        var response = ((IResponse)context.Variables["cosmosResponse"]).Body.As&lt;JObject&gt;();
                        var docs = response["Documents"] as JArray;
                        return docs.Count > 0 ? docs[0].ToString() : JsonConvert.SerializeObject(new {{ error = "not found" }});
                    }}</set-body>
                </return-response>
            </when>
            <otherwise>
                <return-response>
                    <set-status code="502" reason="Cosmos DB Query Failed" />
                    <set-header name="Content-Type" exists-action="override"><value>application/json</value></set-header>
                    <set-body>@{{ return ((IResponse)context.Variables["cosmosResponse"]).Body.As&lt;string&gt;(); }}</set-body>
                </return-response>
            </otherwise>
        </choose>
    </inbound>
    <backend><base /></backend>
    <outbound><base /></outbound>
    <on-error><base /></on-error>
</policies>
""".strip()


rg = os.environ["RESOURCE_GROUP"]
service = os.environ["APIM_NAME"]
with AzureCliCredential() as credential, ApiManagementClient(
    credential, os.environ["AZURE_SUBSCRIPTION_ID"]
) as client:
    for api_id, display_name, description, path, collection, operations in [
        (
            "machine-api", "Machine API",
            "Machines via Cosmos DB (APIM Managed Identity)", "machine", "Machines",
            [
                ("list-machines", "List Machines",
                 "Retrieves all machines from the factory operations database.", None, None),
                ("get-machine", "Get Machine",
                 "Retrieves a specific machine by its unique identifier.", "id", "id"),
            ],
        ),
        (
            "maintenance-api", "Maintenance API",
            "Thresholds via Cosmos DB (APIM Managed Identity)", "maintenance", "Thresholds",
            [
                ("list-thresholds", "List Thresholds",
                 "Retrieves all operational thresholds for factory equipment.", None, None),
                ("get-threshold", "Get Threshold",
                 "Retrieves operational thresholds for a specific machine type.",
                 "machineType", "machineType"),
            ],
        ),
    ]:
        print(f"Creating {display_name}...")
        client.api.begin_create_or_update(
            rg, service, api_id,
            ApiCreateOrUpdateParameter(
                display_name=display_name, description=description, path=path,
                protocols=[Protocol.https], subscription_required=True,
            ),
        ).result()
        for operation_id, operation_name, operation_description, parameter, field in operations:
            print(f"Creating {operation_name} operation...")
            responses = [ResponseContract(status_code=200, description="OK")]
            parameters = []
            if parameter:
                parameters = [ParameterContract(name=parameter, type="string", required=True)]
                responses.append(ResponseContract(status_code=404, description="Not Found"))
            client.api_operation.create_or_update(
                rg, service, api_id, operation_id,
                OperationContract(
                    display_name=operation_name, description=operation_description,
                    method="GET", url_template=f"/{{{parameter}}}" if parameter else "/",
                    template_parameters=parameters, responses=responses,
                ),
            )
            policy = (
                policy_query_by_id(collection, parameter, field)
                if parameter else policy_query_all(collection)
            )
            client.api_operation_policy.create_or_update(
                rg, service, api_id, operation_id, "policy",
                parameters=PolicyContract(value=policy, format="rawxml"),
            )
        print(f"APIM {display_name} deployed: path=/{path} (Cosmos via Managed Identity)")
print("APIM seeding complete!")
'@
    }
    finally {
        Pop-Location
    }
}

#endregion Functions

#region Main Execution

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-FactoryDataSeed -Command $PythonCommand
        Write-Host 'Data seeding complete. Review any APIM skip warnings above.' -ForegroundColor Green
        exit 0
    }
    catch {
        Write-Error -Message "Data seeding failed: $($_.Exception.Message)" -ErrorAction Continue
        exit 1
    }
}

#endregion Main Execution
