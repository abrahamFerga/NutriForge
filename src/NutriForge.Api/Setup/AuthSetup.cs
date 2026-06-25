using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using NutriForge.Api.Auth;
using NutriForge.Application.Authorization;

namespace NutriForge.Api.Setup;

/// <summary>
/// Wires authentication (OIDC/JWT in non-dev; a local dev scheme otherwise) and the RBAC policies.
/// Code references the policy names in <see cref="Policies"/>, never role strings directly.
/// </summary>
public static class AuthSetup
{
    public static IServiceCollection AddNutriForgeAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(env);

        var authority = config["Authentication:Authority"];
        var audience = config["Authentication:Audience"];
        var useJwt = !string.IsNullOrWhiteSpace(authority);

        // Fail closed (#56): the header-based dev scheme trusts X-Debug-Subject / X-Debug-Role verbatim,
        // so anyone could claim any identity or the admin role. It is therefore allowed ONLY in
        // Development. A deployed API must validate real OIDC tokens — if no Authority is configured
        // outside Development we refuse to start rather than silently expose an auth bypass.
        if (!env.IsDevelopment() && !useJwt)
        {
            throw new InvalidOperationException(
                "Authentication:Authority must be configured outside Development. The dev header auth " +
                "scheme is disabled in deployed environments to prevent an identity/role bypass.");
        }

        var auth = services.AddAuthentication(options =>
        {
            var scheme = useJwt ? JwtBearerDefaults.AuthenticationScheme : DevAuthenticationHandler.SchemeName;
            options.DefaultAuthenticateScheme = scheme;
            options.DefaultChallengeScheme = scheme;
        });

        if (useJwt)
        {
            // Entra External ID (CIAM) issues the subject in "sub" and app-role assignments in "roles".
            // Map those so HttpCurrentUser.Subject and the RequireRole(admin) policy work unchanged. The
            // role-claim type is overridable for other IdPs via Authentication:RoleClaim.
            var roleClaim = config["Authentication:RoleClaim"] ?? "roles";
            auth.AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.MapInboundClaims = false; // keep raw "sub"/"roles" claim types
                options.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(audience);
                options.TokenValidationParameters.NameClaimType = "sub";
                options.TokenValidationParameters.RoleClaimType = roleClaim;
            });
        }
        else
        {
            // Local development only — never stand this up in a deployed environment.
            auth.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthenticationHandler>(
                DevAuthenticationHandler.SchemeName, _ => { });
        }

        // Policies are applied explicitly per endpoint group (no global fallback, so the
        // anonymous health/OpenAPI endpoints stay reachable).
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.OwnerOnly, p => p.RequireAuthenticatedUser())
            .AddPolicy(Policies.AdminOnly, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin));

        return services;
    }
}
