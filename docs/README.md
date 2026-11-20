# Agentic Factory – Project Wiki

> **Single source of truth** for deploying, running and demoing the *Intelligent Predictive Maintenance* (tire factory) multi-agent solution on our own Azure subscription.
> Update [03-progress-tracker.md](03-progress-tracker.md) after every step.

## Goal
Deploy the solution from this repo into a **new resource group** and be able to **demo it end to end** (Trigger Anomaly → 5 agents → repair plan, schedule, parts order), while staying inside a **$120/month** budget.

## Target environment
| Item | Value |
|---|---|
| Tenant ID | `05baed4d-e049-45c7-b47d-c99c66fdb589` |
| Subscription | Visual Studio Enterprise Subscription – MPN (`a39be7cd-9ae9-42cf-b42f-dc6e45a82464`) |
| User | alon.havivi@macaw.nl |
| Resource group | `rg-tire-factory-demo` |
| Region | `swedencentral` |
| Demo host | Local Windows laptop (WSL2 recommended) running Aspire |
| Monthly budget | USD 120 |

## Key decisions (see [06-risks-and-decisions.md](06-risks-and-decisions.md))
- **No API Management by default** (2026-11-04): it cost $0.27/hour (~$197/month always-on) and only served two MCP tools. Those tools now run on an **Azure Functions (Flex Consumption) MCP server** (~$0, permanent). APIM is an optional extra: `scripts/demo-up.sh --with-apim`, show-only.
- **AI Search on the Free tier** ($0, 50 MB, 3 indexes). It holds the Foundry IQ knowledge base. A paid tier is only a fallback (`searchServiceSku=basic`, ~$0.10/hour).
- Result: **nothing in the solution is billed hourly any more** – expected cost is a few dollars per month plus model tokens.
- ACR and the placeholder Container App from the template are **removed** (not used).
- Challenge 2 (C# Repair Planner built with Copilot `agentplanning` agent) is **included**.

## Architecture in one picture
```
Local machine (Aspire: UI + workflow API + Python agents + C# Repair Planner)
        │
        ▼
Microsoft Foundry (agents + gpt-5.4 / gpt-5.4-mini / embeddings)
   │               │                    │
   ▼               ▼                    ▼
Functions MCP   AI Search (Free)    Cosmos DB (serverless)
get-machine /   Foundry IQ KB       Storage (machine wiki)
get-threshold   (machine-kb)        App Insights + Log Analytics
 [permanent]     [permanent*]
 optional, show-only: API Management (--with-apim, $0.27/h)
 * free services can be deleted by Microsoft after long inactivity; demo-up.sh recreates it
```
Agents: Anomaly Classification → Fault Diagnosis → Repair Planner → Maintenance Scheduler → Parts Ordering.
Repo originals: [main README](../README.md) · [challenge-0](../challenge-0/README.md) · [challenge-1](../challenge-1/README.md) · [challenge-2](../challenge-2/README.md) · [challenge-3](../challenge-3/README.md) · [challenge-4](../challenge-4/README.md)

## Wiki index
| Page | Purpose |
|---|---|
| [01-plan.md](01-plan.md) | Step-by-step plan (Phases 0–10) |
| [02-cost-and-budget.md](02-cost-and-budget.md) | Costs, budget, scenarios, cost log |
| [03-progress-tracker.md](03-progress-tracker.md) | **Live status – check here for progress** |
| [04-environment-and-config.md](04-environment-and-config.md) | Resource names, `.env` variables, roles |
| [05-demo-runbook.md](05-demo-runbook.md) | Demo-day procedure and script |
| [06-risks-and-decisions.md](06-risks-and-decisions.md) | Decision log and open risks |
| [07-troubleshooting.md](07-troubleshooting.md) | Known issues and fixes |

## Status dashboard
| Phase | Status |
|---|---|
| 0 Account, guardrails, quota, tooling | ✅ Done (2026-11-04) |
| 1 Repo infra changes + scripts | ✅ Done (2026-11-04) |
| 2 Deploy to Azure | ✅ Done (2026-11-04) |
| 3 Data seeding + permissions | ✅ Done (2026-11-04) |
| 4 Challenge 1 agents | ✅ Done (2026-11-04) |
| 5 Challenge 2 Repair Planner | ✅ Done (2026-11-04) |
| 6 Challenge 3 agents | ✅ Done (2026-11-04) |
| 7 Challenge 4 end-to-end demo | ✅ Done (2026-11-04) |
| 10 Remove API Management (Functions MCP + Free Search) | ✅ Done (2026-11-04) – no hourly-billed services left |
| 8 Teardown / rebuild rehearsal | 🟡 Mostly covered by Phase 10 (Search rebuild + `--with-apim` rehearsed); Aspire dashboard traces / App Insights still to click through |
| 9 Demo runbook finalised | 🟡 Updated for the new setup; needs one dry run with a second person |

Legend: ⬜ not started · 🟡 in progress · ✅ done · ⛔ blocked

## Demo day quick start
1. ~30 min before: `scripts/demo-up.sh` in WSL. Normally it only verifies everything (~2 min). If Microsoft deleted the Free Search service it recreates it and rebuilds the knowledge base (~5–15 min).
2. Optional, to show the "API Management as AI gateway" story: `scripts/demo-up.sh --with-apim` (adds APIM, **$0.27/hour**), and `scripts/demo-down.sh` afterwards.
3. `scripts/reset-demo-data.sh`, then `scripts/start-workflow.sh` (first start ~3–4 min) → `aspire describe` shows the frontend URL (Vite, port varies) → open it → **Trigger anomaly** (takes ~60–80 s; default payload machine-001, curing_temperature 179.2, cycle_time 14.5).
4. After the demo: `scripts/demo-down.sh` (a no-op for billing now, but it removes APIM if you used it) and `scripts/reset-demo-data.sh`.

## Rules
- Never commit `.env`, keys or connection strings (`.env` is gitignored). Docs list variable **names** only.
- Do not use `challenge-2/example-solution` as a template for the Challenge 2 build.
