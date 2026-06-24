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

// Integration tests run the AppHost headless: no SPA, and the NutritionAssistant deliberately
// unconfigured so they can assert its 503 graceful-degradation path. They signal this via
// SKIP_NPM_APPS, which therefore also exempts them from the OpenAI-key requirement below.
var underTest = string.Equals(builder.Configuration["SKIP_NPM_APPS"], "true", StringComparison.OrdinalIgnoreCase);

// The MAF NutritionAssistant is a core capability, not an optional add-on: a real run MUST be
// configured with an OpenAI key. Express that the Aspire way — a first-class secret *parameter* —
// instead of hand-throwing. An unresolved parameter is surfaced by Aspire with a clear, masked
// "set a value for openai-api-key" diagnostic and keeps the API from starting, so the key is
// genuinely required for every run. Provide it via (in order of precedence):
//   • user-secrets:  dotnet user-secrets set Parameters:openai-api-key "sk-..."  (recommended)
//   • the OPENAI_API_KEY env var (bridged below, so existing scripts keep working)
//   • the Aspire dashboard's "set parameter value" prompt
if (!underTest)
{
    if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } envKey)
    {
        builder.Configuration["Parameters:openai-api-key"] = envKey;
    }

    var openAiKey = builder.AddParameter("openai-api-key", secret: true);

    api.WithEnvironment("Ai__Provider", "OpenAI")
        .WithEnvironment("Ai__Model", Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL_NAME") ?? "gpt-4o-mini")
        .WithEnvironment("Ai__ApiKey", openAiKey);
}

// SPA — Vite + React dev server. The API origin is injected so the browser talks to the right
// backend. Skipped under integration tests (SKIP_NPM_APPS=true) so they don't boot the Node dev
// server.
if (!underTest)
{
    var web = builder.AddNpmApp("web", "../NutriForge.Web", "dev")
        .WithReference(api).WaitFor(api)
        .WithEnvironment("VITE_API_BASE", api.GetEndpoint("http"))
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .PublishAsDockerFile();

    // Tell the API the browser-facing SPA origin so its CORS allow-list matches. Aspire assigns
    // the Vite endpoint a dynamic port, so a hard-coded `http://localhost:5173` would never match
    // and every cross-origin request would fail with "Failed to fetch". This is an env injection,
    // not a WaitFor, so it introduces no startup cycle (the API does not wait on the SPA).
    api.WithEnvironment("Cors__SpaOrigin", web.GetEndpoint("http"));
}

// Import worker — nightly USDA / Open Food Facts sync; never on a request path.
builder.AddProject<Projects.NutriForge_ImportWorker>("import-worker")
    .WithReference(appDb).WaitFor(appDb)
    .WithReference(cache).WaitFor(cache);

builder.Build().Run();
