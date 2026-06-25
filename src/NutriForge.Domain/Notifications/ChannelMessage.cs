using NutriForge.Domain.Common;

namespace NutriForge.Domain.Notifications;

/// <summary>
/// An outbound notification message — the MockChannel's sink and a general send audit. Carries
/// <see cref="UserId"/> so the per-user query filter and OwnerOnly RBAC apply unchanged.
/// </summary>
public sealed class ChannelMessage : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>The channel that delivered (or recorded) it — e.g. <c>"mock"</c>, <c>"telegram"</c>.</summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>The channel address it was sent to (e.g. a Telegram chat id), when known.</summary>
    public string? ChatId { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
