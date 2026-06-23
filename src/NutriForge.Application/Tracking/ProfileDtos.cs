using NutriForge.Domain.Common;
using NutriForge.Domain.Users;

namespace NutriForge.Application.Tracking;

public sealed record ProfileDto(
    Sex Sex,
    DateOnly BirthDate,
    double HeightCm,
    double WeightKg,
    double? BodyFatPct,
    ActivityLevel Activity,
    Goal Goal,
    MacroStrategy MacroStrategy,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<string> Dislikes,
    IReadOnlyList<string> PreferredDiets,
    int Version);

public sealed record UpsertProfileRequest(
    Sex Sex,
    DateOnly BirthDate,
    double HeightCm,
    double WeightKg,
    double? BodyFatPct,
    ActivityLevel Activity,
    Goal Goal,
    MacroStrategy MacroStrategy,
    IReadOnlyList<string>? Allergens,
    IReadOnlyList<string>? Dislikes,
    IReadOnlyList<string>? PreferredDiets);

internal static class ProfileMappings
{
    public static ProfileDto ToDto(this Profile p) => new(
        p.Sex, p.BirthDate, p.HeightCm, p.WeightKg, p.BodyFatPct, p.Activity, p.Goal, p.MacroStrategy,
        p.Allergens, p.Dislikes, p.PreferredDiets, p.Version);
}
