using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.Planning;

namespace NutriForge.Api.Endpoints;

/// <summary>Pantry stock and consolidated shopping lists — owner-scoped (batch cooking).</summary>
public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        MapPantry(app);
        MapShopping(app);
        return app;
    }

    private static void MapPantry(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pantry")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Pantry");

        group.MapGet("/", async (ICurrentUser user, PantryService pantry, CancellationToken ct) =>
            Results.Ok(await pantry.ListAsync(user.CurrentUserId(), ct)))
            .WithName("ListPantry");

        group.MapPost("/", async (AddPantryRequest req, ICurrentUser user, PantryService pantry, CancellationToken ct) =>
            Results.Ok(await pantry.AddAsync(user.CurrentUserId(), req, ct)))
            .WithName("AddPantryItem");

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, PantryService pantry, CancellationToken ct) =>
            await pantry.DeleteAsync(user.CurrentUserId(), id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeletePantryItem");
    }

    private static void MapShopping(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shopping-lists")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("ShoppingLists");

        group.MapPost("/", async (GenerateShoppingListRequest req, ICurrentUser user, ShoppingListService shopping, CancellationToken ct) =>
        {
            if (req.Recipes is null || req.Recipes.Count == 0)
            {
                return Results.Problem(title: "No recipes", detail: "Provide at least one recipe.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var list = await shopping.GenerateAsync(user.CurrentUserId(), req.Recipes, mealPlanId: null, ct);
            return Results.Created($"/api/v1/shopping-lists/{list.Id}", list);
        }).WithName("GenerateShoppingList");

        group.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, ShoppingListService shopping, CancellationToken ct) =>
            await shopping.GetAsync(user.CurrentUserId(), id, ct) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithName("GetShoppingList");
    }
}
