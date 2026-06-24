# NutriForge — working agreement

## Runtime verification is part of "done" (not optional)

Whenever I add or change a feature, building and passing unit tests is **not** sufficient.
Before calling the work done I must prove it behaves correctly at runtime:

1. **Run the app** via the Aspire AppHost:
   `dotnet run --project src/NutriForge.AppHost`
   (or `aspire run`). This brings up Postgres, Redis, the API, the import worker, and the
   Vite SPA, plus the Aspire dashboard.
2. **Exercise the feature** the way a user would:
   - Drive the API directly with the committed `.http` catalogs under `http/`
     (`foods.http`, `tracking.http`, `recipes-and-plan.http`, `me-and-admin.http`,
     `assistant.http`).
   - Drive the SPA in the browser for UI-facing changes.
3. **Observe via the Aspire MCP.** Connect to the Aspire MCP and read the live
   OpenTelemetry (structured logs, traces, metrics) to confirm the expected behavior and
   to diagnose anything unexpected — e.g. the `CommandError` / request-failure entries on
   the `api` resource. If the MCP is not connected, fall back to the Aspire dashboard
   (Structured logs / Traces) at the URL the AppHost prints on startup.
4. **If something is off, debug from telemetry, fix, and re-run** until the feature works.
5. **Lock it in** with a regression test that fails without the fix (see the
   `verify-runtime` skill for the harness: Aspire integration-test host + `.http` catalog
   + Playwright E2E for the SPA).

The `verify-runtime` skill drives this run → exercise → observe → debug → fix → lock-in
loop end to end — prefer invoking it for feature verification.

### Watch-outs learned from real failures
- A failing `SELECT ... FROM "__EFMigrationsHistory"` at startup is **normal** EF probing of
  a fresh database (the history table doesn't exist yet); EF catches it and creates the
  table. It is not the cause of a UI "Failed to fetch".
- Browser **"Failed to fetch"** with no corresponding API error is almost always a
  **CORS** block (browser-side), not a server fault. The SPA origin must be in the API's
  CORS allow-list. Aspire assigns the Vite endpoint a dynamic port, so the AppHost injects
  the SPA's real origin into the API as `Cors__SpaOrigin` (see `AppHost.cs`).
- **Exercise with concurrency, not just sequential calls.** A browser fires several API
  calls *in parallel* on page load. Sequential `curl`/`.http` checks pass while a real load
  500s — e.g. the just-in-time user-provisioning race in `UserProvisioningMiddleware` (two
  first-touch requests both insert the same `OidcSubject` → Postgres `23505`). When a code
  path does check-then-insert, verify it under a parallel burst (separate connections), and
  lock it in with a concurrency regression test (see
  `Concurrent_first_requests_for_a_new_subject_do_not_500_on_the_provisioning_race`). Note:
  a shared HTTP/2 client serializes the burst onto one connection and hides the race — use
  one client per request.
