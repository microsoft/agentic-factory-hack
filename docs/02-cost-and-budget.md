# 02 – Cost and budget

Back to [wiki home](README.md)

**Budget: USD 120 / month.** Figures are approximate list prices – verify in the Azure Pricing Calculator and Cost Management.

## What the template deploys and what it costs
| Resource | SKU in repo template | ~Idle cost / month | Decision |
|---|---|---|---|
| API Management | BasicV2 | ~$146 (~$0.20/h) | **Ephemeral** – delete between demos. MCP support needs Developer/Basic/Standard/Premium or v2 tiers (not Consumption) |
| AI Search | Basic | ~$74 (~$0.10/h) | **Ephemeral** – delete between demos, re-index wiki on recreate |
| Container Registry | Basic | ~$5 | **Remove** (only for optional ACA deploy) |
| Container App + Environment | 0.5 vCPU, minReplicas 1, placeholder image | ~$7 | **Remove** (unused) |
| Cosmos DB | Serverless | < $1 | Keep |
| Storage | Standard_LRS | < $1 | Keep |
| Log Analytics + App Insights | PerGB2018 | ~$0–3 | Keep, set daily cap |
| Foundry account + project | S0 | $0 idle | Keep |
| Models gpt-5.4, gpt-5.4-mini, text-embedding-3-large | pay per token | cents per demo run | Keep |
| Content Safety | S0 | $0 idle | Keep (not used by challenges) |

## Scenarios
| Scenario | Monthly estimate |
|---|---|
| Everything always on (template as is) | ~$230+ (over budget) |
| Strategy A: APIM + Search only up on demo days (~2 days/month) | **~$10–25** |
| Strategy B: Search always on, APIM on demo days | ~$90–100 |

**Chosen: Strategy A.**

## Guardrails
- Budget `$120` on subscription/RG with alerts at 50 / 75 / 90 % → alon.havivi@macaw.nl (Phase 0)
- Tag RG `purpose=demo`
- Log Analytics daily cap (e.g. 0.1 GB)
- Keep model capacities small (20–50K TPM)
- VS Enterprise subscriptions have a spending limit; running out of credit disables resources
- After every demo run `scripts/demo-down.sh` and verify in Cost Management

## Monthly cost log
| Month | APIM hours | Search hours | Actual cost (USD) | Notes |
|---|---|---|---|---|
| 2026-11 | | | | |
