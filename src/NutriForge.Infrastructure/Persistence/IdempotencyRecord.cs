namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// Records the result of a mutation keyed by its client-supplied <c>Idempotency-Key</c>, scoped
/// to the user. A retry within the 24h replay window returns the stored response verbatim.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Key { get; set; } = string.Empty;
    public Guid UserId { get; set; }

    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>End of the replay window; the dispatcher/cleanup may purge rows past this.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
