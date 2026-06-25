using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NutriForge.Application.Notifications;
using NutriForge.Domain.Common;
using NutriForge.Domain.Diary;
using NutriForge.Domain.Notifications;
using NutriForge.Domain.Users;
using NutriForge.Infrastructure.Notifications;

namespace NutriForge.Application.Tests;

/// <summary>
/// The scheduled daily-summary worker (#95 P1): sends to enabled+linked subscribers due this UTC hour,
/// once a day, building each user's summary cross-user (IgnoreQueryFilters + explicit user scoping).
/// </summary>
public sealed class NotificationWorkerTests
{
    private static readonly DateOnly Today = new(2026, 6, 24);
    private static readonly DateTimeOffset At8 = new(2026, 6, 24, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Worker_sends_a_due_subscriber_their_summary_once_per_day()
    {
        var subscriber = Guid.NewGuid();
        // A DIFFERENT current user than the subscriber — the worker must reach across users.
        await using var db = TestDb.New(Guid.NewGuid());
        db.ChannelSubscriptions.Add(new ChannelSubscription
        {
            UserId = subscriber,
            Channel = "mock",
            Address = "chat-1",
            Enabled = true,
            SendHourUtc = 8,
        });
        db.Targets.Add(new Target { UserId = subscriber, Kcal = 2000, ProteinG = 150, FatG = 60, CarbG = 200, Formula = "t", ProfileVersion = 1 });
        db.DiaryEntries.Add(new DiaryEntry
        {
            UserId = subscriber,
            Date = Today,
            MealSlot = MealSlot.Breakfast,
            FoodId = Guid.NewGuid(),
            FoodName = "egg",
            PortionName = "1",
            Quantity = 1,
            Grams = 50,
            Kcal = 300,
            ProteinG = 20,
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var channel = new MockChannel(db);
        var sent = await DailySummaryWorker.ProcessDueAsync(db, channel, At8, NullLogger.Instance);
        Assert.Equal(1, sent);

        var messages = db.ChannelMessages.IgnoreQueryFilters().Where(m => m.UserId == subscriber).ToList();
        Assert.Single(messages);
        Assert.Contains("2000", messages[0].Body);  // the target
        Assert.Contains("1700", messages[0].Body);  // 2000 − 300 = 1700 kcal to go

        var sub = db.ChannelSubscriptions.IgnoreQueryFilters().Single(s => s.UserId == subscriber);
        Assert.Equal(Today, sub.LastSentOn);

        // Same day again ⇒ deduped, no second message.
        var again = await DailySummaryWorker.ProcessDueAsync(db, channel, At8, NullLogger.Instance);
        Assert.Equal(0, again);
        Assert.Single(db.ChannelMessages.IgnoreQueryFilters().Where(m => m.UserId == subscriber));
    }

    [Fact]
    public async Task Worker_skips_subscribers_that_are_not_due_not_linked_or_disabled()
    {
        await using var db = TestDb.New(Guid.NewGuid());
        db.ChannelSubscriptions.AddRange(
            new ChannelSubscription { UserId = Guid.NewGuid(), Channel = "mock", Address = "a", Enabled = true, SendHourUtc = 9 },   // wrong hour
            new ChannelSubscription { UserId = Guid.NewGuid(), Channel = "mock", Address = null, Enabled = true, SendHourUtc = 8 },  // not linked
            new ChannelSubscription { UserId = Guid.NewGuid(), Channel = "mock", Address = "b", Enabled = false, SendHourUtc = 8 }); // disabled
        await db.SaveChangesAsync(CancellationToken.None);

        var sent = await DailySummaryWorker.ProcessDueAsync(db, new MockChannel(db), At8, NullLogger.Instance);

        Assert.Equal(0, sent);
        Assert.Empty(db.ChannelMessages.IgnoreQueryFilters());
    }
}
