using NutriForge.Domain.Common;

namespace NutriForge.Domain.Diary;

/// <summary>
/// A dated body-measurement entry — weight (always) plus optional body-fat % and waist — tracked over
/// time so the user sees a trend alongside their calories. One entry per day (the latest reading wins);
/// owner-scoped and audited like the rest of the user's data.
/// </summary>
public sealed class BodyMeasurement : IUserOwned, IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public DateOnly Date { get; set; }

    public double WeightKg { get; set; }
    public double? BodyFatPct { get; set; }
    public double? WaistCm { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
