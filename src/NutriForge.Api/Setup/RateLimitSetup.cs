using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NutriForge.Api.Auth;

namespace NutriForge.Api.Setup;

/// <summary>Named rate-limit policies (ARCH: tight on expensive paths, generous on reads).</summary>
public static class RateLimitPolicies
{
    public const string Read = "read";
    public const string Expensive = "expensive";
}

public static class RateLimitSetup
{
    public static IServiceCollection AddNutriForgeRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global per-user partition so one user can't starve others.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(http),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));

            options.AddPolicy(RateLimitPolicies.Read, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(http),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));

            // For LLM-backed paths (diet-plan generate, NL parse) landing in later epics.
            options.AddPolicy(RateLimitPolicies.Expensive, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(http),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    private static string PartitionKey(HttpContext http) =>
        http.Items.TryGetValue(HttpCurrentUser.LocalUserIdItem, out var v) && v is Guid id
            ? id.ToString()
            : http.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
}
