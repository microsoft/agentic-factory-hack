# 05 – Demo runbook

Back to [wiki home](README.md)

> Draft – finalised in Phase 9 after the teardown/rebuild rehearsal. Update timings with real numbers.

## T-60 min: bring services up
1. `az login` and select the subscription.
2. `scripts/demo-up.sh` *(created in Phase 1)*: deploys API Management + AI Search, re-seeds APIM APIs, recreates MCP servers and knowledge base, refreshes `.env`. Expected 15–25 min.
3. `export $(cat .env | xargs)`

## T-15 min: smoke test
- [ ] `curl "$APIM_GATEWAY_URL/machine/machine-001" -H "Ocp-Apim-Subscription-Key: $APIM_SUBSCRIPTION_KEY"` returns JSON
- [ ] Foundry portal → Build: agents visible; playground question works
- [ ] `cd challenge-4/agent-workflow && aspire run`; Aspire dashboard shows all resources healthy
- [ ] Trigger Anomaly once end to end

## Demo script (~20 min)
1. **Scenario**: tire factory, OEE, unplanned downtime (use `images/maintenance-scenario.png`).
2. **Architecture**: `images/hackathon-architecture.png`.
3. **Foundry portal**: show the Anomaly and Fault Diagnosis agents, models, tracing.
4. **API Management**: Machine/Maintenance APIs exposed as MCP servers (AI gateway).
5. **Foundry IQ**: knowledge base over the machine wiki (Blob + AI Search).
6. **Aspire**: run the workflow, show dashboard.
7. **Frontend**: Trigger Anomaly; watch the 5 agents: Anomaly Classification → Fault Diagnosis → Repair Planner → Maintenance Scheduler → Parts Ordering.
8. **Observability**: Aspire traces and App Insights.
9. **Q&A / cost story**: ephemeral APIM + Search, ~$10–25/month.

## After the demo
- `scripts/demo-down.sh` (deletes API Management + AI Search)
- Log hours in [02-cost-and-budget.md](02-cost-and-budget.md)

## Fallbacks
- Quota/network failure: use screenshots/recording (to be captured during Phase 7).
- APIM/Search not ready: demo agents with direct Cosmos tools (`anomaly_classification_agent.py`).
