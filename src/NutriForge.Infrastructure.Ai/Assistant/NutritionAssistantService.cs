using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Assistant;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>Thrown when a chat is requested but no provider is configured (surfaced as 503).</summary>
public sealed class AssistantNotConfiguredException()
    : Exception("The NutritionAssistant is not configured. Set the 'Ai' provider options to enable it.");

/// <summary>
/// Orchestrates one assistant turn: load the user's persisted MAF session, run the agent (which
/// dispatches the read tools), persist the updated session, and return the reply. Owner-scoped.
/// </summary>
public sealed class NutritionAssistantService(
    IAssistantAgentFactory factory,
    AssistantTools tools,
    IAppDbContext db,
    ICurrentUser user)
{
    public bool IsConfigured => factory.IsConfigured;

    public async Task<string> ChatAsync(string message, CancellationToken cancellationToken)
    {
        if (!factory.IsConfigured)
        {
            throw new AssistantNotConfiguredException();
        }

        var uid = user.UserId ?? throw new InvalidOperationException("No authenticated user.");
        var agent = factory.CreateAgent(tools.ToolList());

        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, cancellationToken)
            .ConfigureAwait(false);

        var session = stored is null || string.IsNullOrWhiteSpace(stored.Data)
            ? await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false)
            : await agent.DeserializeSessionAsync(ParseElement(stored.Data), cancellationToken: cancellationToken).ConfigureAwait(false);

        var response = await agent.RunAsync(message, session, cancellationToken: cancellationToken).ConfigureAwait(false);

        var saved = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(false);
        var json = saved.GetRawText();
        if (stored is null)
        {
            db.AssistantSessions.Add(new AssistantSession { UserId = uid, Data = json });
        }
        else
        {
            stored.Data = json;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return response.Text ?? string.Empty;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var uid = user.UserId ?? throw new InvalidOperationException("No authenticated user.");
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, cancellationToken)
            .ConfigureAwait(false);
        if (stored is not null)
        {
            db.AssistantSessions.Remove(stored);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
