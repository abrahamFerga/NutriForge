using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// Relays outbox messages so an external side effect never commits without its triggering
/// transaction. At v1 it routes <c>audit</c> messages to the separate audit database (ADR-0011).
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // a relay must not die on one bad batch; it retries next tick
            catch (Exception ex)
            {
                LogDispatchFailed(logger, ex);
            }
#pragma warning restore CA1031
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var ops = scope.ServiceProvider.GetRequiredService<NutriForgeDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var pending = await ops.Outbox
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct).ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var msg in pending)
        {
            if (msg.Type == "audit")
            {
                var p = JsonSerializer.Deserialize<AuditPayload>(msg.Payload);
                if (p is not null)
                {
                    audit.AuditRecords.Add(new AuditRecord
                    {
                        OccurredAt = p.OccurredAt,
                        EntityType = p.EntityType,
                        EntityId = p.EntityId,
                        Action = p.Action,
                        ActorId = p.ActorId,
                        Changes = JsonSerializer.Serialize(p.Changes),
                    });
                }
            }

            msg.ProcessedAt = clock.UtcNow;
            msg.Attempts++;
        }

        await audit.SaveChangesAsync(ct).ConfigureAwait(false);
        await ops.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatch batch failed; will retry.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception ex);
}
