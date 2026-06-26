namespace NutriForge.Application.Notifications;

/// <summary>
/// Verifies that an inbound WhatsApp webhook genuinely originated from Twilio by checking the
/// <c>X-Twilio-Signature</c> HMAC over the request URL + POST parameters
/// (see https://www.twilio.com/docs/usage/webhooks/webhooks-security).
/// <para>
/// The inbound endpoint is <c>AllowAnonymous</c> — it sits outside the OIDC stack — so this
/// signature IS its authentication. A request that fails validation must be rejected: the
/// <c>From</c> field is attacker-controlled in a forged POST, so without this check anyone who
/// learns the URL could log food to a linked user or probe which numbers are linked.
/// </para>
/// </summary>
public interface IWhatsAppRequestValidator
{
    /// <summary>
    /// True when <paramref name="signature"/> is a valid Twilio signature for the given public
    /// <paramref name="url"/> and form <paramref name="parameters"/>. <b>Fails closed:</b> returns
    /// false when no auth token is configured, or when the signature is missing or does not match.
    /// </summary>
    /// <param name="url">The exact public URL Twilio signed (the one configured in the Twilio console).</param>
    /// <param name="parameters">The inbound form fields (POST body), in any order.</param>
    /// <param name="signature">The value of the <c>X-Twilio-Signature</c> header, or null when absent.</param>
    bool IsValid(string url, IEnumerable<KeyValuePair<string, string>> parameters, string? signature);
}
