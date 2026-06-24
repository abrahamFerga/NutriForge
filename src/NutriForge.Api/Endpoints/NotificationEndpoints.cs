using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Authorization;
using NutriForge.Application.Notifications;

namespace NutriForge.Api.Endpoints;

/// <summary>Notification channels — on-demand sends and the (mock) outbox. Owner-scoped.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/notifications")
            .RequireAuthorization(Policies.OwnerOnly).WithTags("Notifications");

        // Send the current user's daily calorie summary now. The mock channel records it to the
        // outbox; a real channel (Telegram) would deliver it. Returns the message either way.
        group.MapPost("/daily-summary", async (ICurrentUser user, DailySummaryService summaries, IClock clock, CancellationToken ct) =>
        {
            var result = await summaries.SendForAsync(user.CurrentUserId(), clock.Today, ct);
            return Results.Ok(result);
        }).WithName("SendDailySummary");

        // What the channel "sent" — the outbox for the current user, newest first.
        group.MapGet("/outbox", async (ICurrentUser user, IAppDbContext db, CancellationToken ct) =>
        {
            _ = user;
            var items = await db.ChannelMessages
                .OrderByDescending(m => m.CreatedAt)
                .Take(50)
                .Select(m => new ChannelMessageDto(m.ChannelName, m.Body, m.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).WithName("GetNotificationOutbox");

        return app;
    }

    public sealed record ChannelMessageDto(string Channel, string Body, DateTimeOffset CreatedAt);
}
