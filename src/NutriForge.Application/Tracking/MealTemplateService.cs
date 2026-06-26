using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Common;
using NutriForge.Domain.Diary;

namespace NutriForge.Application.Tracking;

/// <summary>
/// Saved meals (#70): a reusable combination of foods the user can log in one tap. Built on the same
/// food-resolution as the diary; logging a template expands its items through <see cref="DiaryService"/>
/// so each lands as a normal diary entry (with a fresh log-time macro snapshot). Owner-scoped.
/// </summary>
public sealed class MealTemplateService(IAppDbContext db, ICatalogDbContext catalog, DiaryService diary)
{
    /// <summary>Create a template from an explicit set of foods. Unknown foods are dropped; null if none resolve.</summary>
    public async Task<MealTemplateDto?> CreateAsync(Guid userId, string name, IReadOnlyList<MealTemplateItemInput> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var foods = await LoadFoodsAsync(items.Select(i => i.FoodId), ct).ConfigureAwait(false);

        var template = new MealTemplate { UserId = userId, Name = name.Trim() };
        foreach (var i in items)
        {
            if (foods.ContainsKey(i.FoodId))
            {
                template.AddItem(i.FoodId, i.PortionId, i.Quantity);
            }
        }

        if (template.Items.Count == 0)
        {
            return null;
        }

        db.MealTemplates.Add(template);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(template, foods);
    }

    /// <summary>Snapshot the foods already logged in a day's meal slot into a new template.</summary>
    public async Task<MealTemplateDto?> CreateFromDayAsync(Guid userId, string name, DateOnly date, MealSlot mealSlot, CancellationToken ct = default)
    {
        var entries = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.Date == date && e.MealSlot == mealSlot)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct).ConfigureAwait(false);

        var items = entries.Select(e => new MealTemplateItemInput(e.FoodId, e.PortionId, e.Quantity)).ToList();
        return items.Count == 0 ? null : await CreateAsync(userId, name, items, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MealTemplateDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var templates = await db.MealTemplates.AsNoTracking()
            .Include(t => t.Items)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        if (templates.Count == 0)
        {
            return [];
        }

        var foods = await LoadFoodsAsync(templates.SelectMany(t => t.Items).Select(i => i.FoodId), ct).ConfigureAwait(false);
        return [.. templates.Select(t => ToDto(t, foods))];
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid templateId, CancellationToken ct = default)
    {
        var template = await db.MealTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return false;
        }

        db.MealTemplates.Remove(template);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Log a template's foods into a day's meal slot. Returns the number of entries created (foods later
    /// removed from the catalog are skipped), or null when the template doesn't exist for this user.
    /// </summary>
    public async Task<int?> LogAsync(Guid userId, Guid templateId, DateOnly date, MealSlot mealSlot, CancellationToken ct = default)
    {
        var template = await db.MealTemplates.AsNoTracking()
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return null;
        }

        var logged = 0;
        foreach (var item in template.Items.OrderBy(i => i.Id))
        {
            try
            {
                await diary.AddAsync(userId, new AddDiaryEntryRequest(date, mealSlot, item.FoodId, item.PortionId, item.Quantity), ct).ConfigureAwait(false);
                logged++;
            }
            catch (FoodNotFoundException)
            {
                // A food removed from the catalog since the template was saved — skip it.
            }
        }

        return logged;
    }

    private async Task<Dictionary<Guid, Domain.Catalog.Food>> LoadFoodsAsync(IEnumerable<Guid> foodIds, CancellationToken ct)
    {
        var ids = foodIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await catalog.Foods.AsNoTracking().Include(f => f.Portions)
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct).ConfigureAwait(false);
    }

    private static MealTemplateDto ToDto(MealTemplate template, Dictionary<Guid, Domain.Catalog.Food> foods)
    {
        var items = new List<MealTemplateItemDto>();
        var total = Macros.Zero;
        foreach (var i in template.Items.OrderBy(i => i.Id))
        {
            if (!foods.TryGetValue(i.FoodId, out var food))
            {
                continue;
            }

            var (grams, portionName) = Resolve(food, i.PortionId, i.Quantity);
            var macros = food.MacrosFor(grams).Rounded();
            total += macros;
            items.Add(new MealTemplateItemDto(food.Id, food.Name, i.PortionId, portionName, i.Quantity, macros.Kcal, macros.ProteinG));
        }

        return new MealTemplateDto(template.Id, template.Name, items, Math.Round(total.Kcal, 1), Math.Round(total.ProteinG, 1));
    }

    // Mirror of DiaryService.BuildEntryAsync's grams resolution: a portion scales by quantity, else
    // the quantity is grams directly.
    private static (double Grams, string PortionName) Resolve(Domain.Catalog.Food food, Guid? portionId, double quantity)
    {
        if (portionId is { } pid && food.Portions.FirstOrDefault(p => p.Id == pid) is { } portion)
        {
            return (portion.Grams * quantity, portion.Name);
        }

        return (quantity, "g");
    }
}
