using NutriForge.Domain.Common;

namespace NutriForge.Domain.Diary;

/// <summary>
/// A saved combination of foods the user can log in one tap (#70) — e.g. "my usual breakfast". Owner-
/// scoped; the items are thin links to catalog foods (name/macros read live at log time), so a relabel
/// or renutrition is reflected. The stored quantity/portion per item reproduces the original entry.
/// </summary>
public sealed class MealTemplate : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    private readonly List<MealTemplateItem> _items = [];
    public IReadOnlyList<MealTemplateItem> Items => _items;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void AddItem(Guid foodId, Guid? portionId, double quantity)
        => _items.Add(new MealTemplateItem { MealTemplateId = Id, FoodId = foodId, PortionId = portionId, Quantity = quantity });

    public void ClearItems() => _items.Clear();
}

/// <summary>One food in a <see cref="MealTemplate"/>: a catalog food + portion + quantity.</summary>
public sealed class MealTemplateItem
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid MealTemplateId { get; set; }
    public Guid FoodId { get; set; }
    public Guid? PortionId { get; set; }
    public double Quantity { get; set; }
}
