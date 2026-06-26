using NutriForge.Api.Setup;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.Recipes;

namespace NutriForge.Api.Endpoints;

/// <summary>
/// Recipes — computed-nutrition catalog with ownership: list, get, scale, create, edit, delete, and
/// (admin) promote-to-global. Reads are scoped by the DB query filter to <c>global ∪ the caller</c>;
/// the owner of a created recipe is always server-derived, never bound from the body.
/// </summary>
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

        group.MapPost("/", async (CreateRecipeRequest req, RecipeService recipes, ICurrentUser user, CancellationToken ct) =>
        {
            if (Invalid(req) is { } problem)
            {
                return problem;
            }

            // Owner is ALWAYS server-derived. A global recipe is admin-only (the `Global` body flag is mere
            // intent, honored here only for admins); everyone else gets a recipe owned by themselves.
            var makeGlobal = req.Global && user.IsInRole(Roles.Admin);
            Guid? owner = makeGlobal ? null : user.UserId;

            var created = await recipes.CreateAsync(req, owner, ct);
            return Results.Created($"/api/v1/recipes/{created.Id}", created);
        }).WithName("CreateRecipe");

        group.MapPut("/{id:guid}", async (Guid id, CreateRecipeRequest req, RecipeService recipes, CancellationToken ct) =>
        {
            if (Invalid(req) is { } problem)
            {
                return problem;
            }

            var result = await recipes.UpdateAsync(id, req, ct);
            return result.Status switch
            {
                RecipeWriteStatus.Ok => Results.Ok(result.Recipe),
                RecipeWriteStatus.Forbidden => Forbidden(),
                _ => Results.NotFound(),
            };
        }).WithName("UpdateRecipe");

        group.MapDelete("/{id:guid}", async (Guid id, RecipeService recipes, CancellationToken ct) =>
        {
            var status = await recipes.DeleteAsync(id, ct);
            return status switch
            {
                RecipeWriteStatus.Ok => Results.NoContent(),
                RecipeWriteStatus.Forbidden => Forbidden(),
                _ => Results.NotFound(),
            };
        }).WithName("DeleteRecipe");

        // Promote a recipe to the shared GLOBAL catalog. Admin-only — layered on top of the group's
        // OwnerOnly so BOTH must pass.
        group.MapPost("/{id:guid}/promote-global", async (Guid id, RecipeService recipes, CancellationToken ct) =>
        {
            var result = await recipes.PromoteToGlobalAsync(id, ct);
            return result.Status switch
            {
                RecipeWriteStatus.Ok => Results.Ok(result.Recipe),
                RecipeWriteStatus.Conflict => Results.Problem(title: "Already a global recipe",
                    detail: "Another global recipe already exists for this source video.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.NotFound(),
            };
        }).RequireAuthorization(Policies.AdminOnly).WithName("PromoteRecipeToGlobal");

        // Import a recipe from a YouTube URL (or pasted recipe text) → an editable preview the user
        // confirms by posting it to POST /recipes. The LLM recognizes; the catalog owns the numbers.
        group.MapPost("/import/preview", async (ImportRecipeRequest req, RecipeImportService import, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url) && string.IsNullOrWhiteSpace(req.Text))
            {
                return Results.Problem(title: "Nothing to import",
                    detail: "Provide a recipe URL (web or YouTube) or pasted recipe text/HTML.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await import.PreviewAsync(req, ct);
            return result.Outcome switch
            {
                ImportOutcome.Ok => Results.Ok(result.Preview),
                ImportOutcome.NeedsExtractor => Results.Problem(title: "Recipe import unavailable",
                    detail: "This page has no structured recipe data, and AI extraction needs an AI provider. Paste a recipe URL with schema.org data, or the recipe text.",
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Problem(title: "Couldn't extract a recipe",
                    detail: "No recipe found. Try a recipe URL with structured (schema.org) data, or paste the recipe text.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
            };
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("ImportRecipePreview");

        // Generate original recipes with AI (#101) — no manual authoring. The model writes the recipe;
        // the catalog + reference resolver own every macro. Returns the created (owned) recipes.
        group.MapPost("/generate", async (GenerateRecipesRequest req, RecipeGenerationService gen, ICurrentUser user, CancellationToken ct) =>
        {
            if (!gen.IsConfigured)
            {
                return Results.Problem(title: "Recipe generation unavailable",
                    detail: "AI recipe generation needs an AI provider to be configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var count = Math.Clamp(req?.Count ?? 3, 1, 12);
            var brief = new RecipeBrief(
                req?.MealType, req?.DietSlug, req?.TargetKcal, req?.MaxPrepMinutes,
                req?.Exclude, req?.Cuisine, Math.Clamp(req?.Servings ?? 4, 1, 12));

            var created = await gen.GenerateAsync(user.CurrentUserId(), brief, count, ct);
            return Results.Ok(created);
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("GenerateRecipes");

        return app;
    }

    private static IResult? Invalid(CreateRecipeRequest req) =>
        string.IsNullOrWhiteSpace(req.Name) || req.Ingredients is null || req.Ingredients.Count == 0
            ? Results.Problem(title: "Invalid recipe", detail: "A name and at least one ingredient are required.",
                statusCode: StatusCodes.Status400BadRequest)
            : null;

    private static IResult Forbidden() =>
        Results.Problem(title: "Not your recipe",
            detail: "You can only edit or delete recipes you own (admins may also manage global recipes).",
            statusCode: StatusCodes.Status403Forbidden);
}
