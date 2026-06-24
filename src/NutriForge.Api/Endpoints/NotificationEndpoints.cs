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

        // The current user's subscription for a channel (defaults to telegram).
        group.MapGet("/subscription", async (string? channel, ICurrentUser user, ChannelSubscriptionService subs, CancellationToken ct) =>
            Results.Ok(await subs.GetAsync(user.CurrentUserId(), channel ?? "telegram", ct)))
            .WithName("GetChannelSubscription");

        // Set the opt-in toggle + daily send hour. The address is bound only via the link flow below.
        group.MapPut("/subscription", async (UpdateChannelSubscriptionRequest req, ICurrentUser user, ChannelSubscriptionService subs, CancellationToken ct) =>
            Results.Ok(await subs.UpsertAsync(user.CurrentUserId(), req, ct)))
            .WithName("UpdateChannelSubscription");

        // Mint a single-use, 15-min link code (returned once; only its hash is stored). The user sends
        // it to the bot (real Telegram), or redeems it directly in dev via the endpoint below.
        group.MapPost("/link", async (LinkRequest req, ICurrentUser user, ChannelSubscriptionService subs, CancellationToken ct) =>
            Results.Ok(await subs.CreateLinkCodeAsync(user.CurrentUserId(), req?.Channel ?? "telegram", ct)))
            .WithName("CreateChannelLinkCode");

        // Redeem a link code to bind a channel address + enable the subscription. 422 on an invalid,
        // expired, or already-used code (no detail, to avoid leaking which).
        group.MapPost("/link/redeem", async (RedeemLinkRequest req, ICurrentUser user, ChannelSubscriptionService subs, CancellationToken ct) =>
        {
            var result = await subs.RedeemAsync(user.CurrentUserId(), req, ct);
            return result.Linked
                ? Results.Ok(result)
                : Results.Problem(title: "Link failed", detail: "That code is invalid, expired, or already used.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
        }).WithName("RedeemChannelLinkCode");

        return app;
    }

    public sealed record LinkRequest(string? Channel);

    public sealed record ChannelMessageDto(string Channel, string Body, DateTimeOffset CreatedAt);
}
