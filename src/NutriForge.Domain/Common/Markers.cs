namespace NutriForge.Domain.Common;

/// <summary>
/// Marks an entity as owned by a single user. A global EF Core query filter on
/// <see cref="UserId"/> enforces per-user isolation (ADR-0001: the user is the tenant).
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; }
}

/// <summary>
/// Marks an entity whose mutations must be captured in the append-only audit log
/// (written to the separate audit database via the outbox — ADR-0011).
/// </summary>
public interface IAuditable
{
}

/// <summary>Carries the standard creation/modification timestamps stamped by the persistence layer.</summary>
public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Marks a property (on an entity) as personal data for GDPR export and audit redaction.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PiiAttribute : Attribute
{
}
