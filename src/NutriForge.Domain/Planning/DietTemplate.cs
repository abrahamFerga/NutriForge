using NutriForge.Domain.Common;

namespace NutriForge.Domain.Planning;

/// <summary>
/// A saved set of diet-plan parameters (#102) the user can re-generate in one tap — because they tend to
/// create the same kind of plan repeatedly. Owner-scoped. Stores only the generation inputs (diet, target,
/// horizon, block size, prep cap, free-text desire); the people to cook for come from the saved household
/// (#100), which is attached automatically at generation time. No nutrition numbers live here.
/// </summary>
public sealed class DietTemplate : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>Display name, e.g. "My usual cut".</summary>
    public string Name { get; set; } = string.Empty;

    public string? DietSlug { get; set; }
    public double? KcalTarget { get; set; }
    public int? MaxPrepMinutes { get; set; }
    public int? MealsPerDay { get; set; }
    public int HorizonDays { get; set; } = 7;
    public int BlockSize { get; set; } = 1;
    public string? Desire { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
