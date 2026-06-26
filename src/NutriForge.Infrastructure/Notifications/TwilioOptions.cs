namespace NutriForge.Infrastructure.Notifications;

/// <summary>
/// Configuration for the Twilio WhatsApp notification channel (section <c>Channels:WhatsApp:Twilio</c>).
/// When unconfigured the application falls back to the <c>MockChannel</c>.
/// </summary>
public sealed class TwilioOptions
{
    public const string SectionName = "Channels:WhatsApp:Twilio";

    /// <summary>Twilio Account SID (starts with "AC"). Required to enable the channel.</summary>
    public string? AccountSid { get; set; }

    /// <summary>Twilio Auth Token. Required to enable the channel.</summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// The "From" WhatsApp number in E.164 format including the <c>whatsapp:</c> prefix,
    /// e.g. <c>whatsapp:+14155238886</c> (Twilio sandbox) or your approved sender number.
    /// </summary>
    public string FromNumber { get; set; } = "whatsapp:+14155238886";

    /// <summary>
    /// The exact public URL Twilio is configured to call for the inbound webhook, used to verify the
    /// <c>X-Twilio-Signature</c> (Twilio signs the URL byte-for-byte as registered in its console).
    /// Set this when the API runs behind a proxy/load balancer, where the request the API receives
    /// carries the internal host/scheme rather than the public one Twilio signed. When empty, the
    /// signature is verified against the URL reconstructed from the request (honouring
    /// <c>X-Forwarded-Proto</c>/<c>X-Forwarded-Host</c>).
    /// </summary>
    public string? InboundWebhookUrl { get; set; }

    /// <summary>True when the minimum credentials are set.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
}
