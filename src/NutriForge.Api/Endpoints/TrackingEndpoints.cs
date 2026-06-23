using NutriForge.Api.Setup;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Common;

namespace NutriForge.Api.Endpoints;

/// <summary>Profile, derived targets, and the daily diary — all owner-scoped.</summary>
public static class TrackingEndpoints
{
    public static IEndpointRouteBuilder MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        MapProfile(app);
        MapTargets(app);
        MapDiary(app);
        return app;
    }

    private static void MapProfile(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/profile")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Profile");

        group.MapGet("/", async (ICurrentUser user, ProfileService profiles, CancellationToken ct) =>
            await profiles.GetAsync(user.CurrentUserId(), ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .WithName("GetProfile");

        group.MapPut("/", async (UpsertProfileRequest req, ICurrentUser user, ProfileService profiles, CancellationToken ct) =>
            Results.Ok(await profiles.UpsertAsync(user.CurrentUserId(), req, ct)))
            .WithName("UpsertProfile")
            .ValidatesBody<RouteHandlerBuilder, UpsertProfileRequest>();
    }

    private static void MapTargets(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/targets")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Targets");

        group.MapGet("/", async (ICurrentUser user, TargetService targets, CancellationToken ct) =>
            await targets.GetAsync(user.CurrentUserId(), ct) is { } t ? Results.Ok(t) : Results.NotFound())
            .WithName("GetTargets");
    }

    private static void MapDiary(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/diary")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Diary");

        group.MapGet("/", async (DateOnly? date, ICurrentUser user, DiaryService diary, IClock clock, CancellationToken ct) =>
            Results.Ok(await diary.GetDayAsync(user.CurrentUserId(), date ?? clock.Today, ct)))
            .WithName("GetDiaryDay");

        group.MapGet("/trend", async (DateOnly? endDate, int? days, ICurrentUser user, DiaryService diary, IClock clock, CancellationToken ct) =>
            Results.Ok(await diary.GetTrendAsync(user.CurrentUserId(), endDate ?? clock.Today, days ?? 7, ct)))
            .WithName("GetDiaryTrend");

        group.MapPost("/", async (AddDiaryEntryRequest req, ICurrentUser user, DiaryService diary, CancellationToken ct) =>
        {
            var entry = await diary.AddAsync(user.CurrentUserId(), req, ct);
            return Results.Created($"/api/v1/diary/{entry.Id}", entry);
        })
            .WithName("AddDiaryEntry")
            .ValidatesBody<RouteHandlerBuilder, AddDiaryEntryRequest>();

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, DiaryService diary, CancellationToken ct) =>
            await diary.DeleteAsync(user.CurrentUserId(), id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteDiaryEntry");

        // Natural-language logging: "2 eggs and toast" → confirmable candidates (nothing is logged
        // until the user confirms each through POST /diary). 503 when no LLM provider is configured.
        group.MapPost("/parse", async (ParseDiaryRequest req, ICurrentUser user, DiaryParseService parse, CancellationToken ct) =>
        {
            if (!parse.IsConfigured)
            {
                return Results.Problem(title: "Natural-language parsing unavailable",
                    detail: "No LLM provider is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Length > 500)
            {
                return Results.Problem(title: "Invalid text", detail: "Text must be 1-500 characters.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await parse.ParseAsync(
                user.CurrentUserId(), req.Text.Trim(), req.MealSlot ?? MealSlot.Snack, req.Date, ct);
            return Results.Ok(result);
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("ParseDiary");
    }

    /// <summary>Free-text meal description to parse into confirmable diary candidates.</summary>
    public sealed record ParseDiaryRequest(string Text, MealSlot? MealSlot, DateOnly? Date);
}
