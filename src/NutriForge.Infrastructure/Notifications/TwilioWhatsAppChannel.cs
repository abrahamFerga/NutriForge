using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Notifications;

namespace NutriForge.Infrastructure.Notifications;

/// <summary>
/// Sends WhatsApp messages via the Twilio Messages REST API. Registered in place of <see cref="MockChannel"/>
/// when <c>Channels:WhatsApp:Provider = twilio</c> and Twilio credentials are present.
/// <see cref="SendAsync"/> never throws — faults degrade to <c>false</c> so a Twilio outage
/// never fails the notification workers.
/// </summary>
public sealed partial class TwilioWhatsAppChannel(
    TwilioOptions options,
    HttpClient httpClient,
    IAppDbContext db,
    ILogger<TwilioWhatsAppChannel> logger) : INotificationChannel
{
    public string Name => "twilio-whatsapp";

    public bool IsConfigured => options.IsConfigured;

    public async Task<bool> SendAsync(Guid userId, string text, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var address = await ResolveAddressAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        // Normalise: the Twilio To param must carry the "whatsapp:" prefix.
        var to = address.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? address
            : $"whatsapp:{address}";

        return await PostMessageAsync(to, text, ct).ConfigureAwait(false);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<string?> ResolveAddressAsync(Guid userId, CancellationToken ct)
    {
        var sub = await db.ChannelSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Channel == Name && s.Enabled, ct)
            .ConfigureAwait(false);
        return sub?.Address;
    }

    private async Task<bool> PostMessageAsync(string to, string text, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{options.AccountSid}/Messages.json";
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);

            var body = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("From", options.FromNumber),
                new KeyValuePair<string, string>("To", to),
                new KeyValuePair<string, string>("Body", text),
            ]);

            var response = await httpClient.PostAsync(url, body, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            LogTwilioError(logger, (int)response.StatusCode, to, detail);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTwilioException(logger, to, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twilio returned {Status} for To={To}: {Detail}")]
    private static partial void LogTwilioError(ILogger logger, int status, string to, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Twilio send failed for To={To}")]
    private static partial void LogTwilioException(ILogger logger, string to, Exception ex);
}
