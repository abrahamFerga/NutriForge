# NutriForge.E2E

Playwright end-to-end tests for the SPA + API.

## Run locally

1. Start the stack (needs Docker): `./scripts/run-and-wait.ps1` (or `dotnet run --project src/NutriForge.AppHost`).
2. From the Aspire dashboard, copy the **web** resource URL.
3. Then:

```bash
cd tests/NutriForge.E2E
npm install
npx playwright install chromium
E2E_BASE_URL=<web-url>   # e.g. http://localhost:5173
npm test
```

The SPA uses the dev-auth scheme, so the browser is "signed in" automatically — no login step.

## In CI

`.github/workflows/ci.yml` always installs and **lists** the specs (validates they compile) on every
PR; the full browser run executes only when the repo variable `E2E_BASE_URL` points at a running
(preview/deployed) stack. Wire that to a preview environment (backlog #66) to make E2E gate every PR.

> `node_modules`, `test-results/`, and `playwright-report/` are git-ignored.
