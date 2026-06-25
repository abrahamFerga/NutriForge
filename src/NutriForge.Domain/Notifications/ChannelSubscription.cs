using NutriForge.Domain.Common;

namespace NutriForge.Domain.Notifications;

/// <summary>
/// A user's opt-in to receive notifications (e.g. the daily calorie summary) on a channel. The
/// channel address (<see cref="Address"/>, e.g. a Telegram chat id) is set only via the secure
/// account-link flow (<see cref="AccountLinkToken"/>), never written directly by the client.
/// Owner-scoped + audited like the rest of the user's data.
/// </summary>
public sealed class ChannelSubscription : IUserOwned, IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>Channel id, e.g. <c>"mock"</c> or <c>"telegram"</c>.</summary>
    public string Channel { get; set; } = "telegram";

    /// <summary>Delivery address (e.g. Telegram chat id). Null until the account is linked.</summary>
    [Pii]
    public string? Address { get; set; }

    /// <summary>Master opt-in toggle. Even when enabled, nothing sends until the account is linked.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hour of day (UTC, 0–23) the scheduled daily summary should go out.</summary>
    public int SendHourUtc { get; set; } = 8;

    /// <summary>The last date a daily summary was sent, so the scheduled worker fires at most once a day.</summary>
    public DateOnly? LastSentOn { get; set; }

    /// <summary>Opt-in to receive a 7-day progress digest each Sunday.</summary>
    public bool WeeklySummaryEnabled { get; set; }

    /// <summary>Dedup guard: the last Sunday the weekly digest was sent for this subscription.</summary>
    public DateOnly? WeeklyLastSentOn { get; set; }

    /// <summary>
    /// UTC hour (0–23) to send a "time to log" nudge if the user hasn't logged anything today.
    /// Null means the log reminder is disabled.
    /// </summary>
    public int? ReminderHourUtc { get; set; }

    /// <summary>Dedup guard: the last date a log reminder was sent.</summary>
    public DateOnly? ReminderLastSentOn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True once an address is linked — a precondition for any real send.</summary>
    public bool IsLinked => !string.IsNullOrWhiteSpace(Address);
}
