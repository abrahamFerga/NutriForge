# NutriForge Operations Runbook
<!-- Closes #53 -->

On-call guide for NutriForge. Covers reading telemetry, common failure patterns,
rollback, and the Postgres restore drill. Keep this file up to date as the system
evolves; stale runbooks are dangerous.

---

## 1. Health check (first 60 seconds on page)

```bash
# API liveness + readiness
curl -sf https://<api-host>/alive        # 200 = process alive
curl -sf https://<api-host>/health       # 200 = DB + Redis reachable

# Aspire dashboard (dev / staging)
# The AppHost prints the dashboard URL on startup — default is https://localhost:15888
```

**If `/alive` is down**: the process crashed. Check container/pod logs and restart.

**If `/health` is down but `/alive` is up**: a dependency (Postgres or Redis) is
unhealthy. See §3.

---

## 2. Reading telemetry

### Aspire dashboard (dev / staging)
1. Open the URL printed by the AppHost (e.g. `https://localhost:15888`).
2. **Structured logs** → filter by resource (`api`, `importworker`) and level ≥ Warning.
3. **Traces** → find the failing trace by trace-id from the error log; the waterfall shows
   which span failed (DB query, LLM call, etc.).
4. **Metrics** → check `http_server_request_duration_seconds` p99 and `nutriforge.*` KPIs.

### Azure Monitor (production)
* Log Analytics workspace: search for `AppRequests | where ResultCode >= 500`.
* Custom metrics namespace: `NutriForge` (see `NutriForgeMetrics.cs`).
* SLO dashboards are in the `nutriforge-slo` workbook (docs/sre/slos.yaml for definitions).

### Key metrics to check first
| Signal | Healthy range | Alert threshold |
|--------|--------------|-----------------|
| `http_server_request_duration_seconds` p99 | < 2 s | > 5 s |
| `nutriforge.diary.entries_logged` rate | proportional to users | sudden drop = logging broken |
| `nutriforge.plan.generated{feasible=true}` fraction | ≥ 90 % | < 80 % |
| Postgres connection pool wait | < 10 ms | > 100 ms |

---

## 3. Common failures

### 3.1 Database connection lost
**Symptoms**: `health` endpoint returns 503; structured logs show `NpgsqlException`.

**Steps**:
1. Verify Postgres is running: `pg_isready -h <host> -U nutriforge`.
2. Check the connection string in App Configuration / Key Vault (`ConnectionStrings__AppDb`).
3. Check Postgres logs for disk-full or OOM events.
4. If the pod restarted during a migration, check `__EFMigrationsHistory` for partial runs.
   A missing final row means the migration did not complete — run `dotnet ef database update`
   against the target environment.

**Note**: a failing `SELECT ... FROM "__EFMigrationsHistory"` at startup is **normal** EF
probing of a fresh database. The table is created automatically; this is not an error.

### 3.2 Redis unavailable
**Symptoms**: `health` endpoint 503; logs show `RedisConnectionException`; idempotency
middleware falls back to pass-through (idempotency checks are skipped, not blocking).

**Steps**:
1. Verify Redis: `redis-cli -h <host> ping`.
2. If Redis is permanently down, consider disabling `IdempotencyMiddleware` temporarily
   (feature flag or comment in `Program.cs`) until Redis is restored — the risk is duplicate
   writes on retried requests.

### 3.3 "Failed to fetch" in the browser (CORS)
**Symptoms**: browser DevTools shows a CORS error; the API has no corresponding error log.

**Root cause**: the SPA origin is not in the CORS allow-list. Aspire injects
`Cors__SpaOrigin` from the Vite endpoint's dynamic port into the API. If the AppHost
restarts and the port changes, a cached SPA might hit the wrong port.

**Fix**: hard-refresh the browser; the new SPA load will come from the current port.
In production, `Cors__SpaOrigin` is a static value in App Configuration.

### 3.4 User-provisioning race (23505 Postgres duplicate-key)
**Symptoms**: a user's very first page load returns 500; subsequent loads succeed.

**Root cause**: two parallel requests both try to insert the OIDC subject → local user
mapping. `UserProvisioningMiddleware` uses an `INSERT … ON CONFLICT DO NOTHING` upsert.
If this 500 reappears, verify the migration `AddUniqueIndexOnOidcSubject` is applied.

### 3.5 Plan generation stuck / timed out
**Symptoms**: `nutriforge.plan.generated` counter stops incrementing; outbox shows plans in
`GenerationRequested` status for > 5 min.

**Steps**:
1. Check `DietPlanGenerationWorker` logs for LLM call errors.
2. The LLM provider may be rate-limited or down — check its status page.
3. If no LLM key is configured, generation returns 503 immediately. This is expected; check
   `AiProvider__ApiKey` in App Configuration.
4. To unblock: update the plan record to `Failed` manually so the user can retry.

### 3.6 Import worker not processing
**Symptoms**: connector runs have `LastRunStatus = "pending"` for > 1 cycle.

**Steps**:
1. Check `NutriForge.ImportWorker` pod logs for unhandled exceptions.
2. Verify the connector's credentials in Key Vault (e.g. OpenFoodFacts has no auth; YouTube
   requires `YouTube__ApiKey`).
3. Restart the ImportWorker pod; it resumes from `LastRunAt`.

---

## 4. Rollback procedure

NutriForge uses blue/green deployments (Azure Container Apps revisions).

```bash
# List recent revisions
az containerapp revision list -g <rg> -n nutriforge-api --query "[].{name:name,active:properties.active}"

# Activate the previous revision
az containerapp revision activate -g <rg> -n nutriforge-api --revision <previous-revision>

# Deactivate the bad revision
az containerapp revision deactivate -g <rg> -n nutriforge-api --revision <bad-revision>
```

**Database migrations**: EF migrations are forward-only. If a migration introduced a breaking
schema change, restore from a PITR snapshot (§5) rather than attempting a manual reversal.

---

## 5. Postgres restore drill

The drill is automated: `.github/workflows/pitr-restore-drill.yml` runs quarterly
(08:00 UTC on 1 Jan/Apr/Jul/Oct) and can also be triggered manually from the Actions
tab. It creates a PITR restore server, verifies connectivity with `pg_isready`, then
deletes the drill server. Required GitHub secrets: `AZURE_SUBSCRIPTION_ID`,
`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`. Required repository variables: `PG_RESOURCE_GROUP`,
`PG_SERVER_NAME`. Optional: `PAGERDUTY_ROUTING_KEY` for failure alerting.

Run the manual steps below only if the automated drill fails or for ad-hoc investigation.

### 5.1 Verify backups exist
```bash
# Azure Database for Postgres Flexible Server
az postgres flexible-server backup list -g <rg> -n <pg-server> \
  --query "[?backupType=='Full'].{time:backupSetTime}" | head -5
```

### 5.2 Point-in-time restore (PITR) to a recovery instance
```bash
# Restore to a NEW server (never restore over production in place)
az postgres flexible-server restore \
  --resource-group <rg> \
  --name nutriforge-recovery \
  --source-server <pg-server> \
  --restore-time "2026-01-15T10:00:00Z"
```

### 5.3 Verify data integrity
```bash
psql "host=nutriforge-recovery.postgres.database.azure.com dbname=nutriforge user=nutriforge" \
  -c "SELECT COUNT(*) FROM diary_entries; SELECT COUNT(*) FROM users;"
```

### 5.4 Cut over (if PITR is for real recovery, not a drill)
1. Scale down the API and ImportWorker containers to 0.
2. Update `ConnectionStrings__AppDb` in App Configuration to point to the recovery instance.
3. Scale containers back up; verify `/health` returns 200.
4. Rename the recovery server to the production name (or update DNS).

**Recovery time objective (RTO)**: PITR completes in < 30 min for a < 50 GB database.
**Recovery point objective (RPO)**: Azure Postgres Flexible Server provides 5-min granularity.

---

## 6. Escalation contacts

| Tier | Who | When |
|------|-----|-------|
| On-call engineer | PagerDuty rotation | Any P1/P2 alert |
| Engineering lead | @abrahamFerga | > 30 min unresolved P1; deploy freeze decisions |
| Azure support | Portal support ticket | Azure platform failures |
