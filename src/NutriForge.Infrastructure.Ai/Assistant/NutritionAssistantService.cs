using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Observability;
using NutriForge.Domain.Assistant;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>Thrown when a chat is requested but no provider is configured (surfaced as 503).</summary>
public sealed class AssistantNotConfiguredException()
    : Exception("The NutritionAssistant is not configured. Set the 'Ai' provider options to enable it.");

/// <summary>Thrown when the user's monthly token budget is exhausted.</summary>
public sealed class TokenBudgetExceededException(long used, long budget)
    : Exception($"Monthly AI token budget exceeded ({used:N0} / {budget:N0} tokens used).")
{
    public long Used { get; } = used;
    public long Budget { get; } = budget;
}

/// <summary>One assistant turn: the reply text plus an optional diary-log proposal awaiting confirmation.</summary>
public sealed record AssistantTurn(string Reply, LogProposal? Proposal);

/// <summary>
/// Orchestrates one assistant turn: screen the input (guardrail), enforce the monthly token budget,
/// load the user's persisted MAF session, run the agent, persist the session, emit OTel metrics,
/// and return the reply + any proposal. Owner-scoped.
/// </summary>
public sealed class NutritionAssistantService(
    IAssistantAgentFactory factory,
    AssistantTools tools,
    LogProposalHolder proposals,
    AiOptions aiOptions,
    NutriForgeMetrics metrics,
    IAppDbContext db,
    ICurrentUser user)
{
    public bool IsConfigured => factory.IsConfigured;

    public async Task<AssistantTurn> ChatAsync(string message, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (PromptGuard.IsLikelyInjection(message))
        {
            metrics.AiTurnCompleted("injection", factory.Model, 0, 0, 0);
            return new AssistantTurn(PromptGuard.RefusalMessage, null);
        }

        var uid = CurrentUserId();
        var session = await LoadOrCreateStoredSessionAsync(uid, cancellationToken).ConfigureAwait(false);
        CheckBudget(session, message);

        var agent = factory.CreateAgent(tools.ToolList());
        var isNew = string.IsNullOrWhiteSpace(session.Data);
        var mafSession = await DeserializeOrCreateAsync(agent, session, cancellationToken).ConfigureAwait(false);
        var enrichedMessage = isNew
            ? await InjectContextAsync(message, session.PersonalContext, cancellationToken).ConfigureAwait(false)
            : message;

        var sw = Stopwatch.StartNew();
        var response = await agent.RunAsync(enrichedMessage, mafSession, cancellationToken: cancellationToken).ConfigureAwait(false);
        sw.Stop();

        var reply = response.Text ?? string.Empty;
        AccumulateTokens(session, enrichedMessage, reply);
        await PersistSessionAsync(agent, mafSession, session, uid, cancellationToken).ConfigureAwait(false);

        metrics.AiTurnCompleted("ok", factory.Model,
            EstimateTokens(enrichedMessage), EstimateTokens(reply), sw.Elapsed.TotalSeconds);

        return new AssistantTurn(reply, proposals.Current);
    }

    /// <summary>Stream the reply token-by-token; the proposal (if any) is read from the holder after.</summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string message, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (PromptGuard.IsLikelyInjection(message))
        {
            metrics.AiTurnCompleted("injection", factory.Model, 0, 0, 0);
            yield return PromptGuard.RefusalMessage;
            yield break;
        }

        var uid = CurrentUserId();
        var session = await LoadOrCreateStoredSessionAsync(uid, cancellationToken).ConfigureAwait(false);
        CheckBudget(session, message);

        var agent = factory.CreateAgent(tools.ToolList());
        var isNew = string.IsNullOrWhiteSpace(session.Data);
        var mafSession = await DeserializeOrCreateAsync(agent, session, cancellationToken).ConfigureAwait(false);
        var enrichedMessage = isNew
            ? await InjectContextAsync(message, session.PersonalContext, cancellationToken).ConfigureAwait(false)
            : message;

        var sw = Stopwatch.StartNew();
        var outputBuilder = new System.Text.StringBuilder();

        await foreach (var update in agent.RunStreamingAsync(enrichedMessage, mafSession, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                outputBuilder.Append(text);
                yield return text;
            }
        }

        sw.Stop();
        var outputText = outputBuilder.ToString();
        AccumulateTokens(session, enrichedMessage, outputText);
        await PersistSessionAsync(agent, mafSession, session, uid, cancellationToken).ConfigureAwait(false);

        metrics.AiTurnCompleted("ok", factory.Model,
            EstimateTokens(enrichedMessage), EstimateTokens(outputText), sw.Elapsed.TotalSeconds);
    }

    /// <summary>The proposal produced during the last streamed turn (read by the endpoint after the stream).</summary>
    public LogProposal? PendingProposal => proposals.Current;

    /// <summary>
    /// Clears the MAF conversation history (Data) but preserves PersonalContext so remembered
    /// preferences survive across chat resets (#77).
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var uid = CurrentUserId();
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, cancellationToken)
            .ConfigureAwait(false);
        if (stored is not null)
        {
            stored.Data = string.Empty;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Clears the user's persisted personal memory without touching the conversation history.</summary>
    public async Task ClearPersonalContextAsync(CancellationToken cancellationToken)
    {
        var uid = CurrentUserId();
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, cancellationToken)
            .ConfigureAwait(false);
        if (stored is not null)
        {
            stored.PersonalContext = null;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Throws <see cref="TokenBudgetExceededException"/> if the user is over their monthly budget.
    /// Use this before writing HTTP response headers so the caller can still return a 429.
    /// </summary>
    public async Task AssertBudgetAsync(string nextMessage, CancellationToken cancellationToken)
    {
        if (aiOptions.MonthlyTokenBudgetK <= 0)
        {
            return;
        }

        var uid = CurrentUserId();
        var stored = await db.AssistantSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == uid, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            CheckBudget(stored, nextMessage);
        }
    }

    /// <summary>Current monthly token usage for the authenticated user (0 if no session exists).</summary>
    public async Task<(long Used, long BudgetK)> GetUsageAsync(CancellationToken cancellationToken)
    {
        var uid = CurrentUserId();
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, cancellationToken)
            .ConfigureAwait(false);
        var used = stored is null ? 0L : CurrentMonthTokens(stored);
        return (used, aiOptions.MonthlyTokenBudgetK);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void EnsureConfigured()
    {
        if (!factory.IsConfigured)
        {
            throw new AssistantNotConfiguredException();
        }
    }

    private Guid CurrentUserId() => user.UserId ?? throw new InvalidOperationException("No authenticated user.");

    private void CheckBudget(AssistantSession stored, string message)
    {
        if (aiOptions.MonthlyTokenBudgetK <= 0)
        {
            return;
        }

        var budgetTokens = (long)aiOptions.MonthlyTokenBudgetK * 1_000;
        var used = CurrentMonthTokens(stored);
        if (used + EstimateTokens(message) > budgetTokens)
        {
            metrics.AiTurnCompleted("budget_exceeded", factory.Model, 0, 0, 0);
            throw new TokenBudgetExceededException(used, budgetTokens);
        }
    }

    private static long CurrentMonthTokens(AssistantSession stored)
    {
        var thisMonth = new DateOnly(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1);
        return stored.BudgetResetMonth == thisMonth ? stored.TokensUsedThisMonth : 0L;
    }

    private static void AccumulateTokens(AssistantSession stored, string input, string output)
    {
        var thisMonth = new DateOnly(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1);
        if (stored.BudgetResetMonth != thisMonth)
        {
            stored.BudgetResetMonth = thisMonth;
            stored.TokensUsedThisMonth = 0;
        }
        stored.TokensUsedThisMonth += EstimateTokens(input) + EstimateTokens(output);
    }

    private static long EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0L : Math.Max(1L, text.Length / 4);

    /// <summary>
    /// Prepends a [Context] block to the first message of a new MAF session (#79/#77).
    /// Swallows any exception so a diary outage never blocks the chat.
    /// </summary>
    private async Task<string> InjectContextAsync(string message, string? personalContext, CancellationToken ct)
    {
        try
        {
            var prefix = await tools.BuildSessionContextAsync(personalContext, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                return prefix + "\n\n" + message;
            }
        }
#pragma warning disable CA1031 // best-effort; a diary outage must not prevent the chat from starting
        catch
        {
            // swallow — use original message
        }
#pragma warning restore CA1031

        return message;
    }

    private async Task<AssistantSession> LoadOrCreateStoredSessionAsync(Guid uid, CancellationToken ct)
    {
        var stored = await db.AssistantSessions.FirstOrDefaultAsync(s => s.UserId == uid, ct).ConfigureAwait(false);
        if (stored is null)
        {
            stored = new AssistantSession { UserId = uid };
            db.AssistantSessions.Add(stored);
        }
        return stored;
    }

    private static async Task<AgentSession> DeserializeOrCreateAsync(
        AIAgent agent, AssistantSession stored, CancellationToken ct)
    {
        return string.IsNullOrWhiteSpace(stored.Data)
            ? await agent.CreateSessionAsync(ct).ConfigureAwait(false)
            : await agent.DeserializeSessionAsync(ParseElement(stored.Data), cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task PersistSessionAsync(
        AIAgent agent, AgentSession mafSession, AssistantSession stored, Guid uid, CancellationToken ct)
    {
        var saved = await agent.SerializeSessionAsync(mafSession, cancellationToken: ct).ConfigureAwait(false);
        stored.Data = saved.GetRawText();

        // If not already tracked (new session), EF tracks it from LoadOrCreateStoredSessionAsync.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
