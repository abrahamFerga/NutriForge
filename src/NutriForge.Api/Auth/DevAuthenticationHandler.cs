using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NutriForge.Application.Authorization;

namespace NutriForge.Api.Auth;

/// <summary>
/// A local-only authentication scheme so the system runs without a live OIDC tenant. Authenticates
/// every request as a stable dev user; the <c>X-Debug-Subject</c> and <c>X-Debug-Role</c> headers
/// let you switch identity/role (e.g. to exercise the <c>admin</c> policies) during manual testing.
/// Never registered outside Development.
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No subject header ⇒ treat the request as anonymous (mirrors a real bearer-less request),
        // so protected endpoints still challenge with 401. Present ⇒ authenticate as that subject.
        var subject = Request.Headers["X-Debug-Subject"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var role = Request.Headers["X-Debug-Role"].FirstOrDefault() ?? Roles.User;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim("sub", subject),
            new Claim(ClaimTypes.Email, $"{subject}@dev.local"),
            new Claim(ClaimTypes.Role, role),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
