using NutriForge.Infrastructure;
using NutriForge.Infrastructure.RecipeImport;
using NutriForge.ImportWorker;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry + health checks + resilience, same as the API.
builder.AddServiceDefaults();

// Persistence + caching + clock (Aspire-wired appdb + cache) so background loops can touch the DB.
// NOTE: the shared background relays are NOT registered here (the API host owns them) to avoid a
// double fan-out; this host registers only its own workers.
builder.AddInfrastructure();

// Recipe import connectors: transcript enrichment (opt-in, ADR-0015) is activated here
// when TranscriptEnrichment:Provider != "none". No YouTube API key needed for the worker.
builder.Services.AddRecipeImport(config: builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
