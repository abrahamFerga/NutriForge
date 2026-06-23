using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Nutrition;
using NutriForge.Domain.Users;

namespace NutriForge.Application.Tracking;

public sealed record TargetDto(
    double Kcal, double ProteinG, double FatG, double CarbG, string Formula, DateTimeOffset ComputedAt);

/// <summary>
/// Owns the derived <see cref="Target"/>. Recomputed from the profile (via the pure
/// <see cref="NutritionTargets"/> math) whenever the profile changes — in the same transaction.
/// </summary>
public sealed class TargetService(IAppDbContext db, IClock clock)
{
    /// <summary>
    /// Recompute and upsert the target for <paramref name="profile"/>. Caller saves; this only
    /// stages the change so it commits atomically with the profile edit.
    /// </summary>
    public async Task<Target> RecomputeAsync(Profile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var computed = NutritionTargets.Compute(profile.ToTargetInputs(clock.Today));

        var target = await db.Targets.FirstOrDefaultAsync(t => t.UserId == profile.UserId, ct)
            .ConfigureAwait(false);
        if (target is null)
        {
            target = new Target { UserId = profile.UserId };
            db.Targets.Add(target);
        }

        target.ApplyFrom(computed, profile.Version, clock.UtcNow);
        return target;
    }

    public async Task<TargetDto?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var t = await db.Targets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct)
            .ConfigureAwait(false);
        return t is null ? null : new TargetDto(t.Kcal, t.ProteinG, t.FatG, t.CarbG, t.Formula, t.ComputedAt);
    }
}
