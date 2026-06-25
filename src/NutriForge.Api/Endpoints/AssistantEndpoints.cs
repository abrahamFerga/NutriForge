using System.Text.Json;
using NutriForge.Api.Setup;
using NutriForge.Application.Authorization;
using NutriForge.Infrastructure.Ai.Assistant;

namespace NutriForge.Api.Endpoints;

/// <summary>The always-present NutritionAssistant (MAF) — chat, streaming chat, status, clear.</summary>
public static class AssistantEndpoints
{
    public sealed record ChatRequest(string Message);

    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/assistant")
            .RequireAuthorization(Policies.OwnerOnly)
            .WithTags("Assistant");

        // Whether a chat provider is configured — lets the SPA enable/disable the panel input.
        group.MapGet("/status", (NutritionAssistantService svc) =>
            Results.Ok(new { configured = svc.IsConfigured }))
            .WithName("AssistantStatus");

        // Monthly token usage for the authenticated user.
        group.MapGet("/usage", async (NutritionAssistantService svc, CancellationToken ct) =>
        {
            var (used, budgetK) = await svc.GetUsageAsync(ct);
            return Results.Ok(new { tokensUsedThisMonth = used, monthlyBudgetK = budgetK });
        })
            .WithName("AssistantUsage");

        // One conversational turn. Returns the reply and an optional diary-log proposal the user
        // confirms via the normal diary endpoint (the agent never writes the diary itself).
        group.MapPost("/chat", async (ChatRequest req, NutritionAssistantService svc, CancellationToken ct) =>
        {
            if (!IsValid(req, out var message))
            {
                return InvalidMessage();
            }

            try
            {
                var turn = await svc.ChatAsync(message, ct);
                return Results.Ok(new { reply = turn.Reply, proposal = turn.Proposal });
            }
            catch (TokenBudgetExceededException ex)
            {
                return Results.Problem(
                    title: "Monthly AI budget exceeded",
                    detail: $"You have used {ex.Used:N0} of your {ex.Budget:N0} monthly token budget. It resets on the 1st.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("AssistantChat");

        // Streaming variant: server-sent events. `data:` events carry text chunks; a final
        // `proposal` event (if any) and a `done` event close the stream.
        group.MapPost("/chat/stream", async (ChatRequest req, NutritionAssistantService svc, HttpContext http, CancellationToken ct) =>
        {
            if (!svc.IsConfigured)
            {
                return Results.Problem(title: "Assistant unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!IsValid(req, out var message))
            {
                return InvalidMessage();
            }

            // Assert budget BEFORE writing SSE headers so we can still return a 429.
            try
            {
                await svc.AssertBudgetAsync(message, ct);
            }
            catch (TokenBudgetExceededException ex)
            {
                return Results.Problem(
                    title: "Monthly AI budget exceeded",
                    detail: $"You have used {ex.Used:N0} of your {ex.Budget:N0} monthly token budget. It resets on the 1st.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers["Cache-Control"] = "no-cache";

            await foreach (var chunk in svc.ChatStreamAsync(message, ct))
            {
                await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { text = chunk })}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }

            if (svc.PendingProposal is { } proposal)
            {
                await http.Response.WriteAsync($"event: proposal\ndata: {JsonSerializer.Serialize(proposal)}\n\n", ct);
            }

            await http.Response.WriteAsync("event: done\ndata: {}\n\n", ct);
            return Results.Empty;
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("AssistantChatStream");

        // Clear the persisted conversation.
        group.MapDelete("/session", async (NutritionAssistantService svc, CancellationToken ct) =>
        {
            await svc.ClearAsync(ct);
            return Results.NoContent();
        })
            .WithName("ClearAssistantSession");

        return app;
    }

    private static bool IsValid(ChatRequest req, out string message)
    {
        message = req.Message?.Trim() ?? string.Empty;
        return message.Length is > 0 and <= 2000;
    }

    private static IResult InvalidMessage() => Results.Problem(
        title: "Invalid message",
        detail: "Message must be between 1 and 2000 characters.",
        statusCode: StatusCodes.Status400BadRequest);
}
