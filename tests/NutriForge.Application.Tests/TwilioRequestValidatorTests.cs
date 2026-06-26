using NutriForge.Application.Notifications;
using NutriForge.Infrastructure.Notifications;

namespace NutriForge.Application.Tests;

/// <summary>
/// The inbound WhatsApp webhook is anonymous, so the Twilio <c>X-Twilio-Signature</c> is its only
/// authentication (#97 hardening). These pin the HMAC algorithm to Twilio's own documented test
/// vector — an independent reference, so a self-consistent-but-wrong implementation can't pass — and
/// assert the security-critical fail-closed behaviour.
/// </summary>
public sealed class TwilioRequestValidatorTests
{
    // Twilio's published validation example (twilio-csharp RequestValidatorTest):
    // auth token "12345", this URL + these POST params ⇒ this exact signature.
    private const string AuthToken = "12345";
    private const string Url = "https://mycompany.com/myapp.php?foo=1&bar=2";
    private const string ExpectedSignature = "RSOYDt4T1cUTdK1PDd93/VVr8B8=";

    private static Dictionary<string, string> DocumentedParameters() => new()
    {
        ["CallSid"] = "CA1234567890ABCDE",
        ["Caller"] = "+14158675309",
        ["Digits"] = "1234",
        ["From"] = "+14158675309",
        ["To"] = "+18005551212",
    };

    [Fact]
    public void Valid_signature_for_the_documented_twilio_vector_is_accepted()
    {
        var validator = new TwilioRequestValidator(AuthToken);

        Assert.True(validator.IsValid(Url, DocumentedParameters(), ExpectedSignature));
    }

    [Fact]
    public void Parameter_order_does_not_matter()
    {
        var validator = new TwilioRequestValidator(AuthToken);

        // Same params, reversed insertion order — the validator sorts internally, so it still matches.
        var reordered = DocumentedParameters().Reverse().ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.True(validator.IsValid(Url, reordered, ExpectedSignature));
    }

    [Fact]
    public void A_tampered_signature_is_rejected()
    {
        var validator = new TwilioRequestValidator(AuthToken);

        // Flip the first character of the otherwise-valid signature.
        var tampered = (ExpectedSignature[0] == 'X' ? 'Y' : 'X') + ExpectedSignature[1..];

        Assert.False(validator.IsValid(Url, DocumentedParameters(), tampered));
    }

    [Fact]
    public void A_tampered_parameter_is_rejected()
    {
        var validator = new TwilioRequestValidator(AuthToken);

        var forged = DocumentedParameters();
        forged["From"] = "+19998887777"; // a spoofed sender breaks the HMAC

        Assert.False(validator.IsValid(Url, forged, ExpectedSignature));
    }

    [Fact]
    public void A_different_url_is_rejected()
    {
        var validator = new TwilioRequestValidator(AuthToken);

        Assert.False(validator.IsValid(
            "https://attacker.example/api/v1/channels/whatsapp/inbound", DocumentedParameters(), ExpectedSignature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_signature_is_rejected(string? signature)
    {
        var validator = new TwilioRequestValidator(AuthToken);

        Assert.False(validator.IsValid(Url, DocumentedParameters(), signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void With_no_auth_token_configured_the_validator_fails_closed(string? authToken)
    {
        // The MockChannel fallback has no token: every request — even one carrying the otherwise-valid
        // signature — must be rejected, so a disabled/misconfigured channel never trusts a spoofed sender.
        var validator = new TwilioRequestValidator(authToken);

        Assert.False(validator.IsValid(Url, DocumentedParameters(), ExpectedSignature));
    }
}
