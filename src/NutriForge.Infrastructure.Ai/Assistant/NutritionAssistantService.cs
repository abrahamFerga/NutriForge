using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Assistant;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>Thrown when a chat is requested but no provider is configured (surfaced as 503).</summary>
public sealed class AssistantNotConfiguredException()
    : Exception("The NutritionAssistant is not configured. Set the 'Ai' provider options to enable it.");

/// <summary>One assistant turn: the reply text plus an optional diary-log proposal awaiting confirmation.</summary>
public sealed record AssistantTurn(string Reply, LogProposal? Proposal);

/// <summary>
/// Orchestrates one assistant turn: screen the input (guardrail), load the user's persisted MAF
/// session, run the agent (which dispatches the read tools and may propose a diary log), persist
/// the session, and return the reply + any proposal. Owner-scoped.
/// </summary>
public sealed class NutritionAssistantService(
    IAssistantAgentFactory factory,
    AssistantTools tools,
    LogProposalHolder proposals,
    IAppDbContext db,
    ICurrentUser user)
{
    public bool IsConfigured => factory.IsConfigured;

    public async Task<AssistantTurn> ChatAsync(string message, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (PromptGuard.IsLikelyInjection(message))
        {
            return new AssistantTurn(PromptGuard.RefusalMessage, null);
        }

        var uid = CurrentUserId();
        var agent = factory.CreateAgent(tools.ToolList());
        var session = await LoadSessionAsync(agent, uid, cancellationToken).ConfigureAwait(false);

        var response = await agent.RunAsync(message, session, cancellationToken: cancellationToken).ConfigureAwait(false);

        await SaveSessionAsync(agent, session, uid, cancellationToken).ConfigureAwait(false);
        return new AssistantTurn(response.Text ?? string.Empty, proposals.Current);
    }

    /// <summary>Stream the reply token-by-token; the proposal (if any) is read from the holder after.</summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string message, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (PromptGuard.IsLikelyInjection(message))
        {
            yield return PromptGuard.RefusalMessage;
            yield break;
        }

        var uid = CurrentUserId();
        var agent = factory.CreateAgent(tools.ToolList());
        var session = await LoadSessionAsync(agent, uid, cancellationToken).ConfigureAwait(false);

        await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }

        await SaveSessionAsync(agent, session, uid, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The proposal produced during the last streamed turn (read by the endpoint after the stream).</summary>
    public LogProposal? PendingProposal => proposals.Current;

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var uid = CurrentUserId();
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, cancellationToken)
            .ConfigureAwait(false);
        if (stored is not null)
        {
            db.AssistantSessions.Remove(stored);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureConfigured()
    {
        if (!factory.IsConfigured)
        {
            throw new AssistantNotConfiguredException();
        }
    }

    private Guid CurrentUserId() => user.UserId ?? throw new InvalidOperationException("No authenticated user.");

    private async Task<AgentSession> LoadSessionAsync(AIAgent agent, Guid uid, CancellationToken ct)
    {
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, ct).ConfigureAwait(false);
        return stored is null || string.IsNullOrWhiteSpace(stored.Data)
            ? await agent.CreateSessionAsync(ct).ConfigureAwait(false)
            : await agent.DeserializeSessionAsync(ParseElement(stored.Data), cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task SaveSessionAsync(AIAgent agent, AgentSession session, Guid uid, CancellationToken ct)
    {
        var saved = await agent.SerializeSessionAsync(session, cancellationToken: ct).ConfigureAwait(false);
        var json = saved.GetRawText();
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, ct).ConfigureAwait(false);
        if (stored is null)
        {
            db.AssistantSessions.Add(new AssistantSession { UserId = uid, Data = json });
        }
        else
        {
            stored.Data = json;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
