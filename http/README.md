# HTTP request catalog

One `.http` file per area, one request per endpoint — the deterministic, reviewable way to
exercise the API by hand or from an AI agent. Works with the VS Code REST Client, Visual Studio,
and Rider.

## Host

The API runs behind Aspire, which assigns a port dynamically. Start the stack
(`scripts/run-and-wait.ps1` or `dotnet run --project src/NutriForge.AppHost`), open the Aspire
dashboard, copy the **api** resource's HTTPS endpoint, and set it as `@host` at the top of each
file (or in a shared `http-client.env.json`).

## Auth (local dev)

In Development the API uses a dev-auth scheme: send `X-Debug-Subject` to act as a user and
`X-Debug-Role` (`user` or `admin`) to pick the role. **No subject header ⇒ anonymous** (the
protected endpoints return 401). In a real deployment these are replaced by an
`Authorization: Bearer <oidc-token>` header — never inline a real token; pull it from env.

Writes carry an `Idempotency-Key` (a fresh GUID) exactly as a real client would.
