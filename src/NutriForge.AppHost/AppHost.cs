// NutriForge Aspire AppHost — the local-dev orchestration entry point.
// Composes the operational + audit Postgres databases (ADR-0011), Redis, the API
// (the deployable modular monolith), and the nightly import worker. Cloud
// provisioning is owned by Terraform (ADR-0013), not `aspire deploy`, so no
// compute-environment is declared here.

var builder = DistributedApplication.CreateBuilder(args);

// Integration tests run the AppHost headless (SKIP_NPM_APPS=true): no SPA, the assistant left
// unconfigured (to assert its 503 path), the OpenAI key not required, and an EPHEMERAL database
// (a fresh, deterministic DB each run). A normal `dotnet run` / `aspire run` is the opposite.
var underTest = string.Equals(builder.Configuration["SKIP_NPM_APPS"], "true", StringComparison.OrdinalIgnoreCase);

// Postgres with an Aspire-generated password (persisted to user-secrets for `dotnet run`, so it is
// stable across restarts). Production does NOT run this AppHost — Terraform/Container Apps inject the
// real connection strings (ADR-0013). For local dev we attach a DATA VOLUME so your data (profile,
// diary, recipes, plans) survives a restart; tests stay volumeless so each run starts clean.
var postgres = builder.AddPostgres("postgres");
if (!underTest)
{
    postgres.WithDataVolume();
}

var appDb = postgres.AddDatabase("appdb");       // foods, recipes, diary, plans, outbox, idempotency
var auditDb = postgres.AddDatabase("auditdb");   // append-only audit, outside the operational DB

var cache = builder.AddRedis("cache");           // search/parse/intent cache, idempotency replay, rate limits

// API — every bounded context in-process (modular monolith).
var api = builder.AddProject<Projects.NutriForge_Api>("api")
    .WithReference(appDb).WaitFor(appDb)
    .WithReference(auditDb).WaitFor(auditDb)
    .WithReference(cache).WaitFor(cache)
    .WithExternalHttpEndpoints();

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
    // Precedence: user-secrets (Parameters:openai-api-key) wins; OPENAI_API_KEY only fills in when
    // user-secrets didn't set it (note the ??=); otherwise the parameter is unresolved and Aspire
    // prompts. REQUIRED — an unresolved parameter blocks API startup (the assistant is a core capability).
    if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } envKey)
    {
        builder.Configuration["Parameters:openai-api-key"] ??= envKey;
    }

    var openAiKey = builder.AddParameter("openai-api-key", secret: true);

    api.WithEnvironment("Ai__Provider", "OpenAI")
        .WithEnvironment("Ai__Model", Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL_NAME") ?? "gpt-4o-mini")
        .WithEnvironment("Ai__ApiKey", openAiKey);

    // YouTube Data API key — OPTIONAL. With it, recipe import auto-reads the video DESCRIPTION (where the
    // recipe usually lives); without it, import degrades to keyless oEmbed (title + thumbnail) + pasted
    // recipe text. So unlike the OpenAI key it must NEVER block startup. We still surface it as a
    // first-class secret parameter (settable in the Aspire dashboard / user-secrets, just like the OpenAI
    // key) but give it an EMPTY DEFAULT, set LAST so it only applies when neither user-secrets nor the
    // YOUTUBE_API_KEY env supplied a value — an unset key then resolves to "" instead of halting the app
    // (and an empty key makes YouTubeMetadataClient fall back to oEmbed).
    if (Environment.GetEnvironmentVariable("YOUTUBE_API_KEY") is { Length: > 0 } ytKey)
    {
        builder.Configuration["Parameters:youtube-api-key"] ??= ytKey;
    }

    builder.Configuration["Parameters:youtube-api-key"] ??= "";

    var youTubeKey = builder.AddParameter("youtube-api-key", secret: true);
    api.WithEnvironment("YouTube__ApiKey", youTubeKey);
}

// Twilio WhatsApp channel (#96) — OPTIONAL. Enables real WhatsApp notifications; without it the
// app falls back to the MockChannel (stored in the DB outbox). Credentials come from user-secrets
// or env vars; missing values default to "" so startup is never blocked.
if (!underTest)
{
    builder.Configuration["Parameters:twilio-account-sid"] ??= Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID") ?? "";
    builder.Configuration["Parameters:twilio-auth-token"] ??= Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN") ?? "";
    var twilioSid = builder.AddParameter("twilio-account-sid", secret: false);
    var twilioToken = builder.AddParameter("twilio-auth-token", secret: true);
    api.WithEnvironment("Channels__WhatsApp__Provider", "twilio")
       .WithEnvironment("Channels__WhatsApp__Twilio__AccountSid", twilioSid)
       .WithEnvironment("Channels__WhatsApp__Twilio__AuthToken", twilioToken);
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
