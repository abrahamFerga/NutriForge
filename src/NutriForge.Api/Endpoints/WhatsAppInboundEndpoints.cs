using System.Text;
using NutriForge.Application.Notifications;

namespace NutriForge.Api.Endpoints;

/// <summary>
/// Twilio inbound webhook for two-way WhatsApp logging (#97).
/// Anonymous — authenticated by the Twilio sender address, not by our OIDC stack.
/// Returns TwiML XML so Twilio delivers the reply directly to the sender's WhatsApp.
/// </summary>
public static class WhatsAppInboundEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppInboundEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Twilio calls this with form-encoded fields: From, Body, MessageSid (+ many others we ignore).
        // Must return 200 with TwiML; any non-200 causes Twilio to retry.
        app.MapPost("/api/v1/channels/whatsapp/inbound",
            async (HttpRequest request, WhatsAppInboundService svc, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);
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
