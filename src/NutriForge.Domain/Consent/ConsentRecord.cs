using NutriForge.Domain.Common;

namespace NutriForge.Domain.Consent;

/// <summary>The consents NutriForge tracks. Health-data processing is GDPR Art. 9 special-category data.</summary>
public enum ConsentType
{
    TermsOfService,
    PrivacyPolicy,
    HealthDataProcessing,
    Marketing,
}

/// <summary>GDPR Art. 6(1) lawful bases for processing personal data.</summary>
public enum LawfulBasis
{
    Consent,
    Contract,
    LegalObligation,
    VitalInterests,
    PublicTask,
    LegitimateInterests,
}

/// <summary>
/// An append-only record of a single consent decision (#58): the user granted or withdrew a specific
/// consent, at a policy version, under a stated lawful basis, at a point in time. Records are never
/// updated — a change of mind is a new row, so the full trail is auditable and exportable (GDPR Art. 7(1)
/// demonstrable consent, Art. 20 portability). Owner-scoped.
/// </summary>
public sealed class ConsentRecord : IUserOwned
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public ConsentType Type { get; set; }

    /// <summary>The policy version the decision was made against (so a later re-consent is distinguishable).</summary>
    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>true = granted, false = withdrawn/declined.</summary>
    public bool Granted { get; set; }

    public LawfulBasis LawfulBasis { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}
