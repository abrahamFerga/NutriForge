namespace NutriForge.Application.Abstractions;

/// <summary>
/// The authenticated principal for the current request. Drives the per-user EF query filter
/// and owner-scoped authorization. In a background/import context, <see cref="IsAuthenticated"/>
/// is false and <see cref="UserId"/> is null.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The local <c>User.Id</c>, or null when unauthenticated (e.g. import worker).</summary>
    Guid? UserId { get; }

    /// <summary>The OIDC subject claim.</summary>
    string? Subject { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);
}
