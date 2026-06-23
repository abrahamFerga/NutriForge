using Microsoft.EntityFrameworkCore;

namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// The append-only audit store (database <c>auditdb</c>, separate from the operational DB —
/// ADR-0011). Written only by the outbox dispatcher; never updated or deleted in normal flow.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<AuditRecord>(e =>
        {
            e.ToTable("audit_records", "audit");
            e.HasKey(a => a.Id);
            e.Property(a => a.EntityType).HasMaxLength(100);
            e.Property(a => a.EntityId).HasMaxLength(100);
            e.Property(a => a.Action).HasMaxLength(20);
            e.Property(a => a.ActorId).HasMaxLength(100);
            e.Property(a => a.Changes).HasColumnType("jsonb");
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => a.OccurredAt);
        });
    }
}
