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
| deploy-ephemeral | APIM + AI Search | deploy-core | ✅ | 2026-11-04 | APIM BasicV2 swedencentral; Search basic **francecentral** (swedencentral + germanywestcentral had no capacity, #13). **Billing started ~11:20 – run `demo-down.sh` when not demoing** |
| keys-seed | get-keys, seed-data, roles, smoke test | deploy-ephemeral, install-tools | ✅ | 2026-11-04 | Seeded 10 Cosmos containers, 5 wiki blobs, 2 APIM APIs; 3 roles assigned; APIM `/machine`, `/maintenance` return 200 |
| challenge1 | Anomaly, MCP servers, KB, Fault Diagnosis | keys-seed | ⬜ | | |
| challenge2 | C# Repair Planner via agentplanning | keys-seed | ⬜ | | |
| challenge3 | Scheduler + Parts Ordering | keys-seed | ⬜ | | |
| challenge4 | `aspire run` end-to-end demo | challenge1, challenge3 | ⬜ | | |
| rehearse-teardown | demo-down then demo-up, retest | challenge4, demo-scripts | ⬜ | | |
| demo-runbook | Final runbook | rehearse-teardown | ⬜ | | |

## Change log
| Date | Change |
|---|---|
| 2026-11-03 | Wiki created, plan agreed (strategy A, local Aspire, Challenge 2 included) |
| 2026-11-04 | Phase 0 complete: login, providers, budget, quota check, WSL tooling. Added `.gitattributes` (LF for `*.sh`) and `scripts/setup-wsl-tools.sh` |
| 2026-11-04 | Phase 1 complete: core + ephemeral templates, `demo-up.sh` / `demo-down.sh`; RG `rg-tire-factory-demo` created |
| 2026-11-04 | Phase 2 complete: core + ephemeral deployed; template fixes (project/model race, App Insights connection string, `searchLocation` parameter); `.env` generated |
| 2026-11-04 | Phase 3 complete: data seeded, roles assigned, APIM smoke test OK. Foundry portal check: project has AppInsights + AI Search connections, no agents yet (expected) |
