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
        MapHydration(app);
        MapFavorites(app);
        MapMealTemplates(app);
        return app;
    }

    private static void MapHydration(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/hydration")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Hydration");

        // Today's (or a given date's) intake + goal.
        group.MapGet("/", async (DateOnly? date, ICurrentUser user, HydrationService hydration, CancellationToken ct) =>
            Results.Ok(await hydration.GetDayAsync(user.CurrentUserId(), date, ct)))
            .WithName("GetHydration");

        // Recent history (default ~30 days), oldest-first.
        group.MapGet("/history", async (int? days, ICurrentUser user, HydrationService hydration, CancellationToken ct) =>
            Results.Ok(await hydration.HistoryAsync(user.CurrentUserId(), days ?? 30, ct)))
            .WithName("GetHydrationHistory");

        // Add water (a negative amount undoes); clamped at zero.
        group.MapPost("/", async (LogHydrationRequest req, ICurrentUser user, HydrationService hydration, CancellationToken ct) =>
        {
            if (req is null || req.Ml == 0 || req.Ml is < -5000 or > 5000)
            {
                return Results.Problem(title: "Invalid amount", detail: "Amount must be a non-zero value within ±5000 ml.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(await hydration.LogAsync(user.CurrentUserId(), req, ct));
        }).WithName("LogHydration");

        // Set the daily goal.
        group.MapPut("/goal", async (SetHydrationGoalRequest req, ICurrentUser user, HydrationService hydration, CancellationToken ct) =>
        {
            if (req is null || req.GoalMl <= 0)
            {
                return Results.Problem(title: "Invalid goal", detail: "Goal must be a positive number of millilitres.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(await hydration.SetGoalAsync(user.CurrentUserId(), req, ct));
        }).WithName("SetHydrationGoal");
    }

    private static void MapMealTemplates(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/meal-templates")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("MealTemplates");

        group.MapGet("/", async (ICurrentUser user, MealTemplateService templates, CancellationToken ct) =>
            Results.Ok(await templates.ListAsync(user.CurrentUserId(), ct)))
            .WithName("ListMealTemplates");

        // Create from an explicit set of foods, or by snapshotting a day's meal (when `date` is set).
        group.MapPost("/", async (CreateMealTemplateRequest req, ICurrentUser user, MealTemplateService templates, CancellationToken ct) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Name) || req.Items is not { Count: > 0 })
            {
                return Results.Problem(title: "A name and at least one food are required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var created = await templates.CreateAsync(user.CurrentUserId(), req.Name, req.Items, ct);
            return created is null
                ? Results.Problem(title: "None of the foods are in the catalog.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Created($"/api/v1/meal-templates/{created.Id}", created);
        }).WithName("CreateMealTemplate");

        group.MapPost("/from-day", async (SaveMealFromDayRequest req, ICurrentUser user, MealTemplateService templates, CancellationToken ct) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.Problem(title: "A name is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var created = await templates.CreateFromDayAsync(user.CurrentUserId(), req.Name, req.Date, req.MealSlot, ct);
            return created is null
                ? Results.Problem(title: "That meal has no logged foods to save.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Created($"/api/v1/meal-templates/{created.Id}", created);
        }).WithName("CreateMealTemplateFromDay");

        // Log a saved meal into a day's slot (defaults to today). Returns how many entries were created.
        group.MapPost("/{id:guid}/log", async (Guid id, LogMealTemplateRequest req, ICurrentUser user, MealTemplateService templates, IClock clock, CancellationToken ct) =>
        {
            if (req is null)
            {
                return Results.Problem(title: "A meal slot is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var logged = await templates.LogAsync(user.CurrentUserId(), id, req.Date ?? clock.Today, req.MealSlot, ct);
            return logged is null ? Results.NotFound() : Results.Ok(new { logged = logged.Value });
        }).WithName("LogMealTemplate");

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, MealTemplateService templates, CancellationToken ct) =>
            await templates.DeleteAsync(user.CurrentUserId(), id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteMealTemplate");
    }

    private static void MapFavorites(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/favorites")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Favorites");

        group.MapGet("/", async (ICurrentUser user, FavoriteService favorites, CancellationToken ct) =>
            Results.Ok(await favorites.ListAsync(user.CurrentUserId(), ct)))
            .WithName("ListFavorites");

        group.MapPost("/{foodId:guid}", async (Guid foodId, ICurrentUser user, FavoriteService favorites, CancellationToken ct) =>
            await favorites.AddAsync(user.CurrentUserId(), foodId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("AddFavorite");

        group.MapDelete("/{foodId:guid}", async (Guid foodId, ICurrentUser user, FavoriteService favorites, CancellationToken ct) =>
            await favorites.RemoveAsync(user.CurrentUserId(), foodId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("RemoveFavorite");
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

        // Quick-add (#69): distinct recently-logged foods, for one-tap re-logging.
        group.MapGet("/recents", async (int? limit, ICurrentUser user, DiaryService diary, CancellationToken ct) =>
            Results.Ok(await diary.RecentFoodsAsync(user.CurrentUserId(), limit ?? 12, ct)))
            .WithName("GetRecentFoods");

        // Quick-add: copy a whole day's entries onto another day (defaults yesterday → today).
        group.MapPost("/copy", async (CopyDayRequest? req, ICurrentUser user, DiaryService diary, IClock clock, CancellationToken ct) =>
        {
            var to = req?.To ?? clock.Today;
            var from = req?.From ?? to.AddDays(-1);
            var copied = await diary.CopyDayAsync(user.CurrentUserId(), from, to, ct);
            return Results.Ok(new { copied, from, to });
        }).WithName("CopyDiaryDay");

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
