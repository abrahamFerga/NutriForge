namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>
/// A diary entry the assistant proposes but does NOT write. The user confirms it in the UI, and the
/// actual write goes through the existing, tested, idempotent <c>POST /api/v1/diary</c> endpoint —
/// so the agent can never silently mutate the diary (application-level human-in-the-loop).
/// </summary>
public sealed record LogProposal(
    Guid FoodId,
    string FoodName,
    Guid? PortionId,
    string PortionName,
    double Quantity,
    double Grams,
    string MealSlot,
    DateOnly Date,
    double Kcal,
    double ProteinG,
    double FatG,
    double CarbG);

/// <summary>Request-scoped slot the <c>ProposeLogFood</c> tool fills so the endpoint can surface it.</summary>
public sealed class LogProposalHolder
{
    public LogProposal? Current { get; set; }
}
