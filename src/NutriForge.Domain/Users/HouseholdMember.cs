using NutriForge.Domain.Common;

namespace NutriForge.Domain.Users;

/// <summary>
/// A person the user regularly cooks for (e.g. a partner or child), saved once and reused across every
/// diet plan so they no longer have to be re-added each time (#100). Owner-scoped. A null
/// <see cref="TargetKcal"/> means "same as me" ⇒ the plan's target ⇒ a portion factor of 1.0; this maps
/// 1:1 onto a plan's <c>PlanMember</c> when a plan is generated.
/// </summary>
public sealed class HouseholdMember : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>Display name, e.g. "Fiancée", "Sam".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>An optional relationship label for the UI (e.g. "Partner", "Son"); never affects nutrition.</summary>
    public string? Relationship { get; set; }

    /// <summary>Their daily kcal target; null means "same as me" (the owner's plan target).</summary>
    public double? TargetKcal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
