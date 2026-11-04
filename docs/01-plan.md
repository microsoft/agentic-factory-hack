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

## Phase 2 – Deploy  ✅ (2026-11-04)
- [x] `az group create -n rg-tire-factory-demo -l swedencentral --tags purpose=demo`
- [x] Deploy core template (via `demo-up.sh`)
- [x] Deploy ephemeral template (APIM in swedencentral, **AI Search in francecentral** – see [06-risks-and-decisions.md](06-risks-and-decisions.md)); `demo-up.sh --skip-seed --search-location francecentral` took 11 min
- [x] `get-keys.sh` ran as part of `demo-up.sh` → `.env` generated; only `ACR_*` are empty (ACR intentionally removed)

## Phase 3 – Data and permissions (Challenge 0)  ✅ (2026-11-04)
- [x] `challenge-0/seed-data.sh` (Cosmos **10** containers in `FactoryOpsDB`, blob container `machine-wiki` with 5 articles, APIM Machine + Maintenance APIs)
- [x] Assigned roles to alon.havivi@macaw.nl: Azure AI Developer (project), Foundry User (AI Services), Cognitive Services OpenAI Contributor (AI Services account)
- [x] Smoke test: `GET /machine`, `/machine/machine-001`, `/maintenance/tire_curing_press` via APIM gateway → HTTP 200 with data (proves APIM → Cosmos via managed identity)

## Phase 4 – Challenge 1 agents  ✅ (2026-11-04)
- [x] Run `agents/anomaly_classification_agent.py` (direct Cosmos tools) → classified machine-001 with 2 warnings
- [x] Create APIM MCP servers `get-machine-data`, `get-maintenance-data` – **scripted**: `scripts/create-apim-mcp-servers.sh` (ARM API `2025-09-01-preview`, MCP API + tool per operation); endpoints written to `.env` (`https://<apim>.azure-api.net/<name>/mcp`)
- [x] Run `anomaly_classification_agent_mcp.py` → creates project connections `machine-data-connection`, `maintenance-data-connection` and agent `AnomalyClassificationAgent`
- [x] Knowledge base – **scripted**: `scripts/create-knowledge-base.sh` runs the notebook's code cells (knowledge source `machine-wiki-blob-ks`, knowledge base `machine-kb`, retrieval test, connection `machine-wiki-connection`); the MCP endpoint is `${SEARCH_SERVICE_ENDPOINT}knowledgebases/machine-kb/mcp?api-version=2025-11-01-preview`
- [x] Added `MCPTool(machine-wiki)` to `fault_diagnosis_agent.py`; ran it → `FaultDiagnosisAgent` returns `curing_temperature_excessive` grounded in the KB
- Both scripts are also the optional post-up hooks of `demo-up.sh`
- ⚠ Open for Phase 8: after an APIM recreate the APIM subscription key changes, so the project connections `machine-data-connection` / `maintenance-data-connection` (which store the key) must be refreshed – add a hook

## Phase 5 – Challenge 2 Repair Planner  ✅ (2026-11-04)
- [x] Built the C# RepairPlanner in `challenge-2/RepairPlanner/` following the spec in `.github/agents/agentplanning.agent.md` (pinned NuGet versions, `net10.0`, dual JSON attributes, hard-coded fault mappings, primary constructors). `challenge-2/example-solution` was **not** opened or used. Note: written by the Copilot CLI agent from the spec, not interactively with the VS Code `agentplanning` agent
- [x] Verified against Azure: agent `RepairPlannerAgent` v1 registered in Foundry → found 3 technicians + 2 parts in Cosmos DB → work order generated and saved (status `new`, assigned to `tech-004`, parts `TCP-HTR-4KW` + `GEN-TS-K400`, 4+ ordered tasks). The test work order was deleted afterwards so the seed data stays at 5 work orders
- How to run (Windows PowerShell): load `.env` into the process and `dotnet run` in `challenge-2/RepairPlanner` (see [04-environment-and-config.md](04-environment-and-config.md))

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
