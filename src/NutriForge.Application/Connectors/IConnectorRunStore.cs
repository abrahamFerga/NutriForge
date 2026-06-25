using NutriForge.Domain.Connectors;

namespace NutriForge.Application.Connectors;

/// <summary>
/// Records and reads the latest run of each outbound connector. A batch connector calls
/// <see cref="BeginAsync"/> when it starts, then <see cref="CompleteAsync"/> or <see cref="FailAsync"/>;
/// the registry reads the latest via <see cref="LatestByKeyAsync"/>. Writes and reads cross the
/// worker→API process boundary through the shared database.
/// </summary>
public interface IConnectorRunStore
{
    /// <summary>Mark a connector as running now (upsert: Status=Running, CompletedAt cleared).</summary>
    Task BeginAsync(string connectorKey, CancellationToken ct = default);

    /// <summary>Mark the in-flight run successful, with the item count and an optional note.</summary>
    Task CompleteAsync(string connectorKey, int itemsProcessed, string? detail = null, CancellationToken ct = default);

    /// <summary>Mark the in-flight run failed, with the error detail.</summary>
    Task FailAsync(string connectorKey, string detail, CancellationToken ct = default);

    /// <summary>The latest run per connector key (absent keys never ran).</summary>
    Task<IReadOnlyDictionary<string, ConnectorRun>> LatestByKeyAsync(CancellationToken ct = default);
}
