using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

internal sealed class FakeCurrentUser(Guid userId) : ICurrentUser
{
    public Guid? UserId => userId;
    public string? Subject => userId.ToString();
    public string? Email => null;
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => false;
}

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

internal static class TestDb
{
    public static NutriForgeDbContext New(Guid userId)
    {
        var options = new DbContextOptionsBuilder<NutriForgeDbContext>()
            .UseInMemoryDatabase($"nf-{Guid.NewGuid()}")
            .Options;
        return new NutriForgeDbContext(options, new FakeCurrentUser(userId));
    }
}
