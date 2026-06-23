// NutriForge Aspire AppHost — the local-dev orchestration entry point.
// Composes the operational + audit Postgres databases (ADR-0011), Redis, the API
// (the deployable modular monolith), and the nightly import worker. Cloud
// provisioning is owned by Terraform (ADR-0013), not `aspire deploy`, so no
// compute-environment is declared here.

var builder = DistributedApplication.CreateBuilder(args);

// Postgres password is a secret parameter -> user-secrets (dev) / env (CI).
var dbPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: dbPassword)
    .WithLifetime(ContainerLifetime.Persistent) // reuse the container across `aspire run`
    .WithDataVolume();                           // persist data between runs

var appDb = postgres.AddDatabase("appdb");       // foods, recipes, diary, plans, outbox, idempotency
var auditDb = postgres.AddDatabase("auditdb");   // append-only audit, outside the operational DB

var cache = builder.AddRedis("cache");           // search/parse/intent cache, idempotency replay, rate limits

// API — every bounded context in-process (modular monolith).
var api = builder.AddProject<Projects.NutriForge_Api>("api")
    .WithReference(appDb).WaitFor(appDb)
    .WithReference(auditDb).WaitFor(auditDb)
    .WithReference(cache).WaitFor(cache)
    .WithExternalHttpEndpoints();

// SPA — Vite + React dev server. The API origin is injected so the browser talks to the right
// backend (the API's CORS policy allows this origin).
builder.AddNpmApp("web", "../NutriForge.Web", "dev")
    .WithReference(api).WaitFor(api)
    .WithEnvironment("VITE_API_BASE", api.GetEndpoint("http"))
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

// Import worker — nightly USDA / Open Food Facts sync; never on a request path.
builder.AddProject<Projects.NutriForge_ImportWorker>("import-worker")
    .WithReference(appDb).WaitFor(appDb)
    .WithReference(cache).WaitFor(cache);

builder.Build().Run();
