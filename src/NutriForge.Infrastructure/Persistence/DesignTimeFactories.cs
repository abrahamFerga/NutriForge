using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NutriForge.Infrastructure.Identity;

namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the model and generate migrations without a
/// running host or a live connection string. The placeholder connection is never opened.
/// </summary>
public sealed class NutriForgeDbContextFactory : IDesignTimeDbContextFactory<NutriForgeDbContext>
{
    private const string DesignConnection = "Host=localhost;Database=appdb;Username=postgres;Password=postgres";

    public NutriForgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NutriForgeDbContext>()
            .UseNpgsql(DesignConnection)
            .Options;
        return new NutriForgeDbContext(options, new SystemCurrentUser());
    }
}

/// <summary>Design-time factory for the separate audit database.</summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    private const string DesignConnection = "Host=localhost;Database=auditdb;Username=postgres;Password=postgres";

    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(DesignConnection)
            .Options;
        return new AuditDbContext(options);
    }
}
