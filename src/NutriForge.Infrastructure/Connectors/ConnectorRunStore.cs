using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Connectors;
using NutriForge.Domain.Connectors;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Infrastructure.Connectors;

/// <summary>Persists the latest run per connector (upsert by key) in the shared appdb.</summary>
public sealed class ConnectorRunStore(NutriForgeDbContext db, IClock clock) : IConnectorRunStore
{
    public async Task BeginAsync(string connectorKey, CancellationToken ct = default)
    {
        var run = await GetOrCreateAsync(connectorKey, ct).ConfigureAwait(false);
        run.StartedAt = clock.UtcNow;
        run.CompletedAt = null;
        run.Status = ConnectorRunStatus.Running;
        run.ItemsProcessed = 0;
        run.Detail = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CompleteAsync(string connectorKey, int itemsProcessed, string? detail = null, CancellationToken ct = default)
    {
        var run = await GetOrCreateAsync(connectorKey, ct).ConfigureAwait(false);
        run.CompletedAt = clock.UtcNow;
        run.Status = ConnectorRunStatus.Success;
        run.ItemsProcessed = itemsProcessed;
        run.Detail = detail;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task FailAsync(string connectorKey, string detail, CancellationToken ct = default)
    {
        var run = await GetOrCreateAsync(connectorKey, ct).ConfigureAwait(false);
        run.CompletedAt = clock.UtcNow;
        run.Status = ConnectorRunStatus.Failed;
        run.Detail = detail;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, ConnectorRun>> LatestByKeyAsync(CancellationToken ct = default)
        => await db.ConnectorRuns.AsNoTracking().ToDictionaryAsync(r => r.ConnectorKey, ct).ConfigureAwait(false);

    private async Task<ConnectorRun> GetOrCreateAsync(string key, CancellationToken ct)
    {
        var run = await db.ConnectorRuns.FirstOrDefaultAsync(r => r.ConnectorKey == key, ct).ConfigureAwait(false);
        if (run is null)
        {
            run = new ConnectorRun { ConnectorKey = key, StartedAt = clock.UtcNow };
            db.ConnectorRuns.Add(run);
        }

        return run;
    }
}
