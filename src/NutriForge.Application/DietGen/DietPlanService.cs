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
    IClock clock,
    Observability.NutriForgeMetrics? metrics = null)
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
        // Allergens are safety-critical → expand through the ontology so derivatives (milk→butter/cheese/
        // whey, nuts→almond/cashew, …) are caught too, not just the literal word (#60).
        foreach (var k in AllergenOntology.Expand((req.ExcludeAllergens ?? []).Concat(profile?.Allergens ?? [])))
        {
            exclude.Add(k);
        }

        // Dislikes are a preference, not a safety constraint → excluded literally only.
        foreach (var k in (req.Dislikes ?? []).Concat(profile?.Dislikes ?? []))
        {
            if (!string.IsNullOrWhiteSpace(k))
            {
                exclude.Add(k.Trim());
            }
        }

        var intent = new DietIntent(
            kcalTarget, target?.ProteinG, req.DietSlug, [.. exclude],
            req.MaxPrepMinutes, req.MealsPerDay ?? 3, req.HorizonDays ?? 7, req.BlockSize ?? 1);

        if (!string.IsNullOrWhiteSpace(req.Desire) && parser.IsConfigured)
        {
            intent = await parser.ParseAsync(req.Desire, intent, ct).ConfigureAwait(false);
        }

        // Day-block rotation is a user cooking preference, not an LLM inference: force it from the request
        // and clamp to the (possibly parser-adjusted) horizon. Like Eaters it never alters the per-day target.
        intent = intent with { BlockSize = Math.Clamp(req.BlockSize ?? 1, 1, Math.Max(intent.HorizonDays, 1)) };

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
            BlockSize = intent.BlockSize,
        };

        // Run the deterministic pipeline now (it's fast) so the 202 poll returns a finished plan.
        // The background worker remains the path for slow, LLM-backed SELECT in future.
        await FinishGenerationAsync(plan, intent, ct).ConfigureAwait(false);

        // When the request carries no explicit members, fall back to the user's SAVED household (#100) so
        // the partner/children they always cook for are attached automatically — no re-adding each plan. A
        // non-null Members (even empty) is an explicit override and wins.
        var members = req.Members ?? await LoadSavedHouseholdAsync(userId, ct).ConfigureAwait(false);
        AttachMembers(plan, members);

        db.MealPlans.Add(plan);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToDto(plan);
    }

    /// <summary>Run FILTER → SELECT → VERIFY → REPAIR over the catalog pool and finish the plan.</summary>
    private async Task FinishGenerationAsync(MealPlan plan, DietIntent intent, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); // #50 — p95 plan-latency SLO

        // Order by Id so the greedy round-robin pool is stable: same inputs ⇒ same plan (deterministic
        // generation, independent of DB heap order). Eaters never enters here.
        var recipes = await catalog.Recipes.AsNoTracking().Include(r => r.Ingredients)
            .Where(r => r.IsNutritionComputed).OrderBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        var diet = string.IsNullOrEmpty(intent.DietSlug)
            ? null
            : await catalog.DietTypes.AsNoTracking().FirstOrDefaultAsync(d => d.Slug == intent.DietSlug, ct).ConfigureAwait(false);

        var pool = RecipeFilter.Filter(recipes, intent, diet);
        var result = await generator.GenerateAsync(pool, intent, plan.TargetKcal, ct).ConfigureAwait(false);

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

        // #50 — plan correctness + latency. within_target mirrors the VERIFY tolerance; allergens_honored
        // re-asserts the gate over the final slots (the spec's "100% honor declared allergens").
        sw.Stop();
        var deviationPct = plan.TargetKcal > 0 ? (result.DailyAverage.Kcal - plan.TargetKcal) / plan.TargetKcal * 100.0 : 0;
        var withinTarget = result.Feasible && Math.Abs(deviationPct) <= PlanVerifier.KcalTolerancePct * 100.0;
        var allergensHonored = PlanVerifier.AllergensClear(result.Slots, intent.ExcludeKeywords);
        metrics?.PlanGenerated(result.Feasible, withinTarget, allergensHonored, deviationPct, sw.Elapsed.TotalSeconds);
    }

    public async Task<DietPlanDto?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.AsNoTracking().Include(p => p.Slots).Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        return plan is null ? null : ToDto(plan);
    }

    public async Task<DietPlanDto?> AcceptAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.Include(p => p.Slots).Include(p => p.Members)
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
        var plan = await db.MealPlans.AsNoTracking().Include(p => p.Slots).Include(p => p.Members)
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
        var plan = await db.MealPlans.AsNoTracking().Include(p => p.Slots).Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        if (plan is null || plan.Slots.Count == 0)
        {
            return null;
        }

        var shoppingDto = await shopping.ComputeAsync(userId, BuildShoppingLines(plan), mealPlanId: plan.Id, ct).ConfigureAwait(false);
        return (ToDto(plan), shoppingDto);
    }

    /// <summary>
    /// Attach the plan's members AFTER generation (they never influence it). The OWNER is always the
    /// primary (factor 1.0, target = plan target); the request carries only the OTHER people. When any
    /// member is present they are the sole quantity authority and <see cref="MealPlan.Eaters"/> is
    /// re-derived to the headcount (labels only); with no members the plan keeps its Eaters multiplier.
    /// </summary>
    /// <summary>Load the user's saved household as plan-member inputs, or null when they've saved nobody.</summary>
    private async Task<IReadOnlyList<PlanMemberInput>?> LoadSavedHouseholdAsync(Guid userId, CancellationToken ct)
    {
        var saved = await db.HouseholdMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToListAsync(ct).ConfigureAwait(false);
        return saved.Count == 0 ? null : HouseholdService.ToPlanMemberInputs(saved);
    }

    private static void AttachMembers(MealPlan plan, IReadOnlyList<PlanMemberInput>? members)
    {
        if (members is not { Count: > 0 })
        {
            return;
        }

        plan.ClearMembers();
        plan.AddMember(new PlanMember { Sequence = 0, Name = "You", TargetKcal = plan.TargetKcal });
        var seq = 1;
        foreach (var m in members)
        {
            var name = string.IsNullOrWhiteSpace(m.Name) ? "Member" : m.Name.Trim();
            plan.AddMember(new PlanMember
            {
                Sequence = seq++,
                Name = name.Length > 120 ? name[..120] : name,
                TargetKcal = m.TargetKcal is > 0 ? m.TargetKcal.Value : plan.TargetKcal,
            });
        }

        plan.Eaters = Math.Clamp(plan.Members.Count, 1, 9);
    }

    /// <summary>
    /// Group the plan's slots into per-recipe shopping lines, scaled by <see cref="MealPlan.PortionMultiplier"/>
    /// (Σ member portion factors, or Eaters when there are no members). This is the ONLY place the
    /// people-count touches quantities: ShoppingListService expands grams from these servings, so
    /// multiplying once here yields exactly the weighted-sum list. The multiplier is NEVER also passed
    /// into ShoppingListService (that would multiply the groceries twice).
    /// </summary>
    private static List<ShoppingRecipeLine> BuildShoppingLines(MealPlan plan) =>
        plan.Slots
            .GroupBy(s => s.RecipeId)
            // Each slot's Day is a BLOCK index; the block is cooked once but eaten for BlockDaysFor(day)
            // days, so weight its servings by that day-count before applying the people multiplier. With
            // BlockSize 1 every block covers 1 day ⇒ identical to the per-day list (no double count).
            .Select(g => new ShoppingRecipeLine(g.Key, g.Sum(s => s.Servings * plan.BlockDaysFor(s.Day)) * plan.PortionMultiplier))
            .ToList();

    /// <summary>
    /// The parallel batch-cook guide (#86): for each day-block, the recipes to cook once for the whole
    /// block — in RAW quantities (people × days), grouped by appliance — plus how many per-person/day
    /// portions to split into. Derived from the plan; nothing persisted.
    /// </summary>
    public async Task<BatchCookPlanDto?> GetBatchCookPlanAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var plan = await db.MealPlans.AsNoTracking().Include(p => p.Slots).Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        if (plan is null || plan.Slots.Count == 0)
        {
            return null;
        }

        var recipeIds = plan.Slots.Select(s => s.RecipeId).ToHashSet();
        var recipes = await catalog.Recipes.AsNoTracking().Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
        var ingredientIds = recipes.Values.SelectMany(r => r.Ingredients)
            .Where(i => i.IngredientId.HasValue).Select(i => i.IngredientId!.Value).ToHashSet();
        var ingredients = await catalog.Ingredients.AsNoTracking()
            .Where(i => ingredientIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);

        var portion = plan.PortionMultiplier;
        var blocks = new List<BatchCookBlockDto>();
        var cursor = 1; // calendar-day cursor, so labels read "Days 1–3", "Days 4–6", …
        foreach (var blockGroup in plan.Slots.GroupBy(s => s.Day).OrderBy(g => g.Key))
        {
            var block = blockGroup.Key;
            var blockDays = plan.BlockDaysFor(block);
            var start = cursor;
            var end = cursor + Math.Max(blockDays, 1) - 1;
            cursor = end + 1;
            var portions = blockDays * plan.Eaters;

            var steps = new List<BatchCookStepDto>();
            foreach (var recipeGroup in blockGroup.GroupBy(s => s.RecipeId))
            {
                if (!recipes.TryGetValue(recipeGroup.Key, out var recipe))
                {
                    continue;
                }

                // Cook for everyone, for the whole block: per-primary servings × people × days.
                var cookServings = recipeGroup.Sum(s => s.Servings) * portion * blockDays;
                var scale = recipe.Servings <= 0 ? cookServings : cookServings / recipe.Servings;
                var ings = recipe.Ingredients.Select(ri =>
                {
                    var ing = ri.IngredientId is { } iid && ingredients.TryGetValue(iid, out var x) ? x : null;
                    var raw = (ing?.RawWeight(ri.Grams) ?? ri.Grams) * scale;
                    return new BatchCookIngredientDto(ri.IngredientName, Math.Round(raw, 1));
                }).ToList();

                steps.Add(new BatchCookStepDto(
                    recipe.Name, string.IsNullOrWhiteSpace(recipe.CookMethod) ? "Other" : recipe.CookMethod,
                    Math.Round(cookServings, 2), portions, ings));
            }

            // Group by appliance (run the oven once for everything that bakes).
            var ordered = steps.OrderBy(s => s.Method, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.RecipeName).ToList();
            var daysLabel = start == end ? $"Day {start}" : $"Days {start}–{end}";
            blocks.Add(new BatchCookBlockDto(block, daysLabel, blockDays, portions, ordered));
        }

        return new BatchCookPlanDto(plan.Id, plan.Eaters, portion, plan.HorizonDays, plan.BlockSize, blocks);
    }

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
        p.AchievedKcal, p.AchievedProteinG, p.AchievedFatG, p.AchievedCarbG, p.Eaters, p.PortionMultiplier,
        // Order by Sequence (owner = 0) so the owner stays first regardless of DB row order on a re-query.
        p.Members.OrderBy(m => m.Sequence).Select(m => new PlanMemberDto(m.Name, m.TargetKcal, MealPlan.FactorOf(m.TargetKcal, p.TargetKcal))).ToList(),
        p.HorizonDays, p.BlockSize, p.NumBlocks,
        p.Message,
        p.Slots.OrderBy(s => s.Day).ThenBy(s => s.MealSlot)
            .Select(s => new PlanSlotDto(s.Day, s.MealSlot.ToString(), s.RecipeId, s.RecipeName, s.Servings,
                s.Kcal, s.ProteinG, s.FatG, s.CarbG)).ToList());
}
