using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Users;

namespace NutriForge.Application.Tracking;

/// <summary>
/// Owner-scoped profile reads/writes. Every write bumps the profile version and recomputes the
/// derived target atomically (SPEC: targets recomputed on profile change).
/// </summary>
public sealed class ProfileService(IAppDbContext db, TargetService targets)
{
    public async Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var p = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct)
            .ConfigureAwait(false);
        return p?.ToDto();
    }

    public async Task<ProfileDto> UpsertAsync(Guid userId, UpsertProfileRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var profile = await db.Profiles.FirstOrDefaultAsync(x => x.UserId == userId, ct)
            .ConfigureAwait(false);
        if (profile is null)
        {
            profile = new Profile { UserId = userId };
            db.Profiles.Add(profile);
        }

        profile.Sex = req.Sex;
        profile.BirthDate = req.BirthDate;
        profile.HeightCm = req.HeightCm;
        profile.WeightKg = req.WeightKg;
        profile.BodyFatPct = req.BodyFatPct;
        profile.Activity = req.Activity;
        profile.Goal = req.Goal;
        profile.MacroStrategy = req.MacroStrategy;
        profile.Allergens = [.. req.Allergens ?? []];
        profile.Dislikes = [.. req.Dislikes ?? []];
        profile.PreferredDiets = [.. req.PreferredDiets ?? []];
        profile.Version++;

        await targets.RecomputeAsync(profile, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return profile.ToDto();
    }
}
