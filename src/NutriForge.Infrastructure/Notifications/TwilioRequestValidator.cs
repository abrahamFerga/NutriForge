using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using NutriForge.Application.Notifications;

namespace NutriForge.Infrastructure.Notifications;

/// <summary>
/// Validates the Twilio <c>X-Twilio-Signature</c>: <c>Base64(HMAC-SHA1(authToken, url + sorted(key+value)))</c>,
/// where the POST parameters are concatenated as <c>key</c>+<c>value</c> after ordinal-sorting by key
/// (https://www.twilio.com/docs/usage/webhooks/webhooks-security).
/// <para>
/// Implements the documented algorithm directly rather than referencing the full Twilio SDK — the
/// outbound channel (<see cref="TwilioWhatsAppChannel"/>) likewise speaks Twilio over raw HTTP. The
/// comparison is constant-time, and the validator <b>fails closed</b>: with no auth token configured
/// (e.g. the MockChannel fallback) no signature can ever validate, so the endpoint rejects everything.
/// </para>
/// </summary>
public sealed class TwilioRequestValidator(string? authToken) : IWhatsAppRequestValidator
{
    public bool IsValid(string url, IEnumerable<KeyValuePair<string, string>> parameters, string? signature)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(parameters);

        // Fail closed: an unconfigured channel (no token) or an unsigned request is never trusted.
        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(ComputeSignature(authToken, url, parameters));
        var provided = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Twilio's webhook protocol mandates HMAC-SHA1; this validates Twilio's own " +
                        "signature, so the algorithm is fixed by the wire format, not a free choice. " +
                        "HMAC-SHA1 does not rely on SHA-1 collision resistance and remains acceptable for MAC use.")]
    private static string ComputeSignature(
        string authToken, string url, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var builder = new StringBuilder(url);
        foreach (var kvp in parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(kvp.Key).Append(kvp.Value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToBase64String(hash);
    }
}
