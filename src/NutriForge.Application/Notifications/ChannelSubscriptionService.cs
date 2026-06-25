using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Notifications;

namespace NutriForge.Application.Notifications;

public sealed record ChannelSubscriptionDto(string Channel, bool Enabled, int SendHourUtc, bool IsLinked, string? Address,
    bool WeeklySummaryEnabled = false, int? ReminderHourUtc = null);
public sealed record UpdateChannelSubscriptionRequest(string Channel, bool Enabled, int SendHourUtc,
    bool WeeklySummaryEnabled = false, int? ReminderHourUtc = null);
public sealed record LinkCodeDto(string Code, DateTimeOffset ExpiresAt);
public sealed record RedeemLinkRequest(string Code, string Address);
public sealed record RedeemResultDto(bool Linked, string Channel);

/// <summary>
/// Owns the user's notification opt-in (<see cref="ChannelSubscription"/>) and the secure account-link
/// flow: mint a single-use, short-lived code (only its SHA-256 hash is stored), then redeem it to bind
/// a channel address. The code's plaintext is returned exactly once and never persisted. Owner-scoped.
/// </summary>
public sealed class ChannelSubscriptionService(IAppDbContext db, IClock clock)
{
    private static readonly string[] AllowedChannels = ["mock", "telegram", "twilio-whatsapp"];
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(15);

    public async Task<ChannelSubscriptionDto> GetAsync(Guid userId, string channel, CancellationToken ct = default)
    {
        var ch = Normalize(channel);
        var sub = await db.ChannelSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Channel == ch, ct).ConfigureAwait(false);
        return ToDto(sub, ch);
    }

    public async Task<ChannelSubscriptionDto> UpsertAsync(Guid userId, UpdateChannelSubscriptionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var ch = Normalize(req.Channel);
        var sub = await db.ChannelSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Channel == ch, ct).ConfigureAwait(false);
        if (sub is null)
        {
            sub = new ChannelSubscription { UserId = userId, Channel = ch };
            db.ChannelSubscriptions.Add(sub);
        }

        sub.Enabled = req.Enabled;
        sub.SendHourUtc = Math.Clamp(req.SendHourUtc, 0, 23);
        sub.WeeklySummaryEnabled = req.WeeklySummaryEnabled;
        sub.ReminderHourUtc = req.ReminderHourUtc is { } h ? Math.Clamp(h, 0, 23) : null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(sub, ch);
    }

    /// <summary>Mint a one-time link code (TTL 15 min). Only its hash is stored; the plaintext is returned once.</summary>
    public async Task<LinkCodeDto> CreateLinkCodeAsync(Guid userId, string channel, CancellationToken ct = default)
    {
        var ch = Normalize(channel);

        // Invalidate any prior un-redeemed codes for this user+channel so only the newest is live.
        var stale = await db.AccountLinkTokens
            .Where(t => t.UserId == userId && t.Channel == ch && t.ConsumedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        db.AccountLinkTokens.RemoveRange(stale);

        var code = NewCode();
        var expires = clock.UtcNow + CodeTtl;
        db.AccountLinkTokens.Add(new AccountLinkToken
        {
            UserId = userId,
            Channel = ch,
            TokenHash = Hash(code),
            ExpiresAt = expires,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LinkCodeDto(code, expires);
    }

    /// <summary>
    /// Redeem a link code: bind <paramref name="req"/>.Address to the matching subscription and enable it.
    /// Validates the hash, the TTL, and single-use atomically; returns Linked=false on any failure (no
    /// information leak about why). The code is consumed so it can never be reused.
    /// </summary>
    public async Task<RedeemResultDto> RedeemAsync(Guid userId, RedeemLinkRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Address))
        {
            return new RedeemResultDto(false, "");
        }

        var now = clock.UtcNow;
        var hash = Hash(req.Code.Trim());
        var token = await db.AccountLinkTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.TokenHash == hash, ct).ConfigureAwait(false);
        if (token is null || !token.IsRedeemable(now))
        {
            return new RedeemResultDto(false, token?.Channel ?? "");
        }

        var ch = token.Channel;
        var sub = await db.ChannelSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Channel == ch, ct).ConfigureAwait(false);
        if (sub is null)
        {
            sub = new ChannelSubscription { UserId = userId, Channel = ch };
            db.ChannelSubscriptions.Add(sub);
        }

        sub.Address = req.Address.Trim();
        sub.Enabled = true;
        token.ConsumedAt = now; // single-use
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RedeemResultDto(true, ch);
    }

    private static string Normalize(string? channel)
    {
        var c = (channel ?? "").Trim().ToLowerInvariant();
        return AllowedChannels.Contains(c) ? c : "telegram";
    }

    /// <summary>A URL-safe, ~12-char random code from a CSPRNG.</summary>
    private static string NewCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(9);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    private static ChannelSubscriptionDto ToDto(ChannelSubscription? sub, string channel) =>
        sub is null
            ? new ChannelSubscriptionDto(channel, false, 8, false, null)
            : new ChannelSubscriptionDto(sub.Channel, sub.Enabled, sub.SendHourUtc, sub.IsLinked, sub.Address,
                sub.WeeklySummaryEnabled, sub.ReminderHourUtc);
}
