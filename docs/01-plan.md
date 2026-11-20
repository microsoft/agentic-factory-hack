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
- Both scripts were also post-up hooks of `demo-up.sh`
- ⚠ **Superseded by Phase 10**: the two data tools no longer run on API Management but on an Azure Functions MCP server; the connections `machine-data-connection` / `maintenance-data-connection` now use header `x-functions-key`. The APIM steps above were done first and then replaced (APIM is now optional, `--with-apim`)

## Phase 5 – Challenge 2 Repair Planner  ✅ (2026-11-04)
- [x] Built the C# RepairPlanner in `challenge-2/RepairPlanner/` following the spec in `.github/agents/agentplanning.agent.md` (pinned NuGet versions, `net10.0`, dual JSON attributes, hard-coded fault mappings, primary constructors). `challenge-2/example-solution` was **not** opened or used. Note: written by the Copilot CLI agent from the spec, not interactively with the VS Code `agentplanning` agent
- [x] Verified against Azure: agent `RepairPlannerAgent` v1 registered in Foundry → found 3 technicians + 2 parts in Cosmos DB → work order generated and saved (status `new`, assigned to `tech-004`, parts `TCP-HTR-4KW` + `GEN-TS-K400`, 4+ ordered tasks). The test work order was deleted afterwards so the seed data stays at 5 work orders
- How to run (Windows PowerShell): load `.env` into the process and `dotnet run` in `challenge-2/RepairPlanner` (see [04-environment-and-config.md](04-environment-and-config.md))

## Phase 6 – Challenge 3 agents  ✅ (2026-11-04)
- [x] `python agents/maintenance_scheduler_agent.py wo-2024-456` → machine-004 / `load_cell_drift`: window 2026-11-05 22:00, risk 76/100, URGENT; schedule saved, work order updated
- [x] `python agents/parts_ordering_agent.py wo-2024-456` → ran OK, but the model returned **no order items** (see troubleshooting #22: seed data says parts are in stock). `wo-2024-468` → "all parts available" path, status `Ready`; scheduler on `wo-2024-468` also OK
- [x] `run-batch.py` → 10/10 successful in 103 s (5 work orders × 2 agents): traces are in App Insights / Foundry → Tracing
- [x] Reset helper `scripts/reset-demo-data.sh` restores the 5 seeded work orders and clears `MaintenanceSchedules`, `PartsOrders`, `ChatHistories` (the agents change work order statuses and write these containers). Run it after testing and before a demo

## Phase 7 – Challenge 4 end-to-end  ✅ (2026-11-04)
- [x] Start: `scripts/start-workflow.sh` (WSL; loads `.env`, `aspire run`). All resources healthy: `app` (Python agents, https :8000), `dotnetworkflow` (http :5231), `frontend` (Vite, port varies), dashboard https://localhost:17072. First start ~3–4 min (uv sync + npm install)
- [x] API run `POST /api/analyze_machine` (machine-001, curing_temperature 179.2, cycle_time 14.5): all **5 agents completed in ~60 s**, no errors: Anomaly (2 warnings) → Fault Diagnosis (`curing_temperature_excessive`, KB + machine tools) → Repair Planner (work order, John Smith) → Maintenance Scheduler (risk 78, URGENT, 2026-11-05 22:00) → Parts Ordering ("all parts available")
- [x] UI run in the browser (Trigger anomaly): "Workflow completed • 5 agents processed", all steps Done, tool calls visible (`knowledge_base_retrieve`, `get-machine`, `GetAvailableTechnicians`, `CreateWorkOrder`, …)
- [x] Aspire dashboard reachable (HTTP 200). Traces/Console views not clicked through yet – do that in the Phase 8 rehearsal
- [x] Aspire stopped afterwards and demo data reset (`scripts/reset-demo-data.sh`: the runs had created 3 extra work orders, schedules and chat history)

## Phase 8 – Teardown / rebuild rehearsal  🟡 (mostly done in Phase 10)
- [x] Teardown and rebuild of the default path: deleted Search (`demo-down.sh --delete-search`), `demo-up.sh` recreated it (Free, francecentral), rebuilt the knowledge base, restored `.env`; whole run 13 min (mostly waiting for the old Search name to be released); workflow verified afterwards on a different machine/fault
- [x] `demo-up.sh --with-apim` + `demo-down.sh` rehearsed (found and fixed the `get-keys.sh` deployment-selection bug and the asynchronous APIM deletion)
- [ ] Click through the Aspire dashboard Traces/Console views and App Insights / Foundry tracing once

## Phase 9 – Runbook  🟡
- [x] [05-demo-runbook.md](05-demo-runbook.md) rewritten for the setup without APIM (steps, demo script, fallbacks, test prompts)
- [ ] One dry run with a second person; capture screenshots / a recording as a fallback

## Phase 10 – Remove API Management  ✅ (2026-11-04)
Reason: APIM BasicV2 = $0.27/hour (price corrected from my earlier $0.20) and it only served two MCP tools. See [06-risks-and-decisions.md](06-risks-and-decisions.md).
- [x] Spikes: Azure Functions MCP extension (Python, Flex Consumption) and AI Search Free tier (throwaway service, full knowledge-base flow, then the real switch)
- [x] MCP server code `challenge-1/mcp-functions/` (tools `get-machine`, `get-threshold`; Cosmos via managed identity)
- [x] Core template: Flex plan + function app + deployment container + Cosmos data role; deployed
- [x] `scripts/deploy-mcp-function.sh` (zip with Linux wheels → `config-zip`, smoke test, Foundry connections with `x-functions-key`, `.env`)
- [x] Agent scripts: configurable auth header (`MCP_AUTH_HEADER` / `MCP_AUTH_KEY`), `allowed_tools` per MCP label
- [x] Regression (6 anomaly prompts, diagnosis, workflow via API and UI) before deleting APIM and again after
- [x] Deleted APIM (+ Cosmos role, soft-delete purge); Search moved from Basic to Free
- [x] APIM split into optional `azuredeploy.demo-apim.json`; `demo-up.sh` / `demo-down.sh` reworked (`--with-apim`, `--seed`, `--redeploy-mcp`, retry on `ServiceDeleting`, keep Free Search)
- [x] Wiki updated, corrected cost numbers, committed
