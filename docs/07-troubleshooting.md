# 07 – Troubleshooting

Back to [wiki home](README.md)

Add every issue we hit here with symptom, cause and fix.

| # | Symptom | Cause | Fix |
|---|---|---|---|
| 1 | Deployment fails with quota / `InsufficientQuota` on model deployment | No quota for gpt-5.4 in region | `az cognitiveservices usage list -l swedencentral`; request quota or lower capacity / change model |
| 2 | `seed-data.sh` skips APIM seeding | Missing `APIM_NAME`, `COSMOS_ENDPOINT` or not logged in | Re-run `get-keys.sh`, `az login`, re-run script |
| 3 | APIM returns 403/401 from Cosmos after recreate | New APIM identity has no Cosmos data role | Recreate sqlRoleAssignment (handled in `demo-up.sh`) |
| 4 | Shell variables empty in new terminal | `.env` not exported | `export $(cat ../.env \| xargs)` |
| 5 | Agent cannot create/run in Foundry (403) | Missing roles | Assign roles from [04-environment-and-config.md](04-environment-and-config.md), refresh `az login` |
| 6 | Search endpoint URL concatenation fails | Missing trailing slash | `get-keys.sh` normalises to one trailing slash |
