using NutriForge.Application.Authorization;
using NutriForge.Application.Connectors;

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

        // Connector registry status — the installed outbound connectors, whether configuration enables
        // each, and the last run of the batch importers (#14). Read straight from the registry so the
        // list stays config-driven rather than hand-maintained here.
        group.MapGet("/status", async (IConnectorRegistry registry, CancellationToken ct) =>
            Results.Ok(new { connectors = await registry.GetStatusAsync(ct).ConfigureAwait(false) }))
            .WithName("ImportStatus");

        return app;
    }
}
