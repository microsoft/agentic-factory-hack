import asyncio
import os

from agent_framework.openai import OpenAIChatClient
from azure.cosmos import CosmosClient
from azure.identity.aio import AzureCliCredential
from dotenv import load_dotenv

# TODO: add HostedMCPTool import

load_dotenv(override=True)

# Configuration
project_endpoint = os.environ.get("AZURE_AI_PROJECT_ENDPOINT")

# Initialize Cosmos DB clients globally for function tools
cosmos_endpoint = os.environ.get("COSMOS_ENDPOINT")
cosmos_key = os.environ.get("COSMOS_KEY")
cosmos_client = CosmosClient(cosmos_endpoint, cosmos_key)
database = cosmos_client.get_database_client("FactoryOpsDB")
thresholds_container = database.get_container_client("Thresholds")
machines_container = database.get_container_client("Machines")
telemetry_container = database.get_container_client("Telemetry")

# MCP configuration
# TODO: add subscription key and MCP endpoint


def get_thresholds(machine_type: str) -> list:
    """Get all thresholds for a machine type from Cosmos DB"""
    try:
        query = f"SELECT * FROM c WHERE c.machineType = '{machine_type}'"
        items = list(thresholds_container.query_items(
            query=query,
            enable_cross_partition_query=True
        ))
        return items
    except Exception as e:
        return [{"error": str(e)}]


def get_machine_data(machine_id: str) -> dict:
    """Get machine data from Cosmos DB"""
    try:
        query = f"SELECT * FROM c WHERE c.id = '{machine_id}'"
        items = list(machines_container.query_items(
            query=query,
            enable_cross_partition_query=True
        ))
        return items[0] if items else {"error": f"Machine {machine_id} not found"}
    except Exception as e:
        return {"error": str(e)}


def get_equipment_health_snapshot() -> list:
    """Rank equipment using recent telemetry, thresholds, trends, and known criticality."""
    try:
        machines = list(machines_container.query_items(
            query="SELECT * FROM c",
            enable_cross_partition_query=True
        ))
        thresholds = list(thresholds_container.query_items(
            query="SELECT * FROM c",
            enable_cross_partition_query=True
        ))
        thresholds_by_type = {}
        for threshold in thresholds:
            thresholds_by_type.setdefault(threshold["machineType"], {})[
                threshold["metric"]] = threshold

        assessments = []
        for machine in machines:
            machine_id = machine["id"]
            readings = list(telemetry_container.query_items(
                query="""SELECT TOP 100 * FROM c
                         WHERE c.machineId = @machineId
                         ORDER BY c.timestamp DESC""",
                parameters=[{"name": "@machineId", "value": machine_id}],
                partition_key=machine_id
            ))
            criticality = str(machine.get("productionCriticality", "unknown")).lower()
            if not readings:
                assessments.append({
                    "machineId": machine_id,
                    "machineName": machine.get("name", machine_id),
                    "priority": "unknown",
                    "productionCriticality": criticality,
                    "dataStatus": "no_recent_telemetry",
                    "alerts": []
                })
                continue

            current = readings[0]
            previous_metrics = readings[1].get("metrics", {}) if len(readings) > 1 else {}
            metric_thresholds = thresholds_by_type.get(machine.get("type"), {})
            alerts = []
            for metric, value in current.get("metrics", {}).items():
                threshold = metric_thresholds.get(metric)
                if not threshold:
                    continue

                normal_range = threshold.get("normalRange", {})
                warning = threshold.get("warningThreshold")
                critical = threshold.get("criticalThreshold")
                low_is_bad = warning is not None and warning < normal_range.get("min", warning)
                if low_is_bad:
                    severity = (
                        "critical" if critical is not None and value <= critical else
                        "warning" if warning is not None and value <= warning else
                        "watch" if value < normal_range.get("min", value) else
                        "normal"
                    )
                else:
                    severity = (
                        "critical" if critical is not None and value >= critical else
                        "warning" if warning is not None and value >= warning else
                        "watch" if value > normal_range.get("max", value) else
                        "normal"
                    )

                if severity == "normal":
                    continue

                previous_value = previous_metrics.get(metric)
                baseline_values = [
                    reading["metrics"][metric]
                    for reading in readings[1:]
                    if reading.get("status") == "normal" and metric in reading.get("metrics", {})
                ]
                baseline = (
                    round(sum(baseline_values) / len(baseline_values), 3)
                    if baseline_values else None
                )
                change_percent = (
                    round((value - baseline) / abs(baseline) * 100, 1)
                    if baseline not in (None, 0) else None
                )
                worsening = (
                    previous_value is not None and
                    ((value < previous_value) if low_is_bad else (value > previous_value))
                )
                alerts.append({
                    "metric": metric,
                    "value": value,
                    "unit": threshold.get("unit"),
                    "severity": severity,
                    "baseline": baseline,
                    "baselineChangePercent": change_percent,
                    "previousValue": previous_value,
                    "trend": "worsening" if worsening else "not_worsening" if previous_value is not None else "unknown",
                    "warningThreshold": warning,
                    "criticalThreshold": critical
                })

            if any(alert["severity"] == "critical" for alert in alerts):
                priority = "critical"
            elif any(alert["severity"] in ("warning", "watch") for alert in alerts):
                priority = {
                    "critical": "high",
                    "high": "high",
                    "medium": "medium",
                    "low": "low"
                }.get(criticality, "medium")
            else:
                priority = "normal"

            assessments.append({
                "machineId": machine_id,
                "machineName": machine.get("name", machine_id),
                "machineType": machine.get("type"),
                "latestTelemetryTimestamp": current.get("timestamp"),
                "priority": priority,
                "productionCriticality": criticality,
                "dataStatus": "available",
                "alerts": alerts,
                "faultCodes": current.get("faultCodes", [])
            })

        priority_order = {"critical": 0, "high": 1, "medium": 2, "low": 3, "normal": 4, "unknown": 5}
        criticality_order = {"critical": 0, "high": 1, "medium": 2, "low": 3, "unknown": 4}
        assessments.sort(key=lambda item: (
            priority_order.get(item["priority"], 5),
            criticality_order.get(item["productionCriticality"], 4),
            not any(alert.get("trend") == "worsening" for alert in item["alerts"])
        ))
        for rank, assessment in enumerate(assessments, start=1):
            assessment["rank"] = rank
        return assessments
    except Exception as e:
        return [{"error": str(e)}]


async def main():
    try:
        async with AzureCliCredential() as credential:
            async with (
                OpenAIChatClient(credential=credential).as_agent(
                    name="AnomalyClassificationAgent",
                    description="Anomaly classification agent",
                    instructions="""You are an anomaly detection and equipment prioritization agent.
                            Use get_equipment_health_snapshot to assess the fleet using recent telemetry,
                            machine-specific thresholds, change from the previous reading, and production criticality.
                            The tool's severity, trend, priority, and rank are authoritative; do not recalculate them.
                            Explain the highest-priority equipment first and cite the metric, measured value,
                            threshold, baseline change, and trend when available.
                            Production criticality may be missing. If it is "unknown", say so and never claim
                            production impact or high priority was caused by criticality.
                            Do not invent baseline values, fault codes, or business impact. Call out missing telemetry.

                            Return JSON with rankedEquipment (rank, machineId, machineName, priority,
                            productionCriticality, alerts), summary, and dataLimitations, followed by a concise
                            human-readable explanation of why the first equipment needs attention.
                            """,
                    tools=[
                        get_machine_data,
                        get_thresholds,
                        get_equipment_health_snapshot]

                ) as agent,
            ):

                print(f"✅ Created Anomaly Classification Agent: {agent.id}")

                # Test the agent with a simple query
                print("\n🧪 Testing the agent with a sample query...")
                try:
                    result = await agent.run("What equipment needs attention first? Analyze recent telemetry across the fleet and rank the machines.")
                    print(f"✅ Agent response: {result.text}")
                except Exception as test_error:
                    print(
                        f"⚠️  Agent test failed (but agent was still created): {test_error}")

                return agent

    except Exception as e:
        print(f"❌ Error creating agent: {e}")
        print("Make sure you have run 'az login' and have proper Azure credentials configured.")
        return None

if __name__ == "__main__":
    asyncio.run(main())
