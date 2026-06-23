#!/usr/bin/env bash
# Boot the whole NutriForge stack (API + Postgres + Redis + SPA) via the Aspire AppHost.
# Watch the console for the dashboard URL; the per-resource endpoints (the API base URL to paste
# into http/*.http) are listed on the dashboard. Requires Docker running.
#
# Usage:  ./scripts/run-and-wait.sh           # full stack incl. the React SPA
#         ./scripts/run-and-wait.sh --no-spa  # API + datastores only (no Node dev server)
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "${1:-}" == "--no-spa" ]]; then
  export SKIP_NPM_APPS=true
fi
export ASPNETCORE_ENVIRONMENT=Development

echo "Starting NutriForge AppHost (Ctrl+C to stop)..."
echo "First run pulls the Postgres + Redis images."
exec dotnet run --project "$repo_root/src/NutriForge.AppHost"
