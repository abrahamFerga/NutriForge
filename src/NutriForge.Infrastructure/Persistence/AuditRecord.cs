namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// An append-only audit record. Lives in the <b>separate</b> audit database (ADR-0011) so it
/// survives independent of the operational store. PII values are redacted before they land here.
/// </summary>
public sealed class AuditRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Entity type that changed, e.g. "DiaryEntry".</summary>
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Added / Modified / Deleted.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The acting user's id (or "system" for imports).</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Changed columns with PII redacted, as JSON.</summary>
    public string Changes { get; set; } = "{}";
}
