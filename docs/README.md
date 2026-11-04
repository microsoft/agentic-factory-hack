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
- **Cost strategy A**: API Management and AI Search are the expensive services (~$146 and ~$74/month). They are **deleted after each demo** and recreated before the next one.
- ACR and the placeholder Container App from the template are **removed** (not used).
- Challenge 2 (C# Repair Planner built with Copilot `agentplanning` agent) is **included**.

## Architecture in one picture
```
Local machine (Aspire: UI + workflow API + Python agents)
        │
        ▼
Microsoft Foundry (agents + gpt-5.4 / gpt-5.4-mini / embeddings)
   │            │                │
   ▼            ▼                ▼
API Mgmt     AI Search       Cosmos DB (serverless)
(MCP servers) (Foundry IQ KB)  Storage (machine wiki)
 [ephemeral]  [ephemeral]      App Insights + Log Analytics
```
Agents: Anomaly Classification → Fault Diagnosis → Repair Planner → Maintenance Scheduler → Parts Ordering.
Repo originals: [main README](../README.md) · [challenge-0](../challenge-0/README.md) · [challenge-1](../challenge-1/README.md) · [challenge-2](../challenge-2/README.md) · [challenge-3](../challenge-3/README.md) · [challenge-4](../challenge-4/README.md)

## Wiki index
| Page | Purpose |
|---|---|
| [01-plan.md](01-plan.md) | Step-by-step plan (Phases 0–9) |
| [02-cost-and-budget.md](02-cost-and-budget.md) | Costs, budget, teardown strategy, cost log |
| [03-progress-tracker.md](03-progress-tracker.md) | **Live status – check here for progress** |
| [04-environment-and-config.md](04-environment-and-config.md) | Resource names, `.env` variables, roles |
| [05-demo-runbook.md](05-demo-runbook.md) | Demo-day procedure and script |
| [06-risks-and-decisions.md](06-risks-and-decisions.md) | Decision log and open risks |
| [07-troubleshooting.md](07-troubleshooting.md) | Known issues and fixes |

## Status dashboard
| Phase | Status |
|---|---|
| 0 Account, guardrails, quota, tooling | ✅ Done (2026-11-04) |
| 1 Repo infra changes + scripts | ⬜ Not started |
| 2 Deploy to Azure | ⬜ Not started |
| 3 Data seeding + permissions | ⬜ Not started |
| 4 Challenge 1 agents | ⬜ Not started |
| 5 Challenge 2 Repair Planner | ⬜ Not started |
| 6 Challenge 3 agents | ⬜ Not started |
| 7 Challenge 4 end-to-end demo | ⬜ Not started |
| 8 Teardown / rebuild rehearsal | ⬜ Not started |
| 9 Demo runbook finalised | ⬜ Not started |

Legend: ⬜ not started · 🟡 in progress · ✅ done · ⛔ blocked

## Demo day quick start
1. ~60 min before: run `scripts/demo-up.sh` (creates API Management + AI Search, re-seeds, refreshes `.env`) – *script is created in Phase 1*.
2. ~15 min before: smoke test (see [05-demo-runbook.md](05-demo-runbook.md)).
3. Demo: `cd challenge-4/agent-workflow && aspire run` → open the frontend → **Trigger Anomaly**.
4. After the demo: run `scripts/demo-down.sh` to stop the spend.

## Rules
- Never commit `.env`, keys or connection strings (`.env` is gitignored). Docs list variable **names** only.
- Do not use `challenge-2/example-solution` as a template for the Challenge 2 build.
