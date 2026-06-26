using NutriForge.Api.Setup;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.DietGen;

namespace NutriForge.Api.Endpoints;

/// <summary>
/// Saved diet presets (#102): named generation parameters the user re-runs in one tap, since they tend to
/// create the same kind of plan every time. The <c>/generate</c> action pairs a preset with the diet-plan
/// generator and the saved household (#100). Owner-scoped.
/// </summary>
public static class DietTemplateEndpoints
{
    public static IEndpointRouteBuilder MapDietTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/diet-templates")
            .RequireAuthorization(Policies.OwnerOnly)
            .WithTags("DietTemplates");

        group.MapGet("/", async (ICurrentUser user, DietTemplateService templates, CancellationToken ct) =>
            Results.Ok(await templates.ListAsync(user.CurrentUserId(), ct)))
            .WithName("ListDietTemplates");

        group.MapPost("/", async (UpsertDietTemplateRequest req, ICurrentUser user, DietTemplateService templates, CancellationToken ct) =>
        {
            if (!IsValid(req))
            {
                return InvalidTemplate();
            }

            var created = await templates.AddAsync(user.CurrentUserId(), req, ct);
            return created is null
                ? Results.Problem(title: "Too many saved diets", detail: "You can save up to 24 diet presets.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Created($"/api/v1/diet-templates/{created.Id}", created);
        }).WithName("CreateDietTemplate");

        group.MapPut("/{id:guid}", async (Guid id, UpsertDietTemplateRequest req, ICurrentUser user, DietTemplateService templates, CancellationToken ct) =>
        {
            if (!IsValid(req))
            {
                return InvalidTemplate();
            }

            var updated = await templates.UpdateAsync(user.CurrentUserId(), id, req, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).WithName("UpdateDietTemplate");

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, DietTemplateService templates, CancellationToken ct) =>
            await templates.DeleteAsync(user.CurrentUserId(), id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteDietTemplate");

        // One-tap regenerate: build the saved request and run the generator (saved household auto-attaches).
        group.MapPost("/{id:guid}/generate", async (Guid id, ICurrentUser user, DietTemplateService templates, AutoDietService auto, CancellationToken ct) =>
        {
            var uid = user.CurrentUserId();
            var req = await templates.ToCreateRequestAsync(uid, id, ct);
            if (req is null)
            {
                return Results.NotFound();
            }

            // Same as "Generate my plan": the agent writes fresh recipes for the plan (catalog untouched).
            var result = await auto.CreateAsync(uid, req, ct);
            return Results.Ok(result.Plan);
        }).RequireRateLimiting(RateLimitPolicies.Expensive).WithName("GenerateFromDietTemplate");

        return app;
    }

    private static bool IsValid(UpsertDietTemplateRequest? req) =>
        req is not null
        && !string.IsNullOrWhiteSpace(req.Name)
        && req.Name.Trim().Length <= 120
        && req.KcalTarget is null or (> 0 and <= 10000)
        && req.HorizonDays is null or (>= 1 and <= 30)
        && req.BlockSize is null or (>= 1 and <= 30);

    private static IResult InvalidTemplate() => Results.Problem(
        title: "Invalid diet preset",
        detail: "A name (≤120 chars) is required; kcal ≤10000, days 1–30, block size 1–30 when provided.",
        statusCode: StatusCodes.Status400BadRequest);
}
