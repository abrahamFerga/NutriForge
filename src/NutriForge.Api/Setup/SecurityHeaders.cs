namespace NutriForge.Api.Setup;

/// <summary>
/// Adds defense-in-depth security response headers to every API response (#57). This is a JSON API, so
/// the CSP is maximally restrictive (<c>default-src 'none'</c>) — there are no sub-resources to load —
/// and framing is denied. HSTS is added separately (HTTPS, non-dev only) by the host.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            // Don't advertise the stack.
            headers.Remove("X-Powered-By");

            await next().ConfigureAwait(false);
        });
    }
}
