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

        var auth = services.AddAuthentication(options =>
        {
            var scheme = useJwt ? JwtBearerDefaults.AuthenticationScheme : DevAuthenticationHandler.SchemeName;
            options.DefaultAuthenticateScheme = scheme;
            options.DefaultChallengeScheme = scheme;
        });

        if (useJwt)
        {
            auth.AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(audience);
                options.MapInboundClaims = false; // keep the raw "sub"/"role" claim types
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
