# Boot the whole NutriForge stack (API + Postgres + Redis + SPA) via the Aspire AppHost and
# stream its output. Watch the console for the dashboard URL; the per-resource endpoints (the
# API base URL to paste into the http/*.http files) are listed on the dashboard.
#
# Usage:   ./scripts/run-and-wait.ps1            # full stack incl. the React SPA
#          ./scripts/run-and-wait.ps1 -NoSpa     # API + datastores only (no Node dev server)
param([switch]$NoSpa)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if ($NoSpa) { $env:SKIP_NPM_APPS = 'true' }
$env:ASPNETCORE_ENVIRONMENT = 'Development'

Write-Host 'Starting NutriForge AppHost (Ctrl+C to stop)...' -ForegroundColor Cyan
Write-Host 'Requires Docker running. First run pulls the Postgres + Redis images.' -ForegroundColor DarkGray

dotnet run --project (Join-Path $repoRoot 'src/NutriForge.AppHost')
