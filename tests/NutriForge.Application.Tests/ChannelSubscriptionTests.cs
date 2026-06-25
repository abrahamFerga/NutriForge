using System.Linq;
using NutriForge.Application.Notifications;

namespace NutriForge.Application.Tests;

/// <summary>
/// Notification opt-in + the secure account-link flow (#95): subscription upsert, and a one-time code
/// that is hashed at rest, single-use, and TTL-bounded.
/// </summary>
public sealed class ChannelSubscriptionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_creates_then_updates_in_place_and_clamps_the_hour()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new ChannelSubscriptionService(db, new FakeClock(T0));

        var created = await svc.UpsertAsync(userId, new UpdateChannelSubscriptionRequest("telegram", true, 30));
        Assert.True(created.Enabled);
        Assert.Equal(23, created.SendHourUtc); // clamped from 30
        Assert.False(created.IsLinked);

        var updated = await svc.UpsertAsync(userId, new UpdateChannelSubscriptionRequest("telegram", false, 7));
        Assert.False(updated.Enabled);
        Assert.Equal(7, updated.SendHourUtc);

        Assert.Equal(1, db.ChannelSubscriptions.Count()); // upsert, not a second row
    }

    [Fact]
    public async Task Link_code_is_hashed_at_rest_single_use_and_binds_the_address()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new ChannelSubscriptionService(db, new FakeClock(T0));

        var link = await svc.CreateLinkCodeAsync(userId, "telegram");
        Assert.False(string.IsNullOrWhiteSpace(link.Code));

        // The plaintext code is never stored — only a 64-char SHA-256 hex.
        var stored = db.AccountLinkTokens.Single();
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.NotEqual(link.Code, stored.TokenHash);
        Assert.DoesNotContain(link.Code, stored.TokenHash);

        // Redeem binds the address + enables the subscription.
        var ok = await svc.RedeemAsync(userId, new RedeemLinkRequest(link.Code, "chat-123"));
        Assert.True(ok.Linked);
        var sub = db.ChannelSubscriptions.Single();
        Assert.Equal("chat-123", sub.Address);
        Assert.True(sub.Enabled);
        Assert.True(sub.IsLinked);

        // Single-use: the same code can't be redeemed again.
        var again = await svc.RedeemAsync(userId, new RedeemLinkRequest(link.Code, "chat-999"));
        Assert.False(again.Linked);
        Assert.Equal("chat-123", db.ChannelSubscriptions.Single().Address); // unchanged
    }

    [Fact]
    public async Task An_expired_code_cannot_be_redeemed()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var link = await new ChannelSubscriptionService(db, new FakeClock(T0)).CreateLinkCodeAsync(userId, "telegram");

        // 16 minutes later — past the 15-minute TTL.
        var late = new ChannelSubscriptionService(db, new FakeClock(T0.AddMinutes(16)));
        var result = await late.RedeemAsync(userId, new RedeemLinkRequest(link.Code, "chat-123"));

        Assert.False(result.Linked);
    }

    [Fact]
    public async Task A_wrong_code_does_not_link()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new ChannelSubscriptionService(db, new FakeClock(T0));
        await svc.CreateLinkCodeAsync(userId, "telegram");

        var result = await svc.RedeemAsync(userId, new RedeemLinkRequest("not-the-code", "chat-123"));

        Assert.False(result.Linked);
        Assert.Empty(db.ChannelSubscriptions.Where(s => s.Address != null));
    }

    [Fact]
    public async Task Creating_a_new_code_invalidates_the_previous_one()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new ChannelSubscriptionService(db, new FakeClock(T0));

        var first = await svc.CreateLinkCodeAsync(userId, "telegram");
        await svc.CreateLinkCodeAsync(userId, "telegram"); // supersedes the first

        var result = await svc.RedeemAsync(userId, new RedeemLinkRequest(first.Code, "chat-123"));
        Assert.False(result.Linked); // the old code no longer exists
        Assert.Single(db.AccountLinkTokens); // only the newest survives
    }
}
