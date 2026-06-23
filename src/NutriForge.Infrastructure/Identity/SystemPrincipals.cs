using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.Identity;

/// <summary>
/// Non-interactive principal for background hosts (e.g. the import worker): unauthenticated,
/// owns no user data. Audit records attributed to it read as "system".
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Subject => null;
    public string? Email => null;
    public bool IsAuthenticated => false;
    public bool IsInRole(string role) => false;
}

/// <summary>System clock — the production <see cref="IClock"/>.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
