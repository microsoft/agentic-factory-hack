# 03 – Progress tracker

Back to [wiki home](README.md) · Details in [01-plan.md](01-plan.md)

Status: ⬜ pending · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Depends on | Status | Date | Notes |
|---|---|---|---|---|---|
| prep-account | Account, providers, budget + alerts | – | ✅ | 2026-11-04 | Logged in (Windows + WSL), Owner confirmed, providers registered (DocumentDB still finishing in background), budget `budget-tire-factory-120` ($120/month; alerts 50/75/90 % actual + 100 % forecast → alon.havivi@macaw.nl) |
| check-quota | Model quota in swedencentral | – | ✅ | 2026-11-04 | GlobalStandard gpt-5.4 limit 250 (need 50), gpt-5.4-mini 1000 (need 50), text-embedding-3-large Standard 350 (need 10). OK |
| install-tools | WSL2, az, .NET 10, Aspire, uv, Node, Python | – | ✅ | 2026-11-04 | WSL Ubuntu-24.04 (user `alon`) set up via `scripts/setup-wsl-tools.sh`; Python venv `~/factory-venv` with `requirements.txt` |
| split-templates | Core + ephemeral templates, deterministic names | – | ✅ | 2026-11-04 | `challenge-0/infra/azuredeploy.demo-core.json` (validated) and `azuredeploy.demo-ephemeral.json` (validated in Phase 2) |
| demo-scripts | `demo-up.sh` / `demo-down.sh` | split-templates | ✅ | 2026-11-04 | `scripts/demo-up.sh`, `scripts/demo-down.sh`; syntax OK, down dry-run OK; real run in Phase 2 / 8. RG `rg-tire-factory-demo` created (empty, free) |
| deploy-core | RG + core template | prep-account, check-quota, split-templates | ✅ | 2026-11-04 | Needed 2 template fixes (see troubleshooting #11, #12). Models: gpt-5.4 (50), gpt-5.4-mini (50), text-embedding-3-large (10) all Succeeded |
| deploy-ephemeral | APIM + AI Search | deploy-core | ✅ | 2026-11-04 | _Superseded by Phase 10 (no APIM, Free Search)._ Originally: APIM BasicV2 swedencentral; Search basic **francecentral** (swedencentral + germanywestcentral had no capacity, #13) |
| keys-seed | get-keys, seed-data, roles, smoke test | deploy-ephemeral, install-tools | ✅ | 2026-11-04 | Seeded 10 Cosmos containers, 5 wiki blobs, 2 APIM APIs; 3 roles assigned; APIM `/machine`, `/maintenance` return 200 |
| challenge1 | Anomaly, MCP servers, KB, Fault Diagnosis | keys-seed | ✅ | 2026-11-04 | All 3 steps verified. MCP servers + KB are scripted (`create-apim-mcp-servers.sh`, `create-knowledge-base.sh`). Agents in Foundry: `AnomalyClassificationAgent:1`, `FaultDiagnosisAgent:1`. TODO Phase 8: refresh project connections after APIM recreate |
| challenge2 | C# Repair Planner via agentplanning | keys-seed | ✅ | 2026-11-04 | `challenge-2/RepairPlanner` builds (0 warnings) and runs end to end; work order saved to Cosmos (then removed). Spec-driven, `example-solution` untouched |
| challenge3 | Scheduler + Parts Ordering | keys-seed | ✅ | 2026-11-04 | Both agents run; `run-batch.py` 10/10; data reset afterwards with `scripts/reset-demo-data.sh`. Known quirk: parts ordering returns an empty order for wo-2024-456 (#22) |
| challenge4 | `aspire run` end-to-end demo | challenge1, challenge3 | ✅ | 2026-11-04 | API + UI runs: 5/5 agents OK in ~60 s. `scripts/start-workflow.sh` added. Data reset afterwards. Open: click through dashboard Traces/Console + App Insights in rehearsal |
| noapim-spikes | Spikes: Functions MCP (Python, Flex) and AI Search Free tier | – | ✅ | 2026-11-04 | Flex + Python 3.12 available in swedencentral; MCP extension works with `mcp_tool_trigger` + bundle `[4.0.0,5.0.0)`. Free Search (francecentral): knowledge source, index (10 docs), knowledge base, retrieval and project-identity MCP all work, same ranking as Basic |
| noapim-mcp-function | MCP server on Azure Functions (code, core template, deploy script) | noapim-spikes | ✅ | 2026-11-04 | `challenge-1/mcp-functions/`, Flex plan + app + Cosmos role in `azuredeploy.demo-core.json`, `scripts/deploy-mcp-function.sh`. Same tool names/arguments; `get-threshold` returns all thresholds. Core update deployed (100 s) |
| noapim-agents | Agent scripts use the function tools | noapim-mcp-function | ✅ | 2026-11-04 | `anomaly_classification_agent_mcp.py`: configurable auth header (`MCP_AUTH_*`). Both MCP agents: `allowed_tools` per label – needed because one server exposes both tools (without it the model flagged in-range values 14/14 vs APIM baseline 4/14, with it 4/8) |
| noapim-verify | Regression: 6 anomaly prompts, diagnosis, workflow via API + UI | noapim-agents | ✅ | 2026-11-04 | All match the baseline; workflow 5/5 agents OK (~60–80 s) before and after deleting APIM; also on a different machine/fault (machine-004) on the rebuilt stack |
| noapim-delete-apim | Delete APIM (+ Cosmos role, purge) | noapim-verify | ✅ | 2026-11-04 | $0.27/hour stopped. Deletion is asynchronous: `demo-down.sh` now waits before purging |
| noapim-search-free | Switch AI Search Basic → Free | noapim-spikes | ✅ | 2026-11-04 | Free Search in francecentral (`searchServiceSku` default `free`); `demo-down.sh` keeps it. Risk: Microsoft may delete inactive free services → `demo-up.sh` recreates (rehearsed, 13 min incl. name-release retries) |
| noapim-scripts | Templates + scripts without APIM by default | noapim-search-free | ✅ | 2026-11-04 | `azuredeploy.demo-apim.json` (optional, deployed as `demo-optional-apim`), `demo-up.sh --with-apim/--seed/--redeploy-mcp`, `ServiceDeleting` retry, `deploy-mcp-function.sh --env-only`. Rehearsed: default rebuild and `--with-apim` + teardown both OK (found + fixed a `get-keys.sh` deployment-selection bug) |
| rehearse-teardown | demo-down then demo-up, retest | challenge4, demo-scripts | 🟡 | 2026-11-04 | Largely done in Phase 10: Search delete + rebuild, `--with-apim` up/down. Still to do: click through Aspire dashboard Traces/Console and App Insights |
| demo-runbook | Final runbook | rehearse-teardown | 🟡 | 2026-11-04 | Updated for the no-APIM setup. Still to do: one dry run with a second person, capture screenshots/recording as a fallback |

## Change log
| Date | Change |
|---|---|
| 2026-11-03 | Wiki created, plan agreed (strategy A, local Aspire, Challenge 2 included) |
| 2026-11-04 | Phase 0 complete: login, providers, budget, quota check, WSL tooling. Added `.gitattributes` (LF for `*.sh`) and `scripts/setup-wsl-tools.sh` |
| 2026-11-04 | Phase 1 complete: core + ephemeral templates, `demo-up.sh` / `demo-down.sh`; RG `rg-tire-factory-demo` created |
| 2026-11-04 | Phase 2 complete: core + ephemeral deployed; template fixes (project/model race, App Insights connection string, `searchLocation` parameter); `.env` generated |
| 2026-11-04 | Phase 3 complete: data seeded, roles assigned, APIM smoke test OK. Foundry portal check: project has AppInsights + AI Search connections, no agents yet (expected) |
| 2026-11-04 | Phase 4 complete: MCP servers (scripted), knowledge base (scripted), Anomaly + Fault Diagnosis agents running in Foundry |
| 2026-11-04 | Phase 5 complete: C# Repair Planner built and verified end to end |
| 2026-11-04 | Phase 6 complete: Scheduler + Parts Ordering agents run, batch 10/10, `reset-demo-data.sh` added |
| 2026-11-04 | Phase 7 complete: end-to-end workflow works via API and UI (5/5 agents, ~60 s); `start-workflow.sh` added |
| 2026-11-04 | Phase 10 complete: APIM removed from the default setup (Azure Functions MCP server instead, `allowed_tools`), AI Search moved to the Free tier, APIM now optional (`--with-apim`). Prices corrected (APIM $0.27/h, Search Basic $0.10/h) |
