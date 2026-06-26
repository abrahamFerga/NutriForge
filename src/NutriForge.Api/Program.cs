using System.Text.Json.Serialization;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using NutriForge.Api.Auth;
using NutriForge.Api.Endpoints;
using NutriForge.Api.Middleware;
using NutriForge.Api.Setup;
using NutriForge.Application;
using NutriForge.Application.Abstractions;
using NutriForge.Infrastructure;
using NutriForge.Infrastructure.Ai;
using NutriForge.Infrastructure.OpenFoodFacts;
using NutriForge.Infrastructure.RecipeImport;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF Community licence (free for OSS / small orgs) — required before any PDF is generated.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Don't advertise the server stack in responses (#57).
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// OpenTelemetry + health checks + service discovery + HTTP resilience.
builder.AddServiceDefaults();

// Export the product-KPI meter (#50) so the dashboards/alerts can read the SPEC success metrics.
builder.Services.ConfigureOpenTelemetryMeterProvider(m => m.AddMeter(NutriForge.Application.Observability.NutriForgeMetrics.MeterName));

// Persistence, caching, audit-outbox, clock (Aspire-wired Postgres + Redis).
builder.AddInfrastructure();
// The background relays (outbox dispatch, diet generation) run in the API host today.
builder.AddInfrastructureBackgroundServices();

// The agentic layer: MAF NutritionAssistant (provider-swappable; unconfigured ⇒ 503).
builder.AddAiAssistant();

// Open Food Facts connector — barcode fetch-on-miss for low-friction logging.
builder.Services.AddOpenFoodFacts();

// Recipe-import connectors (YouTube metadata). The Data API key is optional — without it, import
// falls back to oEmbed title/thumbnail + pasted recipe text (no auto-fetched description).
builder.Services.AddRecipeImport(builder.Configuration["YouTube:ApiKey"], builder.Configuration);

// The request-scoped authenticated principal (overrides the worker's system principal).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Application services + validators.
builder.Services.AddApplication();

// AuthN/Z, rate limiting, Problem Details, CORS, JSON.
builder.Services.AddNutriForgeAuth(builder.Configuration, builder.Environment);
builder.Services.AddNutriForgeRateLimiting();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var spaOrigin = builder.Configuration["Cors:SpaOrigin"] ?? "http://localhost:5173";
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(spaOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

// Defense-in-depth (#57): HSTS (HTTPS, deployed only) + strict security headers on every response.
// The API is stateless/token-based and sets NO cookies, so there's no cookie surface to secure here
// (auth is a Bearer token / dev header); the SPA owns its own cookie/session posture.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();

app.MapDefaultEndpoints(); // /health + /alive (anonymous)

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<UserProvisioningMiddleware>(); // mirror OIDC subject → local user id
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();      // replay-safe writes (needs the local user id)

app.MapGet("/", () => Results.Ok(new { service = "nutriforge-api", status = "ok" }))
    .AllowAnonymous().ExcludeFromDescription();

app.MapFoodEndpoints();
app.MapTrackingEndpoints();
app.MapMeEndpoints();
app.MapAdminEndpoints();
app.MapAssistantEndpoints();
app.MapRecipeEndpoints();
app.MapPlanningEndpoints();
app.MapDietPlanEndpoints();
app.MapHouseholdEndpoints();
app.MapDietTemplateEndpoints();
app.MapNotificationEndpoints();
app.MapWhatsAppInboundEndpoints();

await app.InitializeDatabaseAsync();

await app.RunAsync();

/// <summary>Exposed so the Aspire integration tests can reference the API entry point.</summary>
public partial class Program;
