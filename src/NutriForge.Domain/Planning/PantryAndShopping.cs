using NutriForge.Domain.Common;

namespace NutriForge.Domain.Planning;

/// <summary>On-hand stock for a user, subtracted when a shopping list is generated. Owner-scoped.</summary>
public sealed class PantryItem : IUserOwned, IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public double Grams { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A consolidated, aisle-grouped, pantry-aware shopping list. Owner-scoped.</summary>
public sealed class ShoppingList : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>The meal plan this list was generated from (the closed loop), if any.</summary>
    public Guid? MealPlanId { get; set; }

    private readonly List<ShoppingItem> _items = [];
    public IReadOnlyCollection<ShoppingItem> Items => _items;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ShoppingItem AddItem(ShoppingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.ShoppingListId = Id;
        _items.Add(item);
        return item;
    }
}

/// <summary>One consolidated line on a shopping list, with the pantry already subtracted.</summary>
public sealed class ShoppingItem
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid ShoppingListId { get; set; }

    public Guid? IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string AisleCategory { get; set; } = "Other";

    /// <summary>Total grams needed across the plan, after subtracting pantry stock.</summary>
    public double Grams { get; set; }

    /// <summary>True when pantry stock fully covered the need (still listed, marked as on-hand).</summary>
    public bool PantryCovered { get; set; }
}
