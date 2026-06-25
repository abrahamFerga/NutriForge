using NutriForge.Domain.Common;

namespace NutriForge.Domain.Notifications;

/// <summary>
/// A single-use, short-lived secret that links a channel address (e.g. a Telegram chat) to a user's
/// account. The plaintext code is shown to the user once and NEVER stored — only its
/// <see cref="TokenHash"/> (SHA-256) is persisted, so a leaked database can't be used to link
/// accounts. Expires (<see cref="ExpiresAt"/>) and is consumed exactly once (<see cref="ConsumedAt"/>).
/// </summary>
public sealed class AccountLinkToken : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public string Channel { get; set; } = "telegram";

    /// <summary>SHA-256 (hex) of the plaintext code. The code itself is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set the moment the code is redeemed; a non-null value means it can never be used again.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsRedeemable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;
}
