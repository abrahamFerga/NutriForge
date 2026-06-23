// NutriForge Aspire AppHost — the local-dev orchestration entry point.
// Composes the operational + audit Postgres databases (ADR-0011), Redis, the API
// (the deployable modular monolith), and the nightly import worker. Cloud
// provisioning is owned by Terraform (ADR-0013), not `aspire deploy`, so no
// compute-environment is declared here.

var builder = DistributedApplication.CreateBuilder(args);

// Postgres with an Aspire-generated password (persisted to user-secrets for `dotnet run`;
// regenerated per run in CI/tests, where containers start clean). Production does NOT run this
// AppHost — Terraform/Container Apps inject the real connection strings (ADR-0013) — so no
// hand-managed secret parameter is needed here. Ephemeral container (no data volume) keeps dev
// and integration-test runs deterministic: a fresh password never collides with a stale volume.
var postgres = builder.AddPostgres("postgres");

var appDb = postgres.AddDatabase("appdb");       // foods, recipes, diary, plans, outbox, idempotency
var auditDb = postgres.AddDatabase("auditdb");   // append-only audit, outside the operational DB

var cache = builder.AddRedis("cache");           // search/parse/intent cache, idempotency replay, rate limits

// API — every bounded context in-process (modular monolith).
var api = builder.AddProject<Projects.NutriForge_Api>("api")
    .WithReference(appDb).WaitFor(appDb)
    .WithReference(auditDb).WaitFor(auditDb)
    .WithReference(cache).WaitFor(cache)
    .WithExternalHttpEndpoints();

// Light up the MAF NutritionAssistant when an OpenAI key is present in the environment
// (`export OPENAI_API_KEY=sk-...` before `dotnet run`). No secret lives in source — it's read
// from the env and forwarded to the API's `Ai` config. Otherwise the assistant reports 503.
if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } openAiKey)
{
    api.WithEnvironment("Ai__Provider", "OpenAI")
        .WithEnvironment("Ai__Model", Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL_NAME") ?? "gpt-4o-mini")
        .WithEnvironment("Ai__ApiKey", openAiKey);
}

// SPA — Vite + React dev server. The API origin is injected so the browser talks to the right
// backend (the API's CORS policy allows this origin). Skipped under integration tests
// (SKIP_NPM_APPS=true) so they don't boot the Node dev server.
if (!string.Equals(builder.Configuration["SKIP_NPM_APPS"], "true", StringComparison.OrdinalIgnoreCase))
{
    builder.AddNpmApp("web", "../NutriForge.Web", "dev")
        .WithReference(api).WaitFor(api)
        .WithEnvironment("VITE_API_BASE", api.GetEndpoint("http"))
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .PublishAsDockerFile();
}

// Import worker — nightly USDA / Open Food Facts sync; never on a request path.
builder.AddProject<Projects.NutriForge_ImportWorker>("import-worker")
    .WithReference(appDb).WaitFor(appDb)
    .WithReference(cache).WaitFor(cache);

builder.Build().Run();
