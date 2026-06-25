using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.Consent;
using NutriForge.Domain.Consent;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Api.Endpoints;

/// <summary>Account surface: GDPR Article 20 export, Article 17 erasure, and consent (Art. 6/7) capture.</summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/me")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Me");

        // Article 20 — full data-bundle export. The query filter scopes every set to the caller.
        group.MapGet("/export", async (ICurrentUser current, NutriForgeDbContext db, CancellationToken ct) =>
        {
            var userId = current.CurrentUserId();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(ct);
            var target = await db.Targets.AsNoTracking().FirstOrDefaultAsync(ct);
            var diary = await db.DiaryEntries.AsNoTracking().OrderBy(e => e.Date).ToListAsync(ct);
            // GDPR Art. 7(1) — the demonstrable consent trail travels with the export.
            var consent = await db.ConsentRecords.AsNoTracking().OrderBy(r => r.RecordedAt).ToListAsync(ct);

            return Results.Ok(new
            {
                exportedAt = DateTimeOffset.UtcNow,
                user = new { user?.Id, user?.OidcSubject, user?.Email, user?.DisplayName },
                profile,
                target,
                diary,
                consent,
            });
        }).WithName("ExportMyData");

        // Article 17 — erasure. Purges every user-owned row and tombstones the identity mirror. The consent
        // trail is deliberately retained (Art. 17(3)(b)/(e): proof of lawful basis, defence of legal claims).
        group.MapDelete("/", async (ICurrentUser current, NutriForgeDbContext db, CancellationToken ct) =>
        {
            var userId = current.CurrentUserId();

            db.DiaryEntries.RemoveRange(await db.DiaryEntries.ToListAsync(ct));
            db.Targets.RemoveRange(await db.Targets.ToListAsync(ct));
            db.Profiles.RemoveRange(await db.Profiles.ToListAsync(ct));

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is not null)
            {
                user.Email = null;
                user.DisplayName = "(erased)";
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithName("DeleteMyAccount");

        MapConsent(group);
        return app;
    }

    private static void MapConsent(RouteGroupBuilder me)
    {
        // Current standing for every catalogued consent (granted/withdrawn, version, whether action is due).
        me.MapGet("/consent", async (ICurrentUser current, ConsentService consent, CancellationToken ct) =>
            Results.Ok(await consent.StatusAsync(current.CurrentUserId(), ct)))
            .WithName("GetConsent");

        // The append-only decision trail (also embedded in /me/export).
        me.MapGet("/consent/history", async (ICurrentUser current, ConsentService consent, CancellationToken ct) =>
            Results.Ok(await consent.HistoryAsync(current.CurrentUserId(), ct)))
            .WithName("GetConsentHistory");

        // Record a grant/decline at the current policy version (used at signup/onboarding and on Profile).
        me.MapPost("/consent", async (RecordConsentRequest req, ICurrentUser current, ConsentService consent, CancellationToken ct) =>
        {
            if (req is null || !Enum.IsDefined(req.Type))
            {
                return Results.Problem(title: "Unknown consent type.", statusCode: StatusCodes.Status400BadRequest);
            }

            await consent.RecordAsync(current.CurrentUserId(), req.Type, req.Granted, ct);
            return Results.Ok(await consent.StatusAsync(current.CurrentUserId(), ct));
        }).WithName("RecordConsent");

        // Withdrawal path (Art. 7(3)) — appends a "not granted" row.
        me.MapPost("/consent/{type}/withdraw", async (ConsentType type, ICurrentUser current, ConsentService consent, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(type))
            {
                return Results.Problem(title: "Unknown consent type.", statusCode: StatusCodes.Status400BadRequest);
            }

            await consent.WithdrawAsync(current.CurrentUserId(), type, ct);
            return Results.Ok(await consent.StatusAsync(current.CurrentUserId(), ct));
        }).WithName("WithdrawConsent");
    }
}
