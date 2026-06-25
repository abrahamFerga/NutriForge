namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>Configuration for opt-in transcript enrichment (#93).</summary>
public sealed class TranscriptEnrichmentOptions
{
    public const string SectionName = "TranscriptEnrichment";

    /// <summary>
    /// Provider to use: "none" (default, feature off), "youtube-explode" (self-hosted, fragile),
    /// or "vendor" (managed external API via <see cref="VendorUrl"/>).
    /// </summary>
    public string Provider { get; set; } = "none";

    /// <summary>Base URL of the managed transcript vendor API (only used when Provider = "vendor").</summary>
    public string? VendorUrl { get; set; }

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Provider) &&
        !Provider.Equals("none", StringComparison.OrdinalIgnoreCase);
}
