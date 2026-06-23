using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Api.Endpoints;

/// <summary>GDPR compliance: Article 20 data export and Article 17 erasure.</summary>
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

            return Results.Ok(new
            {
                exportedAt = DateTimeOffset.UtcNow,
                user = new { user?.Id, user?.OidcSubject, user?.Email, user?.DisplayName },
                profile,
                target,
                diary,
            });
        }).WithName("ExportMyData");

        // Article 17 — erasure. Purges every user-owned row and tombstones the identity mirror.
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

        return app;
    }
}
