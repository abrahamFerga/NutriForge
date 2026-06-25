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
        MapMeasurements(app);
        return app;
    }

    private static void MapMeasurements(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/measurements")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Measurements");

        // Recent history (default ~90 days), oldest-first, for a trend.
        group.MapGet("/", async (int? days, ICurrentUser user, MeasurementService measurements, CancellationToken ct) =>
            Results.Ok(await measurements.HistoryAsync(user.CurrentUserId(), days ?? 90, ct)))
            .WithName("GetMeasurements");

        // Log (or overwrite) today's (or a given date's) measurement.
        group.MapPost("/", async (LogMeasurementRequest req, ICurrentUser user, MeasurementService measurements, CancellationToken ct) =>
        {
            if (req.WeightKg is <= 0 or > 700)
            {
                return Results.Problem(title: "Invalid weight", detail: "Weight must be between 0 and 700 kg.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(await measurements.LogAsync(user.CurrentUserId(), req, ct));
        }).WithName("LogMeasurement");

        group.MapDelete("/{date}", async (DateOnly date, ICurrentUser user, MeasurementService measurements, CancellationToken ct) =>
            await measurements.DeleteAsync(user.CurrentUserId(), date, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteMeasurement");
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

        // Photo logging: a meal photo → vision recognition → confirmable candidates (catalog-matched
        // foods carry trusted numbers; off-catalog foods carry a flagged AI estimate). Nothing is
        // logged until the user confirms via POST /diary. 503 when no vision provider is configured.
        const long MaxImageBytes = 8 * 1024 * 1024;
        group.MapPost("/parse-photo", async (HttpRequest request, ICurrentUser user, FoodPhotoService photos, IClock clock, CancellationToken ct) =>
        {
            if (!photos.IsConfigured)
            {
                return Results.Problem(title: "Photo recognition unavailable",
                    detail: "No vision provider is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!request.HasFormContentType)
            {
                return Results.Problem(title: "Invalid request", detail: "Send the image as multipart/form-data.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("image") ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is null || file.Length == 0)
            {
                return Results.Problem(title: "No image", detail: "Attach a meal photo as the 'image' field.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (file.Length > MaxImageBytes)
            {
                return Results.Problem(title: "Image too large", detail: "Max image size is 8 MB.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            var mealSlot = Enum.TryParse<MealSlot>(form["mealSlot"], ignoreCase: true, out var slot) ? slot : MealSlot.Snack;
            var date = DateOnly.TryParse(form["date"], out var d) ? d : clock.Today;

            var result = await photos.AnalyzeAsync(
                user.CurrentUserId(), ms.ToArray(), file.ContentType ?? "image/jpeg", mealSlot, date, ct);
            return Results.Ok(result);
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .DisableAntiforgery()
            .WithName("ParseDiaryPhoto");
    }

    /// <summary>Free-text meal description to parse into confirmable diary candidates.</summary>
    public sealed record ParseDiaryRequest(string Text, MealSlot? MealSlot, DateOnly? Date);
}
