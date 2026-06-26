using NutriForge.Api.Setup;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.DietGen;

namespace NutriForge.Api.Endpoints;

/// <summary>
/// Diet generation + the closed loop (flagship): async generate (202) → poll → accept →
/// shopping list → adherence. The pipeline is deterministic; the LLM only parses the desire.
/// </summary>
public static class DietPlanEndpoints
{
    public static IEndpointRouteBuilder MapDietPlanEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/diet-plans")
            .RequireAuthorization(Policies.OwnerOnly)
            .WithTags("DietPlans");

        // Async generate — returns 202 with the plan id; the worker fills it in.
        group.MapPost("/", async (CreateDietPlanRequest req, ICurrentUser user, DietPlanService plans, CancellationToken ct) =>
        {
            var plan = await plans.CreateAsync(user.CurrentUserId(), req, ct);
            return Results.Accepted($"/api/v1/diet-plans/{plan.Id}", plan);
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("CreateDietPlan");

        // "Make me a full diet" (#101): generate recipes to fill the pool if it's too thin, then plan.
        // The user authors nothing. 503 when no AI provider is configured.
        group.MapPost("/auto", async (CreateDietPlanRequest req, ICurrentUser user, AutoDietService auto, CancellationToken ct) =>
        {
            if (!auto.IsConfigured)
            {
                return Results.Problem(title: "Auto diet unavailable",
                    detail: "Generating a full diet needs an AI provider to be configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var result = await auto.CreateAsync(user.CurrentUserId(), req, ct);
            return Results.Accepted($"/api/v1/diet-plans/{result.Plan.Id}",
                new { plan = result.Plan, recipesGenerated = result.RecipesGenerated });
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("AutoCreateDietPlan");

        // Poll until status is ready | infeasible.
        group.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, DietPlanService plans, CancellationToken ct) =>
            await plans.GetAsync(user.CurrentUserId(), id, ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .WithName("GetDietPlan");

        group.MapPost("/{id:guid}/accept", async (Guid id, ICurrentUser user, DietPlanService plans, CancellationToken ct) =>
            await plans.AcceptAsync(user.CurrentUserId(), id, ct) is { } p
                ? Results.Ok(p)
                : Results.Problem(title: "Cannot accept", detail: "The plan is not ready.", statusCode: StatusCodes.Status409Conflict))
            .WithName("AcceptDietPlan");

        // Closed loop: accepted plan → one consolidated shopping list.
        group.MapPost("/{id:guid}/shopping-list", async (Guid id, ICurrentUser user, DietPlanService plans, CancellationToken ct) =>
            await plans.GenerateShoppingListAsync(user.CurrentUserId(), id, ct) is { } list
                ? Results.Created($"/api/v1/shopping-lists/{list.Id}", list)
                : Results.NotFound())
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("DietPlanShoppingList");

        // Closed loop: how closely logged intake tracked the plan this week.
        group.MapGet("/{id:guid}/adherence", async (Guid id, ICurrentUser user, DietPlanService plans, CancellationToken ct) =>
            Results.Ok(await plans.AdherenceAsync(user.CurrentUserId(), id, ct)))
            .WithName("DietPlanAdherence");

        // Parallel batch-cook guide: one cook session per day-block, raw quantities grouped by appliance.
        group.MapGet("/{id:guid}/batch-cook", async (Guid id, ICurrentUser user, DietPlanService plans, CancellationToken ct) =>
            await plans.GetBatchCookPlanAsync(user.CurrentUserId(), id, ct) is { } guide ? Results.Ok(guide) : Results.NotFound())
            .WithName("DietPlanBatchCook");

        // Printable meal-prep sheet: per-person/day meals + the days×people consolidated shopping list.
        group.MapGet("/{id:guid}/pdf", async (Guid id, ICurrentUser user, DietPlanService plans, CancellationToken ct) =>
        {
            var data = await plans.GetPlanWithShoppingAsync(user.CurrentUserId(), id, ct);
            if (data is null)
            {
                return Results.NotFound();
            }

            var bytes = Pdf.MealPlanPdf.Render(data.Value.Plan, data.Value.Shopping);
            return Results.File(bytes, "application/pdf", "nutriforge-meal-plan.pdf");
        }).WithName("DietPlanPdf");

        return app;
    }
}
