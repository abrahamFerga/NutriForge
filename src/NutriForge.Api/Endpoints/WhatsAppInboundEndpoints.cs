using System.Text;
using NutriForge.Application.Notifications;
using NutriForge.Infrastructure.Notifications;

namespace NutriForge.Api.Endpoints;

/// <summary>
/// Twilio inbound webhook for two-way WhatsApp logging (#97).
/// Anonymous — it sits outside the OIDC stack — so the request is authenticated by Twilio's
/// <c>X-Twilio-Signature</c> HMAC, NOT by the <c>From</c> field (which is attacker-controlled in a
/// forged POST). Requests that fail signature validation are rejected with 403 before any field is
/// trusted. Returns TwiML XML so Twilio delivers the reply directly to the sender's WhatsApp.
/// </summary>
public static class WhatsAppInboundEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppInboundEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Twilio calls this with form-encoded fields: From, Body, MessageSid (+ many others we ignore).
        // Must return 200 with TwiML; any non-200 causes Twilio to retry.
        app.MapPost("/api/v1/channels/whatsapp/inbound",
            async (HttpRequest request, WhatsAppInboundService svc, IWhatsAppRequestValidator validator,
                   IConfiguration config, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);

                // Authenticate Twilio FIRST. The signature is computed over the exact public URL Twilio
                // signed (the operator-pinned override wins; otherwise reconstruct from the request,
                // honouring proxy X-Forwarded-* headers) plus every POST field. A forged request can't
                // produce a valid HMAC without the auth token, so reject with an empty 403 — no body, so
                // a forger can't tell a linked sender from an unknown one (no differential oracle).
                var url = ResolvePublicUrl(request, config);
                var parameters = form.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value.ToString()));
                var signature = request.Headers["X-Twilio-Signature"].ToString();
                if (!validator.IsValid(url, parameters, signature))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                var from = form["From"].ToString();
                var body = form["Body"].ToString();
                var messageSid = form["MessageSid"].ToString();

                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(messageSid))
                {
                    return Results.BadRequest();
                }

                var reply = await svc.HandleAsync(from, body ?? "", messageSid, ct);

                // Unknown sender or duplicate: return empty TwiML (no reply).
                var twiml = reply is null
                    ? "<Response/>"
                    : TwiML(reply);

                return Results.Content(twiml, "text/xml; charset=utf-8", Encoding.UTF8);
            })
            .AllowAnonymous()
            .WithTags("WhatsApp")
            .WithName("WhatsAppInbound")
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// The public URL Twilio signed. Prefers the operator-pinned <see cref="TwilioOptions.InboundWebhookUrl"/>
    /// (the robust choice behind a proxy/load balancer); otherwise reconstructs it from the request,
    /// preferring the proxy-forwarded scheme/host so the signature base string matches what Twilio used.
    /// </summary>
    private static string ResolvePublicUrl(HttpRequest request, IConfiguration config)
    {
        var configured = config[$"{TwilioOptions.SectionName}:InboundWebhookUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var scheme = Forwarded(request, "X-Forwarded-Proto") ?? request.Scheme;
        var host = Forwarded(request, "X-Forwarded-Host") ?? request.Host.Value;
        return $"{scheme}://{host}{request.PathBase}{request.Path}{request.QueryString}";

        static string? Forwarded(HttpRequest request, string header) =>
            request.Headers.TryGetValue(header, out var values) && values.Count > 0 && !string.IsNullOrWhiteSpace(values[0])
                ? values[0]!.Split(',')[0].Trim()
                : null;
    }

    private static string TwiML(string message)
    {
        // Minimal TwiML; no XML encoding beyond what Twilio requires.
        var escaped = System.Security.SecurityElement.Escape(message) ?? message;
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response><Message>{escaped}</Message></Response>
                """;
    }
}
