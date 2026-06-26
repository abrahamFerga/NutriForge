using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Users;

namespace NutriForge.Application.DietGen;

/// <summary>
/// CRUD for the user's saved household — the people they regularly cook for (#100). Owner-scoped. A
/// saved member is reused across every diet plan (see <see cref="DietPlanService"/>), so the user no
/// longer re-adds their partner/children to each new plan. The stored kcal target (or "same as me")
/// maps straight onto a plan's portion factor when a plan is generated.
/// </summary>
public sealed class HouseholdService(IAppDbContext db)
{
    private const int MaxMembers = 12;
    private const int MaxNameLength = 120;

    public async Task<IReadOnlyList<HouseholdMemberDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var members = await db.HouseholdMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. members.Select(ToDto)];
    }

    /// <summary>Add a member. Returns null when the household is already at its cap.</summary>
    public async Task<HouseholdMemberDto?> AddAsync(Guid userId, UpsertHouseholdMemberRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var count = await db.HouseholdMembers.CountAsync(m => m.UserId == userId, ct).ConfigureAwait(false);
        if (count >= MaxMembers)
        {
            return null;
        }

        var member = new HouseholdMember { UserId = userId };
        Apply(member, req);
        db.HouseholdMembers.Add(member);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(member);
    }

    /// <summary>Update an existing member. Returns null when it doesn't exist for this user.</summary>
    public async Task<HouseholdMemberDto?> UpdateAsync(Guid userId, Guid id, UpsertHouseholdMemberRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var member = await db.HouseholdMembers.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct).ConfigureAwait(false);
        if (member is null)
        {
            return null;
        }

        Apply(member, req);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(member);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var member = await db.HouseholdMembers.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct).ConfigureAwait(false);
        if (member is null)
        {
            return false;
        }

        db.HouseholdMembers.Remove(member);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The saved household as plan-member inputs (oldest-first), for <see cref="DietPlanService"/> to attach
    /// when a plan request carries no explicit members. Empty when the user has saved nobody.
    /// </summary>
    public static IReadOnlyList<PlanMemberInput> ToPlanMemberInputs(IEnumerable<HouseholdMember> members) =>
        [.. members.OrderBy(m => m.CreatedAt).Select(m => new PlanMemberInput(m.Name, m.TargetKcal))];

    private static void Apply(HouseholdMember member, UpsertHouseholdMemberRequest req)
    {
        var name = string.IsNullOrWhiteSpace(req.Name) ? "Member" : req.Name.Trim();
        member.Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        member.Relationship = string.IsNullOrWhiteSpace(req.Relationship) ? null : req.Relationship.Trim();
        member.TargetKcal = req.TargetKcal is > 0 ? req.TargetKcal : null;
    }

    private static HouseholdMemberDto ToDto(HouseholdMember m) => new(m.Id, m.Name, m.Relationship, m.TargetKcal);
}
