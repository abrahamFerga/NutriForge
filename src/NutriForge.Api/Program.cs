var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry + health checks + service discovery + HTTP resilience.
builder.AddServiceDefaults();
builder.Services.AddOpenApi();

var app = builder.Build();

// /health (ready) and /alive (live).
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Placeholder — replaced by the Food / Tracking / Recipes / DietGen endpoint
// groups as each ROADMAP phase lands.
app.MapGet("/ping", () => Results.Ok(new { status = "ok", service = "nutriforge-api" }));

app.Run();
