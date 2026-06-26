using NutriForge.Domain.Common;

namespace NutriForge.Domain.Diary;

/// <summary>
/// A day's water intake (#72): a running total in millilitres plus the goal that applied that day. One
/// row per (user, date) — logging accumulates into <see cref="Ml"/>; the goal is snapshotted per day so
/// history reflects the target in force at the time. Owner-scoped and audited like the rest of the diary.
/// </summary>
public sealed class HydrationDay : IUserOwned, IAuditable, ITimestamped
{
    /// <summary>Fallback daily goal (ml) for a user who has never set one — a common ~2.5 L default.</summary>
    public const int DefaultGoalMl = 2500;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public DateOnly Date { get; set; }

    public int Ml { get; set; }
    public int GoalMl { get; set; } = DefaultGoalMl;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
