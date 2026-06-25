using NutriForge.Application.Abstractions;
using NutriForge.Domain.Notifications;

namespace NutriForge.Application.Notifications;

/// <summary>
/// The default/test channel: instead of calling an external service it records each message to the
/// <c>channel_messages</c> outbox, so you can see exactly what would have been sent — and tests and
/// the Dashboard read it back. Always configured; no token, no network.
/// </summary>
public sealed class MockChannel(IAppDbContext db) : INotificationChannel
{
    public string Name => "mock";

    public bool IsConfigured => true;

    public async Task<bool> SendAsync(Guid userId, string text, CancellationToken ct = default)
    {
        db.ChannelMessages.Add(new ChannelMessage { UserId = userId, ChannelName = Name, Body = text });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
