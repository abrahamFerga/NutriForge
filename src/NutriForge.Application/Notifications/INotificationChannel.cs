namespace NutriForge.Application.Notifications;

/// <summary>
/// A messaging channel that can deliver a short text to a user (Telegram, etc.). Swappable behind a
/// config setting; the <see cref="MockChannel"/> records to an outbox for tests/dev. <see
/// cref="SendAsync"/> never throws — it degrades to <c>false</c> on any provider fault, so a channel
/// outage never fails the daily job or an on-demand send.
/// </summary>
public interface INotificationChannel
{
    /// <summary>The channel id, e.g. <c>"mock"</c> or <c>"telegram"</c>.</summary>
    string Name { get; }

    /// <summary>False ⇒ the channel can't send (e.g. no token); callers degrade gracefully.</summary>
    bool IsConfigured { get; }

    Task<bool> SendAsync(Guid userId, string text, CancellationToken ct = default);
}
