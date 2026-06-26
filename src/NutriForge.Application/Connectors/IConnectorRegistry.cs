namespace NutriForge.Application.Connectors;

/// <summary>
/// The connector registry: the installed connectors and their configured/last-run state, for the
/// admin endpoint. Joins the config-driven <see cref="ConnectorDescriptor"/> list with the persisted
/// last run of each.
/// </summary>
public interface IConnectorRegistry
{
    Task<IReadOnlyList<ConnectorStatusDto>> GetStatusAsync(CancellationToken ct = default);
}
