# 06 – Risks and decisions

Back to [wiki home](README.md)

## Decision log
| Date | Decision | Reason |
|---|---|---|
| 2026-11-03 | New RG `rg-tire-factory-demo` in `swedencentral` | Template-allowed region, default |
| 2026-11-03 | Strategy A: delete APIM + AI Search between demos | Together ~$220/month, budget is $120 |
| 2026-11-03 | Run demo app locally on Windows (WSL2) | Free; no Container Apps cost |
| 2026-11-03 | Remove ACR + placeholder Container App | Unused; ~$12/month saved |
| 2026-11-03 | Include Challenge 2 | Requested; demo itself ships its own RepairPlanner |
| 2026-11-03 | `docs/` is the source of truth | Requested |
| 2026-11-04 | AI Search in **francecentral**, rest in swedencentral | swedencentral and germanywestcentral had no Basic Search capacity; France Central was the first region that worked. Cross-region use with Foundry is fine (key-based connection). `demo-up.sh --search-location` parameter added; capacity may differ on the next recreate |

## Open risks
| Risk | Impact | Mitigation |
|---|---|---|
| gpt-5.4 / gpt-5.4-mini quota is 0 in the VS/MPN subscription | Agents cannot run | Check quota first (Phase 0); request quota or switch region/model |
| APIM/Search cannot be stopped, only deleted | Cost if forgotten | Budget alerts, `demo-down.sh`, calendar reminder after demos |
| Recreate takes 15–25 min | Demo delay | Run `demo-up.sh` 60 min before |
| Stale Cosmos role assignment after APIM recreate (new managed identity) | APIM→Cosmos 403 | `demo-up.sh` removes old assignment and creates new |
| APIM subscription key / MCP URL change after recreate | Foundry connections break | Deterministic names; re-save keys in project connections in `demo-up.sh` |
| Repo scripts are bash; apphost uses Linux cert paths | Fail on native Windows | Use WSL2 |
| AI Search Basic capacity not available in a region at recreate time | `demo-up.sh` fails after APIM is already created | Re-run with `--search-location <region>` (francecentral worked on 2026-11-04). Start `demo-up` early (T-60 min) to leave time |
| VS subscription spending limit | Resources disabled | Budget alerts; monitor credit |
| Key auth + public endpoints | Not production-grade | Acceptable for demo; mention in the demo |
