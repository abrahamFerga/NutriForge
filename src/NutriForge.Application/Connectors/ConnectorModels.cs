namespace NutriForge.Application.Connectors;

/// <summary>What kind of external system a connector talks to (shapes how operators read the list).</summary>
public enum ConnectorKind
{
    /// <summary>A nutrition/food catalog source (USDA, Open Food Facts).</summary>
    FoodSource,

    /// <summary>A recipe extractor (YouTube/web import).</summary>
    RecipeImport,

    /// <summary>The chat/vision LLM provider.</summary>
    LlmProvider,

    /// <summary>An outbound messaging channel (Telegram, email).</summary>
    Notification,
}

/// <summary>
/// A statically-known connector the app can talk to, plus whether the current configuration actually
/// enables it. The descriptor list is the "registry"; it's assembled from config at startup so adding
/// a connector is a one-line descriptor, not a code change in the endpoint.
/// </summary>
public sealed record ConnectorDescriptor(
    string Key,
    string DisplayName,
    ConnectorKind Kind,
    string Direction,
    bool Configured);

/// <summary>The last-run facts for a connector, projected for the admin endpoint.</summary>
public sealed record ConnectorRunDto(
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int ItemsProcessed,
    string? Detail);

/// <summary>One row of the registry status: the descriptor joined with its last run (if any).</summary>
public sealed record ConnectorStatusDto(
    string Key,
    string Name,
    string Kind,
    string Direction,
    bool Configured,
    ConnectorRunDto? LastRun);
