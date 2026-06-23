using System.Security.Claims;
using NutriForge.Application.Abstractions;

namespace NutriForge.Api.Auth;

/// <summary>
/// The request-scoped authenticated principal. Claims come from the OIDC token; the local
/// <see cref="UserId"/> is the provisioned <c>User.Id</c> stashed by
/// <see cref="UserProvisioningMiddleware"/> after authentication.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>Key under which the provisioning middleware stores the local user id.</summary>
    public const string LocalUserIdItem = "LocalUserId";

    private HttpContext? Http => accessor.HttpContext;

    public Guid? UserId =>
        Http?.Items.TryGetValue(LocalUserIdItem, out var v) == true && v is Guid id ? id : null;

    public string? Subject =>
        Http?.User.FindFirstValue("sub") ?? Http?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Email => Http?.User.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => Http?.User.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => Http?.User.IsInRole(role) == true;
}
