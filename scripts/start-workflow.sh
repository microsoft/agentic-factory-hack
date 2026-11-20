#!/bin/bash
#
# start-workflow.sh - start the Challenge 4 demo (Aspire host: workflow API, Python agents, React UI) in WSL.
#
# Loads the repo-root .env, then runs `aspire run`. Press CTRL+C to stop everything.
# First start takes a few minutes (uv sync + npm install); later starts are fast.
#
#   Frontend  : URL printed by `aspire describe` (resource "frontend", port changes per start)
#   Workflow  : http://localhost:5231   (POST /api/analyze_machine, /api/analyze_machine/stream, GET /health)
#   Dashboard : https://localhost:17072 (login link with token is printed at startup)
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ -f "$HOME/factory-venv/bin/activate" ]; then
    # shellcheck disable=SC1091
    source "$HOME/factory-venv/bin/activate"
fi
export PATH="$HOME/.aspire/bin:$PATH"

az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login --tenant 05baed4d-e049-45c7-b47d-c99c66fdb589 --use-device-code"; exit 1; }

set -a
# shellcheck disable=SC1091
source "$REPO_ROOT/.env"
set +a

cd "$REPO_ROOT/challenge-4/agent-workflow"
exec aspire run --non-interactive --nologo
