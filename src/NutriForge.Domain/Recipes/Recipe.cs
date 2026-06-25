using NutriForge.Domain.Common;

namespace NutriForge.Domain.Recipes;

/// <summary>
/// A schema.org-aligned recipe whose per-serving nutrition is <b>computed</b> from its resolved
/// ingredients (never copied from the source). Recipes are <b>owner-scoped</b>: a null
/// <see cref="OwnerUserId"/> is a GLOBAL recipe (curated by an admin, readable by everyone); a
/// non-null one is private to that user. Visibility is enforced by an EF query filter
/// (global ∪ mine). Audited on mutation.
/// </summary>
public sealed class Recipe : IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>
    /// Owner of this recipe, or <c>null</c> for a GLOBAL (admin-curated) recipe visible to everyone.
    /// Always server-derived from the authenticated principal — never bound from a request body — and
    /// never the empty guid (see <see cref="AssignOwner"/>). The EF query filter scopes reads to
    /// <c>global ∪ current user</c>.
    /// </summary>
    public Guid? OwnerUserId { get; private set; }

    /// <summary>True when this is a shared, admin-curated recipe (no owner).</summary>
    public bool IsGlobal => OwnerUserId is null;

    public string Name { get; set; } = string.Empty;
    public int Servings { get; set; } = 1;
    public int TotalMinutes { get; set; }
    public string? Instructions { get; set; }

    /// <summary>
    /// Cooking method / appliance (e.g. "Oven", "Stovetop", "No-cook") used to GROUP parallel batch-cook
    /// steps so the user runs the oven once for everything that bakes. Null ⇒ "Other".
    /// </summary>
    public string? CookMethod { get; set; }

    /// <summary>Free-form tags (cuisine, "vegan", "high-protein", proteins) for filtering/variety.</summary>
    public List<string> Tags { get; set; } = [];

    // Provenance for imported recipes (all null for hand-authored / seeded recipes). The source is
    // only a link/embed — nutrition is still COMPUTED from resolved ingredients, never trusted from it.
    public string? SourceUrl { get; set; }
    public string? SourceType { get; set; }       // "youtube" | "web" | "manual"
    public string? SourceVideoId { get; set; }     // canonical 11-char YouTube id — for embed + dedup
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Normalized dedup key for a WEB-imported recipe (null for YouTube — its <see cref="SourceVideoId"/>
    /// dedups — and for hand-authored recipes). Server-derived from <see cref="SourceUrl"/>, never bound
    /// from a request, so re-importing the same page (ignoring scheme / trailing slash / fragment / case)
    /// returns the existing recipe instead of a duplicate.
    /// </summary>
    public string? SourceKey { get; set; }

    /// <summary>
    /// Compute the web dedup key: null when there's a video id (the video index owns YouTube dedup) or no
    /// URL; otherwise the URL's host + path + query, lower-cased with the scheme/fragment/trailing-slash
    /// dropped. Pure.
    /// </summary>
    public static string? NormalizeSourceKey(string? videoId, string? sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(sourceUrl))
        {
            return null;
        }

        var url = sourceUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Trim(url.ToLowerInvariant());
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return Trim($"{uri.Host}{path}{uri.Query}".ToLowerInvariant());

        static string Trim(string s) => s.Length > 400 ? s[..400] : s;
    }

    private readonly List<RecipeIngredient> _ingredients = [];
    public IReadOnlyCollection<RecipeIngredient> Ingredients => _ingredients;

    // Cached computed per-serving nutrition (set by RecomputeNutrition; queryable for FILTER).
    public double KcalPerServing { get; private set; }
    public double ProteinPerServing { get; private set; }
    public double FatPerServing { get; private set; }
    public double CarbPerServing { get; private set; }

    /// <summary>True only when every ingredient resolved — diet generation uses these recipes only.</summary>
    public bool IsNutritionComputed { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Macros PerServing => new(KcalPerServing, ProteinPerServing, FatPerServing, CarbPerServing);

    public RecipeIngredient AddIngredient(RecipeIngredient ingredient)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        ingredient.RecipeId = Id;
        _ingredients.Add(ingredient);
        return ingredient;
    }

    /// <summary>Drop all ingredients (used before re-resolving on an edit).</summary>
    public void ClearIngredients() => _ingredients.Clear();

    /// <summary>
    /// Assign a private owner. Rejects the empty guid so an unauthenticated/system context can never
    /// accidentally mint an "owned-by-nobody" recipe (which would be invisible yet not global).
    /// </summary>
    public void AssignOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("A private recipe must have a real owner.", nameof(ownerUserId));
        }

        OwnerUserId = ownerUserId;
    }

    /// <summary>Promote to a GLOBAL recipe (admin action) — clears the owner so everyone can read it.</summary>
    public void MakeGlobal() => OwnerUserId = null;

    /// <summary>Recompute the cached per-serving nutrition from the (resolved) ingredient contributions.</summary>
    public void RecomputeNutrition()
    {
        var servings = Servings <= 0 ? 1 : Servings;
        var total = _ingredients.Aggregate(Macros.Zero, (acc, i) => acc + i.Contribution);
        var perServing = total.Scale(1.0 / servings).Rounded();

        KcalPerServing = perServing.Kcal;
        ProteinPerServing = perServing.ProteinG;
        FatPerServing = perServing.FatG;
        CarbPerServing = perServing.CarbG;
        IsNutritionComputed = _ingredients.Count > 0 && _ingredients.All(i => i.Resolved);
    }

    /// <summary>Per-serving macros scaled to a number of servings.</summary>
    public Macros MacrosFor(double servings) => PerServing.Scale(servings);
}
