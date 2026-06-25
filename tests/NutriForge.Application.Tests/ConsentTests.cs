using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Consent;
using NutriForge.Domain.Consent;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>Consent + lawful-basis capture (#58) — append-only trail, current standing, withdrawal.</summary>
public sealed class ConsentTests
{
    /// <summary>A clock that advances on demand, so consecutive decisions get distinct timestamps.</summary>
    private sealed class AdvancingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;
        public DateTimeOffset UtcNow => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private static AdvancingClock NewClock() => new(new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero));

    private static NutriForgeDbContext Db(Guid userId, string name) =>
        new(new DbContextOptionsBuilder<NutriForgeDbContext>().UseInMemoryDatabase(name).Options, new FakeCurrentUser(userId));

    private static bool Needs(IReadOnlyList<ConsentStatusDto> status, ConsentType type) =>
        status.First(s => s.Type == type).NeedsAction;

    [Fact]
    public async Task Required_consents_start_outstanding_and_marketing_does_not()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new ConsentService(db, NewClock());

        var status = await svc.StatusAsync(userId);

        Assert.True(Needs(status, ConsentType.TermsOfService));
        Assert.True(Needs(status, ConsentType.PrivacyPolicy));
        Assert.True(Needs(status, ConsentType.HealthDataProcessing));
        Assert.False(Needs(status, ConsentType.Marketing)); // optional → never blocks
        Assert.True(await svc.HasOutstandingRequiredAsync(userId));
    }

    [Fact]
    public async Task Granting_all_required_clears_the_outstanding_gate()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new ConsentService(db, NewClock());

        foreach (var def in ConsentService.Catalog.Where(c => c.Required))
        {
            await svc.RecordAsync(userId, def.Type, granted: true);
        }

        Assert.False(await svc.HasOutstandingRequiredAsync(userId));
        var granted = (await svc.StatusAsync(userId)).First(s => s.Type == ConsentType.HealthDataProcessing);
        Assert.True(granted.Granted);
        Assert.Equal(LawfulBasis.Consent, granted.LawfulBasis); // stamped from the catalog
    }

    [Fact]
    public async Task Withdrawal_reopens_the_gate_and_keeps_the_full_trail()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var clock = NewClock();
        var svc = new ConsentService(db, clock);

        await svc.RecordAsync(userId, ConsentType.Marketing, granted: true);
        clock.Advance(TimeSpan.FromMinutes(1));
        await svc.WithdrawAsync(userId, ConsentType.Marketing);

        // Latest decision wins → marketing is now not granted...
        var marketing = (await svc.StatusAsync(userId)).First(s => s.Type == ConsentType.Marketing);
        Assert.False(marketing.Granted);

        // ...but the grant is preserved (append-only trail): two rows, oldest-first.
        var history = (await svc.HistoryAsync(userId)).Where(h => h.Type == ConsentType.Marketing).ToList();
        Assert.Equal(2, history.Count);
        Assert.True(history[0].Granted);
        Assert.False(history[1].Granted);
    }

    [Fact]
    public async Task A_new_policy_version_reopens_a_previously_granted_required_consent()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);

        // Record a grant against an OLD version directly, then evaluate against the live catalog.
        db.ConsentRecords.Add(new ConsentRecord
        {
            UserId = userId,
            Type = ConsentType.PrivacyPolicy,
            Granted = true,
            PolicyVersion = "2020-01", // stale
            LawfulBasis = LawfulBasis.Consent,
            RecordedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var status = await new ConsentService(db, NewClock()).StatusAsync(userId);
        // Granted, but at a stale version → still needs action.
        Assert.True(Needs(status, ConsentType.PrivacyPolicy));
    }

    [Fact]
    public async Task Consent_is_owner_scoped()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var dbName = $"nf-{Guid.NewGuid()}";

        await using (var aliceDb = Db(alice, dbName))
        {
            await new ConsentService(aliceDb, NewClock()).RecordAsync(alice, ConsentType.Marketing, granted: true);
        }

        await using var bobDb = Db(bob, dbName);
        var bobHistory = await new ConsentService(bobDb, NewClock()).HistoryAsync(bob);
        Assert.Empty(bobHistory); // Bob sees none of Alice's consent records
    }
}
