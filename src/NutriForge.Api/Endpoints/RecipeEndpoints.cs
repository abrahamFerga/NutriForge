using NutriForge.Application.Authorization;
using NutriForge.Application.Recipes;

namespace NutriForge.Api.Endpoints;

/// <summary>Recipes — computed-nutrition catalog: list, get, create, scale.</summary>
public static class RecipeEndpoints
{
    public static IEndpointRouteBuilder MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/recipes")
            .RequireAuthorization(Policies.OwnerOnly)
            .WithTags("Recipes");

        group.MapGet("/", async (string? q, RecipeService recipes, CancellationToken ct) =>
            Results.Ok(await recipes.ListAsync(q, ct)))
            .WithName("ListRecipes");

        group.MapGet("/{id:guid}", async (Guid id, RecipeService recipes, CancellationToken ct) =>
            await recipes.GetAsync(id, ct) is { } r ? Results.Ok(r) : Results.NotFound())
            .WithName("GetRecipe");

        group.MapGet("/{id:guid}/scale", async (Guid id, double servings, RecipeService recipes, CancellationToken ct) =>
            await recipes.ScaleAsync(id, servings, ct) is { } s ? Results.Ok(s) : Results.NotFound())
            .WithName("ScaleRecipe");

        group.MapPost("/", async (CreateRecipeRequest req, RecipeService recipes, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Ingredients is null || req.Ingredients.Count == 0)
            {
                return Results.Problem(title: "Invalid recipe", detail: "A name and at least one ingredient are required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var created = await recipes.CreateAsync(req, ct);
            return Results.Created($"/api/v1/recipes/{created.Id}", created);
        }).WithName("CreateRecipe");

        return app;
    }
}
