namespace NutriForge.Application.Abstractions;

/// <summary>Abstracts "now" so time-dependent logic is deterministic under test.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
