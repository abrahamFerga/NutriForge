using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Common;

namespace NutriForge.Application.Notifications;

/// <summary>
/// Handles inbound WhatsApp messages from Twilio (#97). State machine:
/// <list type="bullet">
///   <item>Food text → parse via NL diary parser → cache candidates → ask for YES/NO</item>
///   <item>"YES" → log all cached candidates → confirm</item>
///   <item>"NO" / "CANCEL" → clear cache → confirm cancelled</item>
/// </list>
/// Returns the reply text (caller wraps in TwiML), or <c>null</c> if the sender is unknown.
/// </summary>
public sealed class WhatsAppInboundService(
    IAppDbContext db,
    DiaryParseService parseService,
    DiaryService diaryService,
    IWhatsAppPendingStore store,
    IClock clock)
{
    public async Task<string?> HandleAsync(
        string from, string body, string messageSid, CancellationToken ct = default)
    {
        var phone = StripWaPrefix(from);

        // Resolve sender → ChannelSubscription
        var sub = await db.ChannelSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Channel == "twilio-whatsapp" && s.Enabled &&
                     (s.Address == phone || s.Address == "whatsapp:" + phone), ct)
            .ConfigureAwait(false);

        if (sub is null)
        {
            return null; // unknown sender — Twilio still gets 200, we just don't reply
        }

        // Idempotency: Twilio may re-deliver; skip if already handled
        if (await store.IsSeenAsync(messageSid, ct).ConfigureAwait(false))
        {
            return null;
        }

        await store.MarkSeenAsync(messageSid, ct).ConfigureAwait(false);

        var normalized = body.Trim().ToUpperInvariant();

        if (normalized is "YES" or "Y" or "CONFIRM")
        {
            return await ConfirmAsync(sub.UserId, phone, ct).ConfigureAwait(false);
        }

        if (normalized is "NO" or "N" or "CANCEL")
        {
            return await CancelAsync(phone, ct).ConfigureAwait(false);
        }

        return await ParseAndAskAsync(sub.UserId, phone, body, ct).ConfigureAwait(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string> ConfirmAsync(Guid userId, string from, CancellationToken ct)
    {
        var pending = await store.GetPendingAsync(from, ct).ConfigureAwait(false);
        if (pending is null || pending.Entries.Count == 0)
        {
            return "Nothing to confirm. Send me a meal description first.";
        }

        int count = 0;
        double totalKcal = 0;
        foreach (var e in pending.Entries)
        {
            var req = new AddDiaryEntryRequest(e.Date, e.MealSlot, e.FoodId, e.PortionId, e.Quantity, Method: "whatsapp");
            await diaryService.AddAsync(userId, req, ct).ConfigureAwait(false);
            totalKcal += e.Kcal;
            count++;
        }

        await store.ClearPendingAsync(from, ct).ConfigureAwait(false);
        return $"Logged {count} item(s), ~{(int)Math.Round(totalKcal)} kcal. Great job!";
    }

    private async Task<string> CancelAsync(string from, CancellationToken ct)
    {
        await store.ClearPendingAsync(from, ct).ConfigureAwait(false);
        return "Cancelled.";
    }

    private async Task<string> ParseAndAskAsync(
        Guid userId, string from, string body, CancellationToken ct)
    {
        if (!parseService.IsConfigured)
        {
            return "I can't parse messages right now — AI is not configured.";
        }

        var today = clock.Today;
        var mealSlot = GuessMealSlot(clock.UtcNow.Hour);
        var result = await parseService.ParseAsync(userId, body, mealSlot, today, ct).ConfigureAwait(false);

        if (result.Candidates.Count == 0)
        {
            var unresolvedPart = result.Unresolved.Count > 0
                ? $" (couldn't find: {string.Join(", ", result.Unresolved)})"
                : "";
            return $"I couldn't recognise any foods in that message{unresolvedPart}. Try something like: \"2 eggs and toast\".";
        }

        var pending = new WhatsAppPending(
            result.Candidates
                .Select(c => new WhatsAppPendingEntry(today, mealSlot, c.FoodId, c.PortionId, c.Quantity, c.FoodName, c.Kcal))
                .ToList());
        await store.SetPendingAsync(from, pending, ct).ConfigureAwait(false);

        var itemList = string.Join(", ", result.Candidates.Select(c => $"{c.FoodName} (~{(int)c.Kcal} kcal)"));
        var total = (int)Math.Round(result.Candidates.Sum(c => c.Kcal));
        return $"Found: {itemList}. Total: ~{total} kcal. Reply YES to log or NO to cancel.";
    }

    private static string StripWaPrefix(string from) =>
        from.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? from[9..] : from;

    private static MealSlot GuessMealSlot(int utcHour) => utcHour switch
    {
        < 10 => MealSlot.Breakfast,
        < 13 => MealSlot.Lunch,
        < 17 => MealSlot.Snack,
        < 21 => MealSlot.Dinner,
        _ => MealSlot.Snack,
    };
}
