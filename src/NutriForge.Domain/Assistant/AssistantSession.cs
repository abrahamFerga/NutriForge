using NutriForge.Domain.Common;

namespace NutriForge.Domain.Assistant;

/// <summary>
/// The persisted, serialized MAF <c>AgentSession</c> for a user's NutritionAssistant conversation,
/// so the chat resumes across requests. Owner-scoped (one row per user). The blob is opaque agent
/// state — never a source of nutrition numbers (those come from tools at run time).
/// </summary>
public sealed class AssistantSession : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>Serialized <c>AgentSession</c> JSON.</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>Estimated token count (input + output) consumed in the current calendar month.</summary>
    public long TokensUsedThisMonth { get; set; }

    /// <summary>The month in which <see cref="TokensUsedThisMonth"/> was last reset (year + month, day = 1).</summary>
    public DateOnly BudgetResetMonth { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
