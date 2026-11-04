# 05 – Demo runbook

Back to [wiki home](README.md)

> Updated 2026-11-04 for the setup without API Management. Still a draft until one dry run with a second person is done.

## T-30 min: bring services up
1. `az login` (WSL) and check `az account show` is the VS Enterprise subscription.
2. `scripts/demo-up.sh` (WSL). Normally it just verifies things and takes ~2 min. If the Free AI Search service was deleted by Microsoft it recreates it and rebuilds the knowledge base (rehearsed: ~13 min, most of it waiting for the old name to be released – the script retries by itself).
3. Optional – only if you want to show the "API Management as AI gateway / MCP server" story: `scripts/demo-up.sh --with-apim` (**$0.27/hour**; open APIM in the portal → APIs / MCP Servers: `get-machine-data`, `get-maintenance-data`). The agents do not use it. Run `scripts/demo-down.sh` afterwards.

## T-15 min: smoke test
- [ ] `scripts/reset-demo-data.sh` (restores the seeded work orders, clears schedules / parts orders / agent memory from earlier tests)
- [ ] Foundry portal (https://ai.azure.com, new portal) → project `msagthack-aiproject-…` → Build: five agents visible; playground question works
- [ ] `scripts/start-workflow.sh` in a WSL terminal; wait until `aspire describe` (second WSL terminal: `export PATH=$HOME/.aspire/bin:$PATH; aspire describe`) shows `app`, `dotnetworkflow`, `frontend` as Running/Healthy; the dashboard login link is printed at startup
- [ ] Open the frontend URL from `aspire describe`, click **Trigger anomaly** once and wait for "Workflow completed • 5 agents processed" (~60–80 s); verified 2026-11-04 (also after APIM was deleted): all five agents Done, tool calls visible (`get-machine`, `get-threshold`, `knowledge_base_retrieve`, `GetAvailableTechnicians`, `CreateWorkOrder`)
- [ ] Afterwards run `scripts/reset-demo-data.sh` again – a test run creates work orders, schedules and agent memory

## Demo script (~20 min)
1. **Scenario**: tire factory, OEE, unplanned downtime (use `images/maintenance-scenario.png`).
2. **Architecture**: `images/hackathon-architecture.png` (note: here the two data tools run as an **Azure Functions MCP server** instead of API Management).
3. **Foundry portal**: show the Anomaly and Fault Diagnosis agents, models, tracing.
4. **MCP tools**: the Functions MCP server (`get-machine`, `get-threshold`) behind the project connections; mention that API Management can front the same tools as an AI gateway (show it live only with `--with-apim`).
5. **Foundry IQ**: knowledge base over the machine wiki (Blob + AI Search Free).
6. **Aspire**: run the workflow, show dashboard.
7. **Frontend**: Trigger Anomaly; watch the 5 agents: Anomaly Classification → Fault Diagnosis → Repair Planner → Maintenance Scheduler → Parts Ordering.
8. **Observability**: Aspire traces and App Insights.
9. **Q&A / cost story**: nothing is billed per hour any more – Functions Flex (scale to zero), Free AI Search, serverless Cosmos DB; a few dollars per month plus model tokens.

## After the demo
- `scripts/reset-demo-data.sh`
- `scripts/demo-down.sh` (removes APIM if you used `--with-apim`; keeps the free Search service)
- Note anything unusual in [02-cost-and-budget.md](02-cost-and-budget.md)

## Appendix – Test prompts for `AnomalyClassificationAgent` (Foundry playground)
Verified 2026-11-04 against the seeded data (after the `get-threshold` patch, and again on the Azure Functions MCP server). Paste as plain text.

| # | Prompt | Observed result |
|---|---|---|
| 1 | `Classify the following anomalies for machine-001: [{"metric": "curing_temperature", "value": 183.5}, {"metric": "curing_pressure", "value": 207}]` | 2 critical (limits 182 / 205), status `high` |
| 2 | `Classify the following anomalies for machine-001: [{"metric": "curing_temperature", "value": 179.2}, {"metric": "cycle_time", "value": 14.5}]` | 2 warnings (limits 178 / 14), status `medium` |
| 3 | `Classify the following anomalies for machine-002: [{"metric": "drum_vibration", "value": 2.1}]` | **flaky** – should be "no alerts", but the workshop agent sometimes reports a warning for a value inside the normal range (about 30–50 % of runs, same on APIM and on Functions). Avoid as a live demo case |
| 4 | `Classify the following anomalies for machine-005: [{"metric": "mixing_temperature", "value": 175}, {"metric": "power_consumption", "value": 290}, {"metric": "vibration", "value": 5.2}]` | temperature critical, power warning, vibration OK |
| 5 | `Classify the following anomalies for machine-003: [{"metric": "throughput", "value": 640}]` | warning (below 650 = bad direction) |
| 6 | `Classify the following anomalies for machine-999: [{"metric": "curing_temperature", "value": 180}]` | an alert/note that the machine was not found (wording varies) |

Fault Diagnosis agent test: `Hello, what can the issue be when machine-001 has curing temperature reading of 179.2°C that exceeds warning threshold of 178°C?` → `curing_temperature_excessive`, causes from the wiki.

## Fallbacks
- Quota/network failure: use screenshots/recording (still to be captured).
- Search not ready: run `scripts/demo-up.sh` again, or demo the agents with direct Cosmos tools (`anomaly_classification_agent.py`).
- Functions MCP not responding (cold start after idle takes a few seconds; the first call can be slow): re-run `scripts/deploy-mcp-function.sh`.
