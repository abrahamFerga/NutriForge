using Microsoft.EntityFrameworkCore;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Api.Setup;

/// <summary>Applies EF Core migrations at startup and seeds the dev catalog (ARCH data-model).</summary>
public static class DatabaseStartup
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var ops = sp.GetRequiredService<NutriForgeDbContext>();
        var audit = sp.GetRequiredService<AuditDbContext>();

        await ops.Database.MigrateAsync();
        await audit.Database.MigrateAsync();

        if (app.Environment.IsDevelopment())
        {
            await DevDataSeeder.SeedAsync(ops);
        }
    }
}
