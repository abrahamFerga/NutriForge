using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Common;

namespace NutriForge.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps <see cref="ITimestamped"/> entities and, for every <see cref="IAuditable"/> mutation,
/// enqueues a PII-redacted audit record onto the outbox in the same transaction (ADR-0011).
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
{
    private const string Redacted = "***";
    private static readonly ConcurrentDictionary<Type, HashSet<string>> PiiPropertyCache = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            Handle(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            Handle(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Handle(DbContext context)
    {
        var now = clock.UtcNow;
        var actor = currentUser.UserId?.ToString() ?? "system";
        var audits = new List<OutboxMessage>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ITimestamped ts)
            {
                if (entry.State == EntityState.Added)
                {
                    ts.CreatedAt = now;
                }

                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    ts.UpdatedAt = now;
                }
            }

            if (entry.Entity is IAuditable
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                audits.Add(BuildAudit(entry, actor, now));
            }
        }

        if (audits.Count > 0)
        {
            context.Set<OutboxMessage>().AddRange(audits);
        }
    }

    private static OutboxMessage BuildAudit(EntityEntry entry, string actor, DateTimeOffset now)
    {
        var clrType = entry.Entity.GetType();
        var pii = PiiPropertyCache.GetOrAdd(clrType, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<PiiAttribute>() is not null)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal));

        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var prop in entry.Properties)
        {
            if (prop.Metadata.IsPrimaryKey())
            {
                continue;
            }

            // For modifications, only record properties that actually changed.
            if (entry.State == EntityState.Modified && !prop.IsModified)
            {
                continue;
            }

            var name = prop.Metadata.Name;
            changes[name] = pii.Contains(name) ? Redacted : prop.CurrentValue?.ToString();
        }

        var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
        var payload = new AuditPayload(
            now,
            clrType.Name,
            idProp?.CurrentValue?.ToString() ?? string.Empty,
            entry.State.ToString(),
            actor,
            changes);

        return new OutboxMessage
        {
            Type = "audit",
            OccurredAt = now,
            Payload = JsonSerializer.Serialize(payload),
        };
    }
}
