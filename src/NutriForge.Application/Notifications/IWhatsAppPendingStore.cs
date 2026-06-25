namespace NutriForge.Application.Notifications;

/// <summary>
/// Short-lived Redis store for two-way WhatsApp conversation state: parsed food candidates
/// awaiting YES/NO confirmation, and dedup guards for inbound MessageSids.
/// </summary>
public interface IWhatsAppPendingStore
{
    /// <summary>True if this MessageSid has already been processed (dedup guard).</summary>
    Task<bool> IsSeenAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Mark a MessageSid as seen (24-h TTL).</summary>
    Task MarkSeenAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Return the pending confirmation for a sender, or null if none.</summary>
    Task<WhatsAppPending?> GetPendingAsync(string from, CancellationToken ct = default);

    /// <summary>Store a pending confirmation (15-min TTL).</summary>
    Task SetPendingAsync(string from, WhatsAppPending pending, CancellationToken ct = default);

    /// <summary>Remove any pending confirmation for a sender.</summary>
    Task ClearPendingAsync(string from, CancellationToken ct = default);
}
