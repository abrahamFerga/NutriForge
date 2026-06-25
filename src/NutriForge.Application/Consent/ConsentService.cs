using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Consent;

namespace NutriForge.Application.Consent;

/// <summary>A consent NutriForge asks for: its current version, whether it's mandatory, and its lawful basis.</summary>
public sealed record ConsentDefinition(ConsentType Type, string Version, bool Required, LawfulBasis LawfulBasis, string Description);

/// <summary>The user's current standing on one consent, against the live catalog.</summary>
public sealed record ConsentStatusDto(
    ConsentType Type,
    string Version,
    bool Required,
    LawfulBasis LawfulBasis,
    string Description,
    bool Granted,
    string? DecidedVersion,
    DateTimeOffset? DecidedAt,
    bool NeedsAction);

/// <summary>One row of the append-only consent trail (surfaced in the GDPR export).</summary>
public sealed record ConsentHistoryDto(ConsentType Type, string PolicyVersion, bool Granted, LawfulBasis LawfulBasis, DateTimeOffset RecordedAt);

/// <summary>Record a consent decision.</summary>
public sealed record RecordConsentRequest(ConsentType Type, bool Granted);

/// <summary>
/// Consent + lawful-basis capture (#58). Maintains an append-only trail of grant/withdraw decisions per
/// user, computes current standing against a versioned catalog, and exposes the trail for the GDPR data
/// export. Withdrawal is just a new "not granted" row, so consent can always be revoked (Art. 7(3)).
/// </summary>
public sealed class ConsentService(IAppDbContext db, IClock clock)
{
    /// <summary>The consents in force. Bumping a <c>Version</c> re-prompts users who consented to the old one.</summary>
    public static readonly IReadOnlyList<ConsentDefinition> Catalog =
    [
        new(ConsentType.TermsOfService, "2026-01", Required: true, LawfulBasis.Contract,
            "The terms governing your use of NutriForge."),
        new(ConsentType.PrivacyPolicy, "2026-01", Required: true, LawfulBasis.Consent,
            "How we process your personal data."),
        new(ConsentType.HealthDataProcessing, "2026-01", Required: true, LawfulBasis.Consent,
            "Explicit consent to process health-related data (weight, body metrics) to compute your nutrition targets."),
        new(ConsentType.Marketing, "2026-01", Required: false, LawfulBasis.Consent,
            "Occasional product news and tips. Entirely optional."),
    ];

    private static ConsentDefinition Definition(ConsentType type) => Catalog.First(c => c.Type == type);

    /// <summary>Append a grant/withdraw decision at the current catalog version + lawful basis.</summary>
    public async Task<ConsentRecord> RecordAsync(Guid userId, ConsentType type, bool granted, CancellationToken ct = default)
    {
        var def = Definition(type);
        var record = new ConsentRecord
        {
            UserId = userId,
            Type = type,
            Granted = granted,
            PolicyVersion = def.Version,
            LawfulBasis = def.LawfulBasis,
            RecordedAt = clock.UtcNow,
        };
        db.ConsentRecords.Add(record);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record;
    }

    /// <summary>Withdraw a consent (Art. 7(3)) — recorded as a new "not granted" row, preserving the trail.</summary>
    public Task<ConsentRecord> WithdrawAsync(Guid userId, ConsentType type, CancellationToken ct = default)
        => RecordAsync(userId, type, granted: false, ct);

    /// <summary>Current standing for every catalogued consent (latest decision per type vs the live version).</summary>
    public async Task<IReadOnlyList<ConsentStatusDto>> StatusAsync(Guid userId, CancellationToken ct = default)
    {
        var records = await db.ConsentRecords.AsNoTracking()
            .Where(r => r.UserId == userId)
            .ToListAsync(ct).ConfigureAwait(false);

        var latestByType = records
            .GroupBy(r => r.Type)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RecordedAt).First());

        return [.. Catalog.Select(def =>
        {
            latestByType.TryGetValue(def.Type, out var latest);
            var granted = latest?.Granted ?? false;
            var atCurrentVersion = granted && latest!.PolicyVersion == def.Version;
            // A required consent "needs action" until it's granted at the current version (never decided,
            // an older version, or since withdrawn).
            return new ConsentStatusDto(
                def.Type, def.Version, def.Required, def.LawfulBasis, def.Description,
                granted, latest?.PolicyVersion, latest?.RecordedAt, def.Required && !atCurrentVersion);
        })];
    }

    /// <summary>The full append-only trail, oldest-first, for the Art. 20 data export.</summary>
    public async Task<IReadOnlyList<ConsentHistoryDto>> HistoryAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.ConsentRecords.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(r => new ConsentHistoryDto(r.Type, r.PolicyVersion, r.Granted, r.LawfulBasis, r.RecordedAt))];
    }

    /// <summary>True when any mandatory consent is still outstanding (drives the signup/onboarding gate).</summary>
    public async Task<bool> HasOutstandingRequiredAsync(Guid userId, CancellationToken ct = default)
        => (await StatusAsync(userId, ct).ConfigureAwait(false)).Any(s => s.NeedsAction);
}
