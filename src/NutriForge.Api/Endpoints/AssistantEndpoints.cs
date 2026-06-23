using NutriForge.Api.Setup;
using NutriForge.Application.Authorization;
using NutriForge.Infrastructure.Ai.Assistant;

namespace NutriForge.Api.Endpoints;

/// <summary>The always-present NutritionAssistant (MAF) — chat, status, and clear-conversation.</summary>
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

        // One conversational turn. The agent dispatches read tools that own the numbers.
        group.MapPost("/chat", async (ChatRequest req, NutritionAssistantService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message) || req.Message.Length > 2000)
            {
                return Results.Problem(
                    title: "Invalid message",
                    detail: "Message must be between 1 and 2000 characters.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var reply = await svc.ChatAsync(req.Message.Trim(), ct);
            return Results.Ok(new { reply });
        })
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithName("AssistantChat");

        // Clear the persisted conversation.
        group.MapDelete("/session", async (NutritionAssistantService svc, CancellationToken ct) =>
        {
            await svc.ClearAsync(ct);
            return Results.NoContent();
        })
            .WithName("ClearAssistantSession");

        return app;
    }
}
