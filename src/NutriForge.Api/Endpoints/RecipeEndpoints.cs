using NutriForge.Api.Setup;
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

        // Import a recipe from a YouTube URL (or pasted recipe text) → an editable preview the user
        // confirms by posting it to POST /recipes. The LLM recognizes; the catalog owns the numbers.
        group.MapPost("/import/preview", async (ImportRecipeRequest req, RecipeImportService import, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url) && string.IsNullOrWhiteSpace(req.Text))
            {
                return Results.Problem(title: "Nothing to import",
                    detail: "Provide a YouTube/recipe URL or pasted recipe text.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (!import.IsConfigured)
            {
                return Results.Problem(title: "Recipe import unavailable",
                    detail: "No AI provider is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var preview = await import.PreviewAsync(req, ct);
            return preview is null
                ? Results.Problem(title: "Couldn't extract a recipe",
                    detail: "No recipe found. For a YouTube link with an empty description, paste the recipe text.",
                    statusCode: StatusCodes.Status422UnprocessableEntity)
                : Results.Ok(preview);
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("ImportRecipePreview");

        return app;
    }
}
