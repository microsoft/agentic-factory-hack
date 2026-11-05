#!/bin/bash
#
# create-knowledge-base.sh - scripted version of challenge-1/create_knowledge_base.ipynb
#
# Runs the notebook's code cells (knowledge source, knowledge base, retrieval test, project connection)
# so the Foundry IQ knowledge base can be recreated after AI Search has been deleted and recreated.
# Waits for the search index to be populated before testing retrieval. Idempotent.
#
# Run inside WSL after demo-up.sh (demo-up.sh calls it as a post-up hook).
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ -f "$HOME/factory-venv/bin/activate" ]; then
    # shellcheck disable=SC1091
    source "$HOME/factory-venv/bin/activate"
fi

set -a
# shellcheck disable=SC1091
source "$REPO_ROOT/.env"
set +a

export NOTEBOOK="$REPO_ROOT/challenge-1/create_knowledge_base.ipynb"
export ENV_FILE="$REPO_ROOT/.env"
cd "$REPO_ROOT/challenge-1"

python -u - <<'PY'
import json
import os
import time

import dotenv

# load_dotenv() cannot auto-locate .env for code that comes from stdin, so point it at the repo-root file
_load = dotenv.load_dotenv
dotenv.load_dotenv = lambda *args, **kwargs: _load(os.environ["ENV_FILE"], override=True)

nb = json.load(open(os.environ["NOTEBOOK"], encoding="utf-8"))
code_cells = ["".join(c["source"]) for c in nb["cells"] if c["cell_type"] == "code"]
# 0 imports/config, 1 knowledge source, 2 knowledge base, 3 retrieval test, 4 project connection
ns = {"__name__": "__main__"}

print("==> Imports and configuration")
exec(code_cells[0], ns)
print("==> Knowledge source")
exec(code_cells[1], ns)
print("==> Knowledge base")
exec(code_cells[2], ns)

index_client = ns["index_client"]
index_name = f"{ns['knowledge_source_name']}-index"
print(f"==> Waiting for index '{index_name}' to be populated (up to 10 min)")
for attempt in range(40):
    try:
        count = index_client.get_index_statistics(index_name).get("documentCount", 0)
    except Exception as exc:
        count = -1
        print(f"    (index not ready yet: {str(exc)[:100]})")
    print(f"    attempt {attempt + 1}: documents={count}")
    if count and count > 0:
        break
    time.sleep(15)
else:
    raise SystemExit("Index was not populated in time")

print("==> Retrieval test")
for attempt in range(5):
    try:
        exec(code_cells[3], ns)
        break
    except Exception as exc:
        print(f"    retrieval attempt {attempt + 1} failed: {str(exc)[:150]}")
        time.sleep(30)
else:
    raise SystemExit("Retrieval test failed")

print("==> Project connection for the knowledge base MCP endpoint")
exec(code_cells[4], ns)
PY

echo "==> Knowledge base ready: ${SEARCH_SERVICE_ENDPOINT}knowledgebases/machine-kb/mcp"
