# 01 – Step-by-step plan

Back to [wiki home](README.md) · Track progress in [03-progress-tracker.md](03-progress-tracker.md)

## Phase 0 – Account, guardrails, quota, tooling  ✅ (2026-11-04)
- [x] `az login --tenant 05baed4d-e049-45c7-b47d-c99c66fdb589` and `az account set --subscription a39be7cd-9ae9-42cf-b42f-dc6e45a82464`
- [x] Confirm **Owner** role on the subscription (role assignments are required)
- [x] Register providers: Microsoft.AlertsManagement, ApiManagement, CognitiveServices, Search, DocumentDB, OperationalInsights, Insights, Storage
- [x] Create **$120 budget** with alerts at 50 / 75 / 90 % (email alon.havivi@macaw.nl)
- [x] **Quota check** in `swedencentral`: gpt-5.4, gpt-5.4-mini (GlobalStandard), text-embedding-3-large. If 0: request quota, or change region/model (`MODEL_DEPLOYMENT_NAME`)
- [x] Install tooling: WSL2 Ubuntu, Azure CLI, .NET SDK 10, Aspire CLI, uv, Node LTS, Python 3.11+, `pip install -r requirements.txt`, VS Code + Jupyter

## Phase 1 – Repo infra changes  ✅ (2026-11-04)
- [x] Split `challenge-0/infra/azuredeploy.json` into **core** (`azuredeploy.demo-core.json`) and **ephemeral** (`azuredeploy.demo-ephemeral.json`) templates (original left untouched)
  - core: Foundry + project + model deployments, Cosmos (serverless), Storage, Log Analytics (daily cap 0.1 GB), App Insights, Content Safety
  - ephemeral: API Management (BasicV2), AI Search (basic), their role assignments, Foundry→Search connection
  - The Cosmos data role for the APIM identity is **not** in the template: it is created/removed by `demo-up.sh` / `demo-down.sh` (the identity changes on every recreate and an ARM role assignment with a changed principalId cannot be updated)
- [x] Deterministic suffix (`uniqueString(resourceGroup().id)`) so names/URLs survive recreate
- [x] ACR + Container App dropped from the new templates; `get-keys.sh` already tolerates a missing ACR (warning only) and falls back to resource discovery, so it was not changed
- [x] Added `scripts/demo-up.sh` and `scripts/demo-down.sh` (syntax-checked; `demo-down.sh` dry-run OK on empty RG; full test happens in Phase 2 and Phase 8)
- [x] Core template validated with `az deployment group validate` (ephemeral can only be validated after core exists, Phase 2)

## Phase 2 – Deploy
- [ ] `az group create -n rg-tire-factory-demo -l swedencentral --tags purpose=demo`
- [ ] Deploy core template
- [ ] Deploy ephemeral template (~5–10 min)
- [ ] `challenge-0/get-keys.sh --resource-group rg-tire-factory-demo` → verify `.env` has no empty values

## Phase 3 – Data and permissions (Challenge 0)
- [ ] `challenge-0/seed-data.sh` (Cosmos 7 containers, blob `machine-wiki`, APIM Machine + Maintenance APIs)
- [ ] Assign roles to yourself: Azure AI Developer (project), Foundry User (AI Services), Cognitive Services OpenAI Contributor
- [ ] Smoke test: `curl $APIM_GATEWAY_URL/machine/machine-001` with subscription key

## Phase 4 – Challenge 1 agents
- [ ] Run `agents/anomaly_classification_agent.py`
- [ ] Create APIM MCP servers `get-machine-data`, `get-maintenance-data`; add URLs to `.env`
- [ ] Run `anomaly_classification_agent_mcp.py`
- [ ] Run `create_knowledge_base.ipynb` (Foundry IQ); add wiki MCP endpoint to `.env`
- [ ] Add `MCPTool(machine-wiki)` to `fault_diagnosis_agent.py`; run it

## Phase 5 – Challenge 2 Repair Planner
- [ ] Build the C# RepairPlanner with the Copilot `agentplanning` agent (pinned NuGet versions, no `example-solution`)
- [ ] Verify against Cosmos DB

## Phase 6 – Challenge 3 agents
- [ ] `python agents/maintenance_scheduler_agent.py wo-2024-456`
- [ ] `python agents/parts_ordering_agent.py wo-2024-456`
- [ ] Optional small `run-batch.py` for traces

## Phase 7 – Challenge 4 end-to-end
- [ ] `cd challenge-4/agent-workflow && aspire run`
- [ ] Open dashboard + frontend, **Trigger Anomaly**, all 5 agents complete
- [ ] Check traces (Aspire dashboard, App Insights, Foundry tracing)

## Phase 8 – Teardown / rebuild rehearsal
- [ ] `scripts/demo-down.sh`; confirm daily cost drops to cents
- [ ] `scripts/demo-up.sh`; re-test end to end; record timings and fixes in [07-troubleshooting.md](07-troubleshooting.md)

## Phase 9 – Runbook
- [ ] Finalise [05-demo-runbook.md](05-demo-runbook.md) with real timings and fallback material
