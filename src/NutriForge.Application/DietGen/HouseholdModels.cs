namespace NutriForge.Application.DietGen;

/// <summary>A saved household member as returned to the client.</summary>
public sealed record HouseholdMemberDto(Guid Id, string Name, string? Relationship, double? TargetKcal);

/// <summary>Create or update a saved household member. A null <see cref="TargetKcal"/> means "same as me".</summary>
public sealed record UpsertHouseholdMemberRequest(string Name, string? Relationship, double? TargetKcal);
