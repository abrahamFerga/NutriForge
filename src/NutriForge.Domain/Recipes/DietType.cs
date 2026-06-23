namespace NutriForge.Domain.Recipes;

/// <summary>
/// A diet expressed as <b>data</b>, not code (ADR-0007): a recipe satisfies it when it carries all
/// required tags and contains none of the excluded ingredient keywords. New diets are rows, not
/// branches. Public-read catalog.
/// </summary>
public sealed class DietType
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Tags a recipe must have (e.g. "vegan"). Empty ⇒ no tag requirement.</summary>
    public List<string> RequiredTags { get; set; } = [];

    /// <summary>Ingredient-name keywords a recipe must NOT contain (e.g. "pork", "gelatin").</summary>
    public List<string> ExcludedKeywords { get; set; } = [];
}
