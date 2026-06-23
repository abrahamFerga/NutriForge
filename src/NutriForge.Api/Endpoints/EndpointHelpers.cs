using NutriForge.Application.Abstractions;

namespace NutriForge.Api.Endpoints;

internal static class EndpointHelpers
{
    /// <summary>The authenticated local user id. Endpoints behind <c>OwnerOnly</c> always have one.</summary>
    public static Guid CurrentUserId(this ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.UserId ?? throw new InvalidOperationException("No authenticated user on the request.");
    }
}
