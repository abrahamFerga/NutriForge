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

    /// <summary>True when the minimum credentials are set.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
}
