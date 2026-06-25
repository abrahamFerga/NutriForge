using Microsoft.EntityFrameworkCore;
using NutriForge.Domain.Assistant;
using NutriForge.Domain.Diary;
using NutriForge.Domain.Notifications;
using NutriForge.Domain.Planning;
using NutriForge.Domain.Users;

namespace NutriForge.Application.Abstractions;

/// <summary>
/// The operational, user-owned store (schema <c>app</c>). User-owned sets are subject to the
/// global per-user query filter; <see cref="Users"/> is keyed by id, not filtered.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Profile> Profiles { get; }
    DbSet<Target> Targets { get; }
    DbSet<DiaryEntry> DiaryEntries { get; }
    DbSet<BodyMeasurement> BodyMeasurements { get; }
    DbSet<AssistantSession> AssistantSessions { get; }
    DbSet<PantryItem> PantryItems { get; }
    DbSet<ShoppingList> ShoppingLists { get; }
    DbSet<MealPlan> MealPlans { get; }
    DbSet<ChannelMessage> ChannelMessages { get; }
    DbSet<ChannelSubscription> ChannelSubscriptions { get; }
    DbSet<AccountLinkToken> AccountLinkTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
