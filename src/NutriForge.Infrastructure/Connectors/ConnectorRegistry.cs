using NutriForge.Application.Connectors;

namespace NutriForge.Infrastructure.Connectors;

/// <summary>
/// Joins the config-driven connector descriptors with each connector's persisted last run. The
/// descriptor list is injected (built once from configuration at startup), so the registry itself
/// holds no knowledge of which connectors exist or how they're configured.
/// </summary>
public sealed class ConnectorRegistry(IEnumerable<ConnectorDescriptor> descriptors, IConnectorRunStore runs)
    : IConnectorRegistry
{
    public async Task<IReadOnlyList<ConnectorStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        var latest = await runs.LatestByKeyAsync(ct).ConfigureAwait(false);

        return [.. descriptors.Select(d =>
        {
            ConnectorRunDto? lastRun = latest.TryGetValue(d.Key, out var run)
                ? new ConnectorRunDto(run.StartedAt, run.CompletedAt, run.Status.ToString(), run.ItemsProcessed, run.Detail)
                : null;

            return new ConnectorStatusDto(d.Key, d.DisplayName, d.Kind.ToString(), d.Direction, d.Configured, lastRun);
        })];
    }
}
