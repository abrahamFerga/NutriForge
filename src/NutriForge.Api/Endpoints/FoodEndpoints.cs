using NutriForge.Api.Setup;
using NutriForge.Application.Authorization;
using NutriForge.Application.Food;

namespace NutriForge.Api.Endpoints;

/// <summary>Food catalog: search, get, barcode lookup, and user submission.</summary>
public static class FoodEndpoints
{
    public static IEndpointRouteBuilder MapFoodEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/foods")
            .RequireAuthorization(Policies.OwnerOnly)
            .WithTags("Foods");

        group.MapGet("/search", async (string q, FoodService foods, CancellationToken ct) =>
            Results.Ok(await foods.SearchAsync(q, ct)))
            .WithName("SearchFoods")
            .RequireRateLimiting(RateLimitPolicies.Read);

        group.MapGet("/{id:guid}", async (Guid id, FoodService foods, CancellationToken ct) =>
            await foods.GetAsync(id, ct) is { } food ? Results.Ok(food) : Results.NotFound())
            .WithName("GetFood");

        group.MapGet("/barcode/{gtin}", async (string gtin, FoodService foods, CancellationToken ct) =>
            await foods.GetByBarcodeAsync(gtin, ct) is { } food ? Results.Ok(food) : Results.NotFound())
            .WithName("GetFoodByBarcode");

        group.MapPost("/", async (SubmitFoodRequest req, FoodService foods, CancellationToken ct) =>
        {
            var created = await foods.SubmitAsync(req, ct);
            return Results.Created($"/api/v1/foods/{created.Id}", created);
        })
            .WithName("SubmitFood")
            .ValidatesBody<RouteHandlerBuilder, SubmitFoodRequest>();

        return app;
    }
}
