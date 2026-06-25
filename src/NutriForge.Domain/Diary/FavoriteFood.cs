using NutriForge.Domain.Common;

namespace NutriForge.Domain.Diary;

/// <summary>
/// A food the user starred for one-tap logging. Owner-scoped; a thin link to a catalog food (the food
/// details are read live so a relabel/renutrition is reflected). At most one per (user, food).
/// </summary>
public sealed class FavoriteFood : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid FoodId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
