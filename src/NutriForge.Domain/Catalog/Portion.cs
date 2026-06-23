namespace NutriForge.Domain.Catalog;

/// <summary>A named serving for a food (e.g. "1 medium egg" → 50 g), used for low-friction logging.</summary>
public sealed class Portion
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid FoodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Grams { get; set; }
}
