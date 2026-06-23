using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NutriForge.Domain.Users;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Api.Auth;

/// <summary>
/// Just-in-time mirrors the authenticated OIDC subject into a local <see cref="User"/> row and
/// stashes its id on <c>HttpContext.Items</c> so the per-user query filter and owner-scoped
/// handlers have a stable local id. Runs after authentication, before the endpoints.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, NutriForgeDbContext db)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);

        var principal = context.User;
        if (principal.Identity?.IsAuthenticated == true)
        {
            var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(subject))
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.OidcSubject == subject, context.RequestAborted)
                    .ConfigureAwait(false);
                if (user is null)
                {
                    user = User.FromSubject(subject, principal.FindFirstValue(ClaimTypes.Email), null);
                    db.Users.Add(user);
                    await db.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
                }

                context.Items[HttpCurrentUser.LocalUserIdItem] = user.Id;
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
