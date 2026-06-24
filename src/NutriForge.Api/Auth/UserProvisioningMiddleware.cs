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
                    try
                    {
                        await db.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
                    }
                    catch (DbUpdateException)
                    {
                        // A concurrent first request provisioned the same subject first (the browser
                        // fires several API calls in parallel on load), losing the race on the unique
                        // OidcSubject constraint. Abandon our insert and adopt the winner's row. If the
                        // row still isn't there, this wasn't the provisioning race — surface the failure.
                        db.Entry(user).State = EntityState.Detached;
                        user = await db.Users.AsNoTracking()
                            .FirstOrDefaultAsync(u => u.OidcSubject == subject, context.RequestAborted)
                            .ConfigureAwait(false);
                        if (user is null)
                        {
                            throw;
                        }
                    }
                }

                context.Items[HttpCurrentUser.LocalUserIdItem] = user.Id;
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
