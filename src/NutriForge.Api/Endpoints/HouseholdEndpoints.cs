using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.DietGen;

namespace NutriForge.Api.Endpoints;

/// <summary>
/// The saved household (#100): people the user regularly cooks for, persisted once and auto-attached to
/// every diet plan that doesn't override them. Owner-scoped.
/// </summary>
public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/household")
            .RequireAuthorization(Policies.OwnerOnly)
            .WithTags("Household");

        group.MapGet("/", async (ICurrentUser user, HouseholdService household, CancellationToken ct) =>
            Results.Ok(await household.ListAsync(user.CurrentUserId(), ct)))
            .WithName("ListHousehold");

        group.MapPost("/", async (UpsertHouseholdMemberRequest req, ICurrentUser user, HouseholdService household, CancellationToken ct) =>
        {
            if (!IsValid(req))
            {
                return InvalidMember();
            }

            var created = await household.AddAsync(user.CurrentUserId(), req, ct);
            return created is null
                ? Results.Problem(title: "Household is full", detail: "You can save up to 12 household members.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Created($"/api/v1/household/{created.Id}", created);
        }).WithName("AddHouseholdMember");

        group.MapPut("/{id:guid}", async (Guid id, UpsertHouseholdMemberRequest req, ICurrentUser user, HouseholdService household, CancellationToken ct) =>
        {
            if (!IsValid(req))
            {
                return InvalidMember();
            }

            var updated = await household.UpdateAsync(user.CurrentUserId(), id, req, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).WithName("UpdateHouseholdMember");

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, HouseholdService household, CancellationToken ct) =>
            await household.DeleteAsync(user.CurrentUserId(), id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteHouseholdMember");

        return app;
    }

    private static bool IsValid(UpsertHouseholdMemberRequest? req) =>
        req is not null
        && !string.IsNullOrWhiteSpace(req.Name)
        && req.Name.Trim().Length <= 120
        && req.TargetKcal is null or (> 0 and <= 10000);

    private static IResult InvalidMember() => Results.Problem(
        title: "Invalid household member",
        detail: "A name (≤120 chars) is required; kcal target, if given, must be between 1 and 10000.",
        statusCode: StatusCodes.Status400BadRequest);
}
