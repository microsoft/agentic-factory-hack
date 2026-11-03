# 03 – Progress tracker

Back to [wiki home](README.md) · Details in [01-plan.md](01-plan.md)

Status: ⬜ pending · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Depends on | Status | Date | Notes |
|---|---|---|---|---|---|
| prep-account | Account, providers, budget + alerts | – | ⬜ | | |
| check-quota | Model quota in swedencentral | – | ⬜ | | |
| install-tools | WSL2, az, .NET 10, Aspire, uv, Node, Python | – | ⬜ | | |
| split-templates | Core + ephemeral templates, deterministic names | – | ⬜ | | |
| demo-scripts | `demo-up.sh` / `demo-down.sh` | split-templates | ⬜ | | |
| deploy-core | RG + core template | prep-account, check-quota, split-templates | ⬜ | | |
| deploy-ephemeral | APIM + AI Search | deploy-core | ⬜ | | |
| keys-seed | get-keys, seed-data, roles, smoke test | deploy-ephemeral, install-tools | ⬜ | | |
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
