using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.Recipes;

/// <summary>
/// Recipe catalog: create recipes whose per-serving nutrition is <b>computed</b> from resolved
/// ingredients (the differentiator — never trusted from the source), read, list, scale, edit, delete,
/// and (admin) promote to global. Recipes are owner-scoped: the EF query filter limits reads to
/// <c>global ∪ the current user</c>, and the owner is always server-derived from
/// <see cref="ICurrentUser"/> — never bound from a request body. Ingredient resolution and unit→grams
/// conversion are fully deterministic.
/// </summary>
public sealed class RecipeService(ICatalogDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// Create a recipe owned by <paramref name="ownerUserId"/> (the caller, server-derived), or a GLOBAL
    /// recipe when it is <c>null</c>. Passing null is an admin-only action and the endpoint is the trust
    /// boundary that decides it — the service never reads an owner off the request body.
    /// </summary>
    public async Task<RecipeDto> CreateAsync(CreateRecipeRequest request, Guid? ownerUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Dedup imported recipes within the caller's visibility (global ∪ own): a video already imported
        // as a global, or already imported by this user, is returned instead of creating a duplicate —
        // preferring the global copy. Other users' private imports are invisible here, so each user can
        // still import the same video into their own catalog.
        if (!string.IsNullOrWhiteSpace(request.SourceVideoId))
        {
            var existing = await db.Recipes.AsNoTracking().Include(r => r.Ingredients)
                .Where(r => r.SourceVideoId == request.SourceVideoId)
                .OrderBy(r => r.OwnerUserId == null ? 0 : 1) // prefer the global copy when both exist
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing.ToDto(currentUser.UserId);
            }
        }

        // Web import (#91): same normalized source page (no video id) ⇒ return the existing visible recipe.
        var sourceKey = Recipe.NormalizeSourceKey(request.SourceVideoId, request.SourceUrl);
        if (sourceKey is not null)
        {
            var existing = await db.Recipes.AsNoTracking().Include(r => r.Ingredients)
                .Where(r => r.SourceKey == sourceKey)
                .OrderBy(r => r.OwnerUserId == null ? 0 : 1)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing.ToDto(currentUser.UserId);
            }
        }

        var recipe = await BuildRecipeAsync(new Recipe(), request, ct).ConfigureAwait(false);
        if (ownerUserId is { } owner)
        {
            recipe.AssignOwner(owner);
        }

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return recipe.ToDto(currentUser.UserId);
    }

    /// <summary>Dry-run: build + resolve + compute nutrition WITHOUT persisting — for an import preview.</summary>
    public async Task<RecipeDto> PreviewAsync(CreateRecipeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recipe = await BuildRecipeAsync(new Recipe(), request, ct).ConfigureAwait(false);
        return recipe.ToDto(currentUser.UserId);
    }

    /// <summary>
    /// Edit an existing recipe (re-resolving ingredients). Ownership-guarded: the caller may edit their
    /// own recipe, or — if an admin — a global one; a recipe they can't see is reported as
    /// <see cref="RecipeWriteStatus.NotFound"/>, a visible-but-foreign one as
    /// <see cref="RecipeWriteStatus.Forbidden"/>.
    /// </summary>
    public async Task<RecipeWriteResult> UpdateAsync(Guid id, CreateRecipeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipe = await db.Recipes.Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (recipe is null)
        {
            return new RecipeWriteResult(RecipeWriteStatus.NotFound, null);
        }

        if (!CanMutate(recipe))
        {
            return new RecipeWriteResult(RecipeWriteStatus.Forbidden, null);
        }

        // Replace the ingredient rows explicitly. The new lines carry client-generated keys, so adding
        // them only via the tracked navigation would make EF assume they already exist (UPDATE → 0 rows);
        // an explicit Remove(old)/Add(new) keeps each row's change-tracker state unambiguous.
        db.RecipeIngredients.RemoveRange(recipe.Ingredients.ToList());
        await BuildRecipeAsync(recipe, request, ct).ConfigureAwait(false);
        db.RecipeIngredients.AddRange(recipe.Ingredients.ToList());

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RecipeWriteResult(RecipeWriteStatus.Ok, recipe.ToDto(currentUser.UserId));
    }

    /// <summary>Delete a recipe the caller owns (or, for an admin, a global one). Same 404/403 semantics.</summary>
    public async Task<RecipeWriteStatus> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (recipe is null)
        {
            return RecipeWriteStatus.NotFound;
        }

        if (!CanMutate(recipe))
        {
            return RecipeWriteStatus.Forbidden;
        }

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return RecipeWriteStatus.Ok;
    }

    /// <summary>
    /// Promote a recipe to GLOBAL (admin action — the endpoint enforces the role). Only operates on a
    /// recipe the admin can already see (their own or an existing global); promoting an already-global
    /// recipe is a no-op. Refuses (<see cref="RecipeWriteStatus.Conflict"/>) if a different global recipe
    /// already exists for the same source video, which the composite unique index would otherwise reject.
    /// </summary>
    public async Task<RecipeWriteResult> PromoteToGlobalAsync(Guid id, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (recipe is null)
        {
            return new RecipeWriteResult(RecipeWriteStatus.NotFound, null);
        }

        if (recipe.IsGlobal)
        {
            return new RecipeWriteResult(RecipeWriteStatus.Ok, recipe.ToDto(currentUser.UserId));
        }

        if (!string.IsNullOrWhiteSpace(recipe.SourceVideoId))
        {
            var clash = await db.Recipes
                .AnyAsync(r => r.Id != recipe.Id && r.OwnerUserId == null && r.SourceVideoId == recipe.SourceVideoId, ct)
                .ConfigureAwait(false);
            if (clash)
            {
                return new RecipeWriteResult(RecipeWriteStatus.Conflict, null);
            }
        }

        recipe.MakeGlobal();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RecipeWriteResult(RecipeWriteStatus.Ok, recipe.ToDto(currentUser.UserId));
    }

    /// <summary>The caller may mutate their own recipe, or — as an admin — a global one.</summary>
    private bool CanMutate(Recipe recipe) =>
        (recipe.OwnerUserId is { } owner && owner == currentUser.UserId)
        || (recipe.IsGlobal && currentUser.IsInRole(Roles.Admin));

    /// <summary>
    /// Resolve <paramref name="request"/>'s scalar fields + ingredients into <paramref name="recipe"/>
    /// and recompute its per-serving nutrition (not saved). Used for both create and edit; clears any
    /// existing ingredients first so an edit fully re-resolves.
    /// </summary>
    private async Task<Recipe> BuildRecipeAsync(Recipe recipe, CreateRecipeRequest request, CancellationToken ct)
    {
        var ingredients = await db.Ingredients.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var foodIds = ingredients.Where(i => i.DefaultFoodId.HasValue).Select(i => i.DefaultFoodId!.Value).ToHashSet();
        var foods = await db.Foods.AsNoTracking()
            .Where(f => foodIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct).ConfigureAwait(false);

        recipe.Name = request.Name.Trim();
        recipe.Servings = request.Servings <= 0 ? 1 : request.Servings;
        recipe.TotalMinutes = request.TotalMinutes;
        recipe.Instructions = request.Instructions;
        recipe.Tags = [.. (request.Tags ?? []).Select(t => t.Trim().ToLowerInvariant())];
        recipe.SourceUrl = request.SourceUrl;
        recipe.SourceType = request.SourceType;
        recipe.SourceVideoId = request.SourceVideoId;
        recipe.ThumbnailUrl = request.ThumbnailUrl;
        recipe.SourceKey = Recipe.NormalizeSourceKey(request.SourceVideoId, request.SourceUrl);

        recipe.ClearIngredients();
        foreach (var line in request.Ingredients ?? [])
        {
            var ri = new RecipeIngredient
            {
                RawText = line.RawText ?? $"{line.Quantity} {line.Unit} {line.Name}".Trim(),
                Quantity = line.Quantity,
                Unit = line.Unit,
                IngredientName = line.Name.Trim(),
            };

            var ingredient = ingredients.FirstOrDefault(i => i.Matches(line.Name));
            if (ingredient is not null)
            {
                ri.IngredientId = ingredient.Id;
                var grams = UnitConverter.ToGrams(line.Quantity, line.Unit, ingredient.DensityGPerMl, ingredient.GramsPerCount);
                if (grams is { } g && ingredient.DefaultFoodId is { } foodId && foods.TryGetValue(foodId, out var food))
                {
                    ri.SetContribution(g, food.MacrosFor(g).Rounded());
                }
            }

            // Fallback (#101): an ingredient the catalog doesn't carry resolves against the curated
            // reference table (deterministic, never the LLM) so AI-generated / freely-imported recipes
            // still compute their nutrition instead of being stuck at 0 kcal.
            if (!ri.Resolved && NutritionReference.Resolve(line.Name, line.Quantity, line.Unit) is { } refHit)
            {
                ri.SetContribution(refHit.Grams, refHit.Macros);
            }

            recipe.AddIngredient(ri);
        }

        recipe.RecomputeNutrition();
        return recipe;
    }

    public async Task<RecipeDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (recipe is null)
        {
            return null;
        }

        // Load the linked catalog ingredients so the detail can show raw (buy) vs cooked (plate) weights.
        var ids = recipe.Ingredients.Where(i => i.IngredientId.HasValue).Select(i => i.IngredientId!.Value).ToHashSet();
        var byId = ids.Count == 0
            ? null
            : await db.Ingredients.AsNoTracking().Where(i => ids.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);

        return recipe.ToDto(currentUser.UserId, byId);
    }

    public async Task<IReadOnlyList<RecipeSummary>> ListAsync(string? query, CancellationToken ct = default)
    {
        // Hide per-plan generated recipes (#101) from the catalog browser — they belong to a specific diet
        // plan, not the user's recipe collection. EF applies C# null-semantics, so null-source recipes stay.
        var q = db.Recipes.AsNoTracking().Where(r => r.SourceType != RecipeGenerationService.PlanGeneratedSource);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            q = q.Where(r => r.Name.ToLower().Contains(term));
        }

        var recipes = await q.OrderBy(r => r.Name).Take(100).ToListAsync(ct).ConfigureAwait(false);
        return recipes.Select(r => r.ToSummary(currentUser.UserId)).ToList();
    }

    public async Task<ScaledRecipeDto?> ScaleAsync(Guid id, double targetServings, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (recipe is null)
        {
            return null;
        }

        var factor = targetServings / (recipe.Servings <= 0 ? 1 : recipe.Servings);
        var scaled = recipe.Ingredients
            .Select(i => new ScaledIngredientDto(i.IngredientName, Math.Round(i.Quantity * factor, 2), i.Unit, Math.Round(i.Grams * factor, 1)))
            .ToList();

        // Per-serving nutrition is invariant under scaling.
        return new ScaledRecipeDto(recipe.Id, recipe.Name, recipe.Servings, targetServings, scaled, recipe.KcalPerServing);
    }
}
