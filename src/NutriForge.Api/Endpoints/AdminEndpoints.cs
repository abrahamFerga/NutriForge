using NutriForge.Application.Authorization;

namespace NutriForge.Api.Endpoints;

/// <summary>
/// Internal, admin-only surface for the importer/connector registry. Never called by the SPA;
/// gated by <c>AdminOnly</c> (and network-restricted in production).
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/internal/import")
            .RequireAuthorization(Policies.AdminOnly).WithTags("Admin");

        // Connector registry status. The runtime importers land with Epics 2 & 4; this exposes
        // the registry so operators can see what's installed and its last-run state.
        group.MapGet("/status", () => Results.Ok(new
        {
            connectors = new[]
            {
                new { name = "USDA FoodData Central", direction = "outbound", installed = true, lastRun = (DateTimeOffset?)null },
                new { name = "Open Food Facts", direction = "outbound", installed = false, lastRun = (DateTimeOffset?)null },
            },
        })).WithName("ImportStatus");

        return app;
    }
}
