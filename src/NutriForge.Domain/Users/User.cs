using NutriForge.Domain.Common;

namespace NutriForge.Domain.Users;

/// <summary>
/// Local mirror of an authenticated identity. The source of truth is the OIDC provider
/// (Entra External ID); this row anchors all user-owned data by <see cref="Id"/>.
/// </summary>
public sealed class User : IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>The OIDC subject claim — the stable external identifier.</summary>
    public string OidcSubject { get; set; } = string.Empty;

    [Pii]
    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static User FromSubject(string subject, string? email, string? displayName) => new()
    {
        OidcSubject = subject,
        Email = email,
        DisplayName = displayName,
    };
}
