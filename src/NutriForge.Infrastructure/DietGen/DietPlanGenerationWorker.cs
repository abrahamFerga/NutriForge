using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NutriForge.Application.DietGen;
using NutriForge.Domain.Planning;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Infrastructure.DietGen;

/// <summary>
/// Consumes the async diet-plan generation jobs (ADR-0008): polls for plans in
/// <see cref="PlanStatus.Generating"/>, runs the deterministic FILTER→SELECT→VERIFY→REPAIR pipeline,
/// and finishes each plan to Ready or Infeasible. Operates as "system" — it bypasses the per-user
/// query filter and scopes explicitly to each plan's user.
/// </summary>
public sealed partial class DietPlanGenerationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DietPlanGenerationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // a worker must not die on one bad job
            catch (Exception ex)
            {
                LogBatchFailed(logger, ex);
            }
#pragma warning restore CA1031
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriForgeDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<DietPlanGenerator>();

        var pending = await db.MealPlans
            .IgnoreQueryFilters()
            .Include(p => p.Slots)
            .Where(p => p.Status == PlanStatus.Generating)
            .OrderBy(p => p.CreatedAt)
            .Take(10)
            .ToListAsync(ct).ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        List<Domain.Recipes.Recipe> recipes;
        try
        {
            recipes = await db.Recipes.IgnoreQueryFilters().Include(r => r.Ingredients)
                .Where(r => r.IsNutritionComputed).ToListAsync(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // surface the batch failure onto the plans rather than stalling them
        catch (Exception ex)
        {
            foreach (var plan in pending)
            {
                plan.Status = PlanStatus.Infeasible;
                plan.Message = $"Generation failed (pool load): {ex.GetType().Name}: {ex.Message}";
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }
#pragma warning restore CA1031

        foreach (var plan in pending)
        {
            await GenerateOneAsync(db, generator, plan, recipes, ct).ConfigureAwait(false);
        }
    }

    private async Task GenerateOneAsync(
        NutriForgeDbContext db, DietPlanGenerator generator, MealPlan plan,
        List<Domain.Recipes.Recipe> recipes, CancellationToken ct)
    {
        try
        {
            var intent = JsonSerializer.Deserialize<DietIntent>(plan.IntentJson, Json)
                ?? new DietIntent(plan.TargetKcal, null, null, [], null, 3, plan.HorizonDays);

            var diet = string.IsNullOrEmpty(intent.DietSlug)
                ? null
                : await db.DietTypes.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Slug == intent.DietSlug, ct).ConfigureAwait(false);

            // The batch pool was loaded with IgnoreQueryFilters (system context — every user's recipes), so
            // scope it to THIS plan's owner before selecting: global recipes (no owner) plus the plan
            // owner's own. Without this a user's private recipe could leak into another user's plan.
            var visible = recipes.Where(r => r.OwnerUserId is null || r.OwnerUserId == plan.UserId).ToList();
            var pool = RecipeFilter.Filter(visible, intent, diet);
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
#pragma warning disable CA1031 // a single failed plan must not crash the batch
        catch (Exception ex)
        {
            LogPlanFailed(logger, plan.Id, ex);
            plan.Status = PlanStatus.Infeasible;
            plan.Message = $"Generation failed: {ex.GetType().Name}: {ex.Message}";
        }
#pragma warning restore CA1031

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Diet-plan generation batch failed; will retry.")]
    private static partial void LogBatchFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Diet-plan {PlanId} generation failed.")]
    private static partial void LogPlanFailed(ILogger logger, Guid planId, Exception ex);
}
