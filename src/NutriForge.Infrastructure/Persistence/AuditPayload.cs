namespace NutriForge.Infrastructure.Persistence;

/// <summary>The JSON shape carried through the outbox from the audit interceptor to the dispatcher.</summary>
public sealed record AuditPayload(
    DateTimeOffset OccurredAt,
    string EntityType,
    string EntityId,
    string Action,
    string ActorId,
    IReadOnlyDictionary<string, string?> Changes);
