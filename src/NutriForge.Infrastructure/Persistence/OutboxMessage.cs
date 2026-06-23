namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// A transactional-outbox row. External side effects (audit writes, future diet-plan job
/// dispatch and notifications) are committed in the same transaction as the domain change,
/// then relayed by the dispatcher — so a side effect never commits without its trigger.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Logical type, e.g. "audit". The dispatcher routes on this.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON payload.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
}
