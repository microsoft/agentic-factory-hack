# 06 – Risks and decisions

Back to [wiki home](README.md)

## Decision log
| Date | Decision | Reason |
|---|---|---|
| 2026-11-03 | New RG `rg-tire-factory-demo` in `swedencentral` | Template-allowed region, default |
| 2026-11-03 | Strategy A: delete APIM + AI Search between demos | Together ~$220/month, budget is $120 (**superseded 2026-11-04**: no hourly-billed services any more, see below) |
| 2026-11-03 | Run demo app locally on Windows (WSL2) | Free; no Container Apps cost |
| 2026-11-03 | Remove ACR + placeholder Container App | Unused; ~$12/month saved |
| 2026-11-03 | Include Challenge 2 | Requested; demo itself ships its own RepairPlanner |
| 2026-11-03 | `docs/` is the source of truth | Requested |
| 2026-11-04 | Patch APIM `get-threshold` policy to return all thresholds of a machine type (deviation from the workshop) | Original returns only the first threshold, so multi-metric anomalies (machine-001 pressure, machine-005 power/vibration, machine-003 throughput) were not validated; the demo scenario needs them. Done in `create-apim-mcp-servers.sh`, not in the original seed script |
| 2026-11-04 | AI Search in **francecentral**, rest in swedencentral | swedencentral and germanywestcentral had no Basic Search capacity; France Central was the first region that worked. Cross-region use with Foundry is fine (key-based connection). `demo-up.sh --search-location` parameter added (default now `francecentral`) |
| 2026-11-04 | **Remove API Management from the default setup**; MCP tools run on an **Azure Functions (Flex Consumption, Python) MCP server** with key auth | APIM BasicV2 is $0.27/h (corrected price) and only served two MCP tools for two Foundry agents. Functions costs ~$0, is permanent (no recreate, no changing key, no $6.50/day risk) and is a documented Foundry pattern. Challenge 4, 2 and 3 never used APIM. Lost: the live "APIM as AI gateway via portal" part of Challenge 1 → kept as optional `--with-apim` (show-only, not wired to the agents) |
| 2026-11-04 | One MCP server for both tools; both MCP agents use `allowed_tools` per label | Without it each agent saw 4 tools (duplicate names across two labels) and the anomaly agent flagged in-range values far more often (14/14 vs 4/14 on APIM). With `allowed_tools`: 4/8 (same as baseline) |
| 2026-11-04 | **AI Search Free tier** (permanent) instead of Basic (ephemeral) | Verified: knowledge source, index, knowledge base, retrieval and MCP via the project identity all work on Free. $0, nothing to tear down. Fallback: `searchServiceSku=basic`. Limits: 50 MB, 3 indexes, 1 free service per subscription |
| 2026-11-04 | Optional APIM deployment named `demo-optional-apim` (not `hack-deployment-*`) | `get-keys.sh` takes the newest deployment matching `hack-deployment` and reads its outputs; the APIM template has none → wrong project name in `.env` (found during the rehearsal) |
| 2026-11-04 | Deploy the function with `az functionapp deployment source config-zip` and vendored Linux wheels | `az functionapp deploy --type zip` returned HTTP 415 on this Flex app; Core Tools' npm install is blocked; vendoring avoids remote build |

## Open risks
| Risk | Impact | Mitigation |
|---|---|---|
| gpt-5.4 / gpt-5.4-mini quota is 0 in the VS/MPN subscription | Agents cannot run | Checked 2026-11-04: quota is sufficient (250 / 1000) |
| Microsoft deletes the inactive **Free** AI Search service | Fault Diagnosis agent loses its knowledge base | `demo-up.sh` recreates the service and the knowledge base (rehearsed, ~13 min). Run it ~30 min before a demo |
| Free Search capacity: not available in swedencentral on 2026-11-04 | `demo-up.sh` fails when recreating | Default region `francecentral`; override with `--search-location`; fallback `searchServiceSku=basic` |
| A just-deleted Search name is blocked for several minutes (`ServiceDeleting`) | Recreate right after delete fails | `demo-up.sh` retries every 90 s (up to 8 times) |
| Function key rotation or accidental key reset | MCP connections return 401 | Re-run `scripts/deploy-mcp-function.sh` (re-creates both connections with the new key) |
| Cold start of the Flex function after idle (a few seconds) | First MCP tool call is slow | Acceptable; do a smoke test before the demo |
| Workshop's anomaly agent is non-deterministic for in-range values (e.g. drum_vibration 2.1 → sometimes a warning) | Confusing live demo case | Use the documented demo payload (machine-001, 179.2 / 14.5); same behavior on APIM |
| Repair Planner sometimes creates 2 work orders per run | Extra Cosmos documents | `scripts/reset-demo-data.sh` before/after demos |
| Optional APIM (`--with-apim`) left running | $0.27/h (~$6.50/day) | `demo-down.sh` prints the remaining hourly-billed services; budget alerts |
| Repo scripts are bash; apphost uses Linux cert paths | Fail on native Windows | Use WSL2 |
| VS subscription spending limit | Resources disabled | Budget alerts; monitor credit |
| Key auth + public endpoints | Not production-grade | Acceptable for demo; mention in the demo |

## Closed risks (kept for history)
| Risk | Resolution |
|---|---|
| APIM/Search cannot be stopped, only deleted; forgetting costs money | No hourly-billed services remain by default |
| APIM recreate takes 15–25 min and changes subscription key / Cosmos role | APIM no longer part of the default setup |
| Stale Cosmos role assignment after APIM recreate | Only relevant for `--with-apim`: `demo-up.sh` cleans orphans and re-grants the role |
