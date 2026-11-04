# 02 – Cost and budget

Back to [wiki home](README.md)

**Budget: USD 120 / month.** Figures are approximate list prices (Azure retail prices API, 2026-11-04) – verify in the Azure Pricing Calculator and Cost Management.

## What the solution consists of and what it costs now
| Resource | SKU | ~Cost | Lifecycle |
|---|---|---|---|
| Azure Functions MCP server (replaces APIM) | Flex Consumption, Python 3.12, 2 GB instances, scale to zero | ~$0 idle; cents per demo | permanent |
| AI Search | **Free** (50 MB, 3 indexes, 1 per subscription) | $0 | permanent* |
| Cosmos DB | Serverless | < $1 | permanent |
| Storage (+ function deployment container) | Standard_LRS | < $1 | permanent |
| Log Analytics + App Insights | PerGB2018, 0.1 GB/day cap | ~$0–3 | permanent |
| Foundry account + project | S0 | $0 idle | permanent |
| Models gpt-5.4, gpt-5.4-mini, text-embedding-3-large | pay per token | cents per demo run | permanent |
| Content Safety | S0 | $0 idle | permanent (not used by challenges) |
| *Optional:* API Management | BasicV2 | **$0.27/hour** (~$197/month if left on) | only with `demo-up.sh --with-apim` |
| *Fallback:* AI Search Basic | Basic | $0.10/hour (~$73/month) | only if the Free tier ever fails (`searchServiceSku=basic`) |
| Removed from the original template | Container Registry (~$5/month), Container App (~$7/month) | – | not deployed |

\* Microsoft may delete *inactive* free search services when a region is short of capacity; `demo-up.sh` recreates it and rebuilds the knowledge base.

## Scenarios
| Scenario | Monthly estimate |
|---|---|
| Original workshop template always on (APIM BasicV2 + Search Basic + ACR + Container App) | ~$285 (over budget) |
| Previous plan: APIM + Search Basic deleted between demos, ~2 demo days (10 h each) | ~$10–15 plus the risk of forgetting to delete (~$9/day) |
| **Current: Functions MCP + Free Search, nothing hourly-billed** | **~$1–5 + model tokens** |
| Current + `--with-apim` for a 10 h demo | + ~$2.70 per demo day |

## Guardrails
- Budget `budget-tire-factory-120` ($120/month) on the subscription with alerts at 50 / 75 / 90 % actual and 100 % forecast → alon.havivi@macaw.nl
- Tag RG `purpose=demo`
- Log Analytics daily cap 0.1 GB
- Keep model capacities small (50K TPM)
- VS Enterprise subscriptions have a spending limit; running out of credit disables resources
- If you used `--with-apim`: run `scripts/demo-down.sh` afterwards. It prints `Hourly-billed services remaining: API Management=0, paid AI Search=0` when everything is clean

## Monthly cost log
| Month | APIM hours (optional) | Paid Search hours | Actual cost (USD) | Notes |
|---|---|---|---|---|
| 2026-11 | ~2.5 h (estimate: build + migration tests + one rehearsal, deleted 2026-11-04) | ~2 h Basic (estimate, then switched to Free) | _check Cost Management_ | One-off build month. Expect a few dollars of APIM/Search hours plus model tokens from the test runs |
