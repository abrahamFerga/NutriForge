using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NutriForge.IntegrationTests;

/// <summary>
/// Security hardening for the anonymous Twilio inbound webhook (#97): the <c>X-Twilio-Signature</c> is
/// the endpoint's only authentication, because <c>From</c> is attacker-controlled in a forged POST.
/// Proves a missing/invalid/tampered signature is rejected with 403 before any field is trusted, that a
/// validly-signed request is processed, and that the existing unknown-sender / duplicate-MessageSid
/// behaviour still holds. The AppHost arms the validator under test with a deterministic auth token + a
/// fixed public URL (see the <c>underTest</c> branch in AppHost.cs) so signatures are computable
/// independently of the dynamic Aspire port.
/// </summary>
[Collection("AppHost")]
public sealed class WhatsAppInboundSecurityTests(AppHostFixture fixture)
{
    private const string Endpoint = "/api/v1/channels/whatsapp/inbound";

    // MUST match the AppHost `underTest` branch.
    private const string AuthToken = "itest-twilio-token";
    private const string SignedUrl = "https://nutriforge.test/api/v1/channels/whatsapp/inbound";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Inbound_without_a_signature_is_rejected_403()
    {
        await fixture.CreateReadyClientAsync(); // block until the API is serving
        var anon = fixture.App.CreateHttpClient("api");

        var fields = NewMessage("whatsapp:+15550000001", "2 eggs");
        using var res = await PostInbound(anon, signature: null, fields);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Inbound_with_an_invalid_signature_is_rejected_403()
    {
        await fixture.CreateReadyClientAsync();
        var anon = fixture.App.CreateHttpClient("api");

        var fields = NewMessage("whatsapp:+15550000002", "2 eggs");
        // A well-formed but wrong signature (28 base64 chars) — exercises the equal-length compare path too.
        using var res = await PostInbound(anon, "AAAAAAAAAAAAAAAAAAAAAAAAAAA=", fields);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_valid_signature_for_a_different_body_does_not_authenticate_a_tampered_request()
    {
        await fixture.CreateReadyClientAsync();
        var anon = fixture.App.CreateHttpClient("api");

        var original = NewMessage("whatsapp:+15550000003", "2 eggs");
        var signature = Sign(original);

        // Reuse the signature but swap the Body — the HMAC no longer covers the payload ⇒ 403.
        var tampered = original.Select(f => f.Key == "Body" ? (f.Key, Value: "1 cake") : f).ToArray();
        using var res = await PostInbound(anon, signature, tampered);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_validly_signed_request_from_an_unknown_sender_returns_empty_twiml()
    {
        await fixture.CreateReadyClientAsync();
        var anon = fixture.App.CreateHttpClient("api");

        // No subscription links this number → the handler returns null → empty <Response/> (200, no reply).
        var fields = NewMessage("whatsapp:+15559990000", "2 eggs");
        using var res = await PostInbound(anon, Sign(fields), fields);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<Message>", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<Response", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_validly_signed_known_sender_is_processed_then_a_duplicate_message_sid_is_deduped()
    {
        var phone = "whatsapp:+15557654321";
        var client = await fixture.CreateReadyClientAsync(subject: $"wa-{Guid.NewGuid():N}");
        await LinkWhatsAppAddressAsync(client, phone);

        var anon = fixture.App.CreateHttpClient("api");
        var fields = NewMessage(phone, "2 eggs and toast"); // a fixed MessageSid so the second post is a duplicate
        var signature = Sign(fields);

        // First delivery: a valid signature from a known sender ⇒ processed. The assistant is unconfigured
        // under test, so the reply is the "AI is not configured" message — but it IS a <Message>, which only
        // happens when the signed request was accepted and handled (an unknown sender would get no reply).
        using (var res = await PostInbound(anon, signature, fields))
        {
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            Assert.Contains("<Message>", body, StringComparison.OrdinalIgnoreCase);
        }

        // Re-delivery of the SAME MessageSid (still validly signed) ⇒ deduped ⇒ empty <Response/>.
        using (var res = await PostInbound(anon, signature, fields))
        {
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("<Message>", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Subscribe + mint a one-time code + redeem it, binding <paramref name="address"/> to this user.</summary>
    private static async Task LinkWhatsAppAddressAsync(HttpClient client, string address)
    {
        using (var sub = await client.PutAsJsonAsync("/api/v1/notifications/subscription",
            new { channel = "twilio-whatsapp", enabled = true, sendHourUtc = 9 }, Json))
        {
            Assert.Equal(HttpStatusCode.OK, sub.StatusCode);
        }

        string code;
        using (var link = await client.PostAsJsonAsync("/api/v1/notifications/link", new { channel = "twilio-whatsapp" }, Json))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
            code = (await link.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString()!;
        }

        using var redeem = await client.PostAsJsonAsync("/api/v1/notifications/link/redeem",
            new { code, address }, Json);
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        Assert.True((await redeem.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("linked").GetBoolean());
    }

    private static (string Key, string Value)[] NewMessage(string from, string body) =>
    [
        ("From", from),
        ("Body", body),
        ("MessageSid", "SM" + Guid.NewGuid().ToString("N")),
    ];

    private static async Task<HttpResponseMessage> PostInbound(
        HttpClient client, string? signature, (string Key, string Value)[] fields)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent(
                fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))),
        };
        if (signature is not null)
        {
            req.Headers.Add("X-Twilio-Signature", signature);
        }

        return await client.SendAsync(req);
    }

    /// <summary>Compute the Twilio signature exactly as the server does: HMAC-SHA1 over url + ordinal-sorted key+value.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Twilio's webhook protocol mandates HMAC-SHA1; the test must produce a real Twilio signature.")]
    private static string Sign((string Key, string Value)[] fields)
    {
        var builder = new StringBuilder(SignedUrl);
        foreach (var (key, value) in fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            builder.Append(key).Append(value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(AuthToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
