using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Planning;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Planning;

namespace NutriForge.Application.DietGen;

/// <summary>
/// Owns the async diet-plan lifecycle (ADR-0008): PARSE the desire + persist the plan as
/// <see cref="PlanStatus.Generating"/> (the generation worker finishes it), poll, accept, and the
/// closed loop (accepted plan → shopping list; logged intake → adherence). Owner-scoped.
/// </summary>
public sealed class DietPlanService(
    IAppDbContext db,
    ICatalogDbContext catalog,
    TargetService targets,
    ProfileService profiles,
    IDietIntentParser parser,
    DietPlanGenerator generator,
    ShoppingListService shopping,
    IClock clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Create a plan (PARSE + persist as Generating) and return it; the worker generates it.</summary>
    public async Task<DietPlanDto> CreateAsync(Guid userId, CreateDietPlanRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var target = await targets.GetAsync(userId, ct).ConfigureAwait(false);
        var profile = await profiles.GetAsync(userId, ct).ConfigureAwait(false);

        var kcalTarget = req.KcalTarget ?? target?.Kcal ?? 2000;
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in (req.ExcludeAllergens ?? []).Concat(req.Dislikes ?? [])
            .Concat(profile?.Allergens ?? []).Concat(profile?.Dislikes ?? []))
        {
            if (!string.IsNullOrWhiteSpace(k))
            {
                exclude.Add(k.Trim());
            }
        }

        var intent = new DietIntent(
            kcalTarget, target?.ProteinG, req.DietSlug, [.. exclude],
            req.MaxPrepMinutes, req.MealsPerDay ?? 3, req.HorizonDays ?? 7);

        if (!string.IsNullOrWhiteSpace(req.Desire) && parser.IsConfigured)
        {
            intent = await parser.ParseAsync(req.Desire, intent, ct).ConfigureAwait(false);
        }

        var plan = new MealPlan
        {
            UserId = userId,
            Status = PlanStatus.Generating,
            Desire = req.Desire,
            IntentJson = JsonSerializer.Serialize(intent, Json),
            HorizonDays = intent.HorizonDays,
            TargetKcal = intent.KcalTarget ?? kcalTarget,
            // Clamp the people-count to a sane [1, 9] in ONE place. Deliberately NOT fed into `intent`
            // or `kcalTarget` above — generation stays per-eater; Eaters only scales the shopping list.
            Eaters = Math.Clamp(req.Eaters ?? 1, 1, 9),
        };

        // Run the deterministic pipeline now (it's fast) so the 202 poll returns a finished plan.
        // The background worker remains the path for slow, LLM-backed SELECT in future.
        await FinishGenerationAsync(plan, intent, ct).ConfigureAwait(false);

        db.MealPlans.Add(plan);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToDto(plan);
    }

    /// <summary>Run FILTER → SELECT → VERIFY → REPAIR over the catalog pool and finish the plan.</summary>
    private async Task FinishGenerationAsync(MealPlan plan, DietIntent intent, CancellationToken ct)
    {
        // Order by Id so the greedy round-robin pool is stable: same inputs ⇒ same plan (deterministic
        // generation, independent of DB heap order). Eaters never enters here.
        var recipes = await catalog.Recipes.AsNoTracking().Include(r => r.Ingredients)
            .Where(r => r.IsNutritionComputed).OrderBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        var diet = string.IsNullOrEmpty(intent.DietSlug)
            ? null
            : await catalog.DietTypes.AsNoTracking().FirstOrDefaultAsync(d => d.Slug == intent.DietSlug, ct).ConfigureAwait(false);

        var pool = RecipeFilter.Filter(recipes, intent, diet);
        var result = generator.Generate(pool, intent, plan.TargetKcal);

        plan.ClearSlots();
        foreach (var s in result.Slots)
        {
            var slot = new PlanSlot
            {
                Day = s.Day,
                MealSlot = s.Meal,
                RecipeId = s.Recipe.Id,
                RecipeName = s.Recipe.Name,
                Servings = s.Servings,
            };
            slot.SetMacros(s.Macros.Rounded());
            plan.AddSlot(slot);
        }

        plan.Status = result.Feasible ? PlanStatus.Ready : PlanStatus.Infeasible;
        plan.Message = result.Message;
        plan.AchievedKcal = result.DailyAverage.Kcal;
        plan.AchievedProteinG = result.DailyAverage.ProteinG;
        plan.AchievedFatG = result.DailyAverage.FatG;
        plan.AchievedCarbG = result.DailyAverage.CarbG;
    }

    public async Task<DietPlanDto?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.AsNoTracking().Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        return plan is null ? null : ToDto(plan);
    }

    public async Task<DietPlanDto?> AcceptAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        if (plan is null || plan.Status != PlanStatus.Ready)
        {
            return null;
        }

        plan.Status = PlanStatus.Accepted;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(plan);
    }

    /// <summary>Closed loop: turn an accepted (or ready) plan's recipes into one consolidated shopping list.</summary>
    public async Task<ShoppingListDto?> GenerateShoppingListAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.AsNoTracking().Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        if (plan is null || plan.Slots.Count == 0)
        {
            return null;
        }

        return await shopping.GenerateAsync(userId, BuildShoppingLines(plan), mealPlanId: plan.Id, ct).ConfigureAwait(false);
    }

    /// <summary>The plan plus its consolidated (un-persisted) shopping list — for the PDF/meal-prep export.</summary>
    public async Task<(DietPlanDto Plan, ShoppingListDto Shopping)?> GetPlanWithShoppingAsync(
        Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.AsNoTracking().Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        if (plan is null || plan.Slots.Count == 0)
        {
            return null;
        }

        var shoppingDto = await shopping.ComputeAsync(userId, BuildShoppingLines(plan), mealPlanId: plan.Id, ct).ConfigureAwait(false);
        return (ToDto(plan), shoppingDto);
    }

    /// <summary>
    /// Group the plan's slots into per-recipe shopping lines, scaled by <see cref="MealPlan.Eaters"/>.
    /// This is the ONLY place the people-count touches quantities: ShoppingListService expands grams
    /// from these servings, so multiplying once here yields exactly Eaters× the single-eater list.
    /// Eaters is NEVER also passed into ShoppingListService (that would 4× the groceries).
    /// </summary>
    private static List<ShoppingRecipeLine> BuildShoppingLines(MealPlan plan) =>
        plan.Slots
            .GroupBy(s => s.RecipeId)
            .Select(g => new ShoppingRecipeLine(g.Key, g.Sum(s => s.Servings) * plan.Eaters))
            .ToList();

    /// <summary>Closed loop: how closely logged intake tracked the plan's daily target over the last week.</summary>
    public async Task<IReadOnlyList<AdherenceDto>> AdherenceAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct)
            .ConfigureAwait(false);
        if (plan is null)
        {
            return [];
        }

        var planned = plan.AchievedKcal > 0 ? plan.AchievedKcal : plan.TargetKcal;
        var end = clock.Today;
        var start = end.AddDays(-6);

        var byDay = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.Date >= start && e.Date <= end)
            .GroupBy(e => e.Date)
            .Select(g => new { Date = g.Key, Kcal = g.Sum(x => x.Kcal) })
            .ToListAsync(ct).ConfigureAwait(false);
        var lookup = byDay.ToDictionary(x => x.Date, x => x.Kcal);

        return Enumerable.Range(0, 7).Select(offset => start.AddDays(offset)).Select(d =>
        {
            var logged = lookup.GetValueOrDefault(d, 0);
            var pct = planned > 0 ? Math.Max(0, 100 - (Math.Abs(logged - planned) / planned * 100)) : 0;
            return new AdherenceDto(d, Math.Round(planned), Math.Round(logged), Math.Round(pct, 1));
        }).ToList();
    }

    private static DietPlanDto ToDto(MealPlan p) => new(
        p.Id, p.Status.ToString(), p.TargetKcal,
        p.AchievedKcal, p.AchievedProteinG, p.AchievedFatG, p.AchievedCarbG, p.Eaters, p.Message,
        p.Slots.OrderBy(s => s.Day).ThenBy(s => s.MealSlot)
            .Select(s => new PlanSlotDto(s.Day, s.MealSlot.ToString(), s.RecipeId, s.RecipeName, s.Servings,
                s.Kcal, s.ProteinG, s.FatG, s.CarbG)).ToList());
}
