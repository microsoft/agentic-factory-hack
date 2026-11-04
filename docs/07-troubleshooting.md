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
| 7 | `set: pipefail: invalid option name` / `\r` errors running a `.sh` in WSL | CRLF line endings (git `autocrlf=true`) | `.gitattributes` forces LF for `*.sh`; re-checkout the script (`git rm --cached` is not needed: delete file, `git checkout -- <file>`) |
| 8 | `pip install` says externally-managed-environment in WSL | Ubuntu 24.04 PEP 668 | Use the venv: `source ~/factory-venv/bin/activate` (`demo-up.sh` activates it automatically) |
| 9 | Redeploying ephemeral fails with `RoleAssignmentUpdateNotPermitted` / Cosmos role assignment conflict | Old Search/APIM identity left an orphan role assignment with the same deterministic name | `demo-down.sh` removes them before deleting; `demo-up.sh` also cleans orphans. Manual: delete the role assignment whose principal shows as "Unknown" |
| 10 | `ServiceAlreadyExists` / name conflict when recreating APIM | APIM is soft-deleted after delete | `az apim deletedservice purge --service-name <name> --location swedencentral` (done by both scripts) |
| 11 | Core deploy: `RequestConflict: Another operation is in progress` on the Foundry account | Project and model deployments were created in parallel on the same account | Fixed in `azuredeploy.demo-core.json`: first model deployment now depends on the project connection (serialises the operations) |
| 12 | Core deploy: `An Application Insights instrumentation key is not supported. Provide the full Application Insights connection string` | Foundry project connection credential changed | Fixed: connection `credentials.key` now uses `ConnectionString` (not `InstrumentationKey`) |
| 13 | Ephemeral deploy: `InsufficientResourcesAvailable ... region 'swedencentral' is currently out of the resources` for AI Search | No Basic-tier Search capacity in the region (also germanywestcentral on 2026-11-04) | `demo-up.sh --search-location francecentral` (or probe other regions with a throwaway `az search service create`). APIM + Foundry stay in swedencentral |
| 14 | Running `wsl -d Ubuntu-24.04 -- bash -c '...$VAR...'` from PowerShell: shell variables come out empty | `wsl --` passes the command through an outer shell that expands `$VAR` first | Use `wsl -d Ubuntu-24.04 --exec bash -c '...'` (single quotes in PowerShell), or run commands inside an interactive WSL terminal |
| 15 | `az role assignment create --query "{role:roleDefinitionName}"` prints `role: null` | Output shape of `create`; assignment is fine | Verify with `az role assignment list --assignee <id> --all` |
| 16 | `AssertionError` in `dotenv.find_dotenv` when running notebook code via `python -` | `load_dotenv()` cannot locate `.env` for stdin code | `create-knowledge-base.sh` patches `load_dotenv` to use the repo-root `.env` path |
| 17 | Waiting for the search index never ends (`documents=-1`) | `get_index_statistics()` returns camelCase `documentCount`, not `document_count` | Fixed in `create-knowledge-base.sh` (`.get("documentCount")`) |
| 18 | Script output not visible while running in background | Python buffers stdout when not attached to a terminal | Run python with `-u` (done in the scripts) |
| 19 | (Precaution, not yet observed) Adding a tool to a just-created APIM MCP server could fail with 404 | MCP server is created asynchronously | `create-apim-mcp-servers.sh` retries adding the tool for up to ~2.5 min |
