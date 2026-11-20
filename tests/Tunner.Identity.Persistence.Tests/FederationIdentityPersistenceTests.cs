using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tunner.Accounts;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Public synthetic persistence groundwork only; no provider proof, session or linking authorization.</summary>
// Deliberately not ordinary xUnit-dispatched: only a future reviewed fixed registration may supply the finite owner.
internal sealed class FederationIdentityPersistenceTests(IdentityPostgresFixture fixture)
{
    private static AccountRow Account() => new()
    {
        AccountId = "ACC-" + Guid.NewGuid().ToString("N"), State = AccountState.VerificationPending, Version = 1,
        RecordedAtUtc = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), OperationId = "synthetic-federation-account",
        CorrelationId = "synthetic-federation-correlation", EvidenceReference = "SYNTHETIC-TEST-ONLY"
    };

    private static FederationIdentityKey Key(string client = "synthetic-client-a", string subject = "OpaqueSubject",
        string ns = "synthetic-stable-registration-a", string protocolIssuer = "https://issuer.example.test") =>
        new FederationProviderRegistration(FederationProviderKind.Google, ns, "https://issuer.example.test", client,
            ["https://alias.example.test"], "synthetic-profile-v1", "synthetic-explicit-issuer-map", 2048, 128)
            .BindExternalIdentity(protocolIssuer, client, subject);

    public async Task ExactRegistrationNamespacesRoundTripWithoutCrossClientOrSubjectFolding()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var first = Account(); var second = Account();
        var a = Key(); var alias = Key(protocolIssuer: "https://alias.example.test");
        var b = Key(client: "synthetic-client-b", ns: "synthetic-stable-registration-b"); var c = Key(subject: "opaquesubject");
        await using (var owner = db.Context())
        {
            owner.Accounts.AddRange(first, second);
            owner.ExternalIdentityLinks.AddRange(a.PrepareOwnedLink(Guid.NewGuid(), first.AccountId, "synthetic-a"),
                b.PrepareOwnedLink(Guid.NewGuid(), second.AccountId, "synthetic-b"),
                c.PrepareOwnedLink(Guid.NewGuid(), first.AccountId, "synthetic-c"));
            await owner.SaveChangesAsync();
        }
        await using var read = db.Context();
        var one = await read.ExternalIdentityLinks.SingleAsync(x => x.Provider == a.ProviderNamespace && x.Subject == a.Subject);
        var two = await read.ExternalIdentityLinks.SingleAsync(x => x.Provider == b.ProviderNamespace && x.Subject == b.Subject);
        var three = await read.ExternalIdentityLinks.SingleAsync(x => x.Provider == c.ProviderNamespace && x.Subject == c.Subject);
        var sameIdentity = await read.ExternalIdentityLinks.SingleAsync(x => x.Provider == alias.ProviderNamespace && x.Subject == alias.Subject);
        Assert.Equal(first.AccountId, one.AccountId); Assert.Equal(second.AccountId, two.AccountId); Assert.Equal(first.AccountId, three.AccountId);
        Assert.NotEqual(one.LinkId, two.LinkId); Assert.NotEqual(one.LinkId, three.LinkId);
        Assert.Equal(one.LinkId, sameIdentity.LinkId);
        Assert.Equal(3, await read.ExternalIdentityLinks.CountAsync());
    }

    public async Task ConcurrentExactIdentityHasOneOwnerAndCannotRewriteThatOwner()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var a = Account(); var b = Account(); var key = Key();
        await using (var seed = db.Context()) { seed.Accounts.AddRange(a, b); await seed.SaveChangesAsync(); }
        await using var first = db.Context(); await using var second = db.Context();
        first.ExternalIdentityLinks.Add(key.PrepareOwnedLink(Guid.NewGuid(), a.AccountId, "synthetic-first"));
        second.ExternalIdentityLinks.Add(key.PrepareOwnedLink(Guid.NewGuid(), b.AccountId, "synthetic-second"));
        var results = await Task.WhenAll(TryInsert(first), TryInsert(second));
        Assert.Single(results, value => value);
        await using var read = db.Context();
        var winner = await read.ExternalIdentityLinks.AsNoTracking().SingleAsync();
        var original = winner.AccountId;
        var otherOwner = original == a.AccountId ? b.AccountId : a.AccountId;
        // AccountId is also an EF alternate-key member. Bypass EF's earlier key-mutation rejection
        // to prove that PostgreSQL itself preserves the immutable winner against a direct write.
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => read.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE identity.external_identity_links SET account_id={otherOwner}, version=version+1 WHERE link_id={winner.LinkId}"));
        Assert.Equal("23514", rejected.SqlState);
        await using var final = db.Context();
        Assert.Equal(original, (await final.ExternalIdentityLinks.SingleAsync()).AccountId);
    }

    public async Task OwnerRollbackLeavesNoPartialIdentityReservation()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var account = Account(); var key = Key(); var id = Guid.NewGuid();
        await using (var seed = db.Context()) { seed.Accounts.Add(account); await seed.SaveChangesAsync(); }
        await using (var owner = db.Context())
        {
            await using var transaction = await owner.Database.BeginTransactionAsync();
            owner.ExternalIdentityLinks.Add(key.PrepareOwnedLink(id, account.AccountId, "synthetic-rolled-back"));
            await owner.SaveChangesAsync(); await transaction.RollbackAsync();
        }
        await using (var read = db.Context()) Assert.Empty(await read.ExternalIdentityLinks.ToArrayAsync());
        await using (var owner = db.Context())
        {
            owner.ExternalIdentityLinks.Add(key.PrepareOwnedLink(id, account.AccountId, "synthetic-original"));
            await owner.SaveChangesAsync();
        }
        await using var final = db.Context(); var committed = await final.ExternalIdentityLinks.SingleAsync();
        Assert.Equal(id, committed.LinkId); Assert.Equal(1, committed.Version); Assert.Equal("synthetic-original", committed.EvidenceReference);
    }

    public async Task PersistedSubjectIsImmutableAndEvidenceUpdatesDetectStaleVersion()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var account = Account(); var key = Key();
        await using (var seed = db.Context())
        {
            seed.Accounts.Add(account); seed.ExternalIdentityLinks.Add(key.PrepareOwnedLink(Guid.NewGuid(), account.AccountId, "synthetic-initial"));
            await seed.SaveChangesAsync();
        }
        await using (var changed = db.Context())
        {
            var row = await changed.ExternalIdentityLinks.SingleAsync(); row.Subject = "different"; row.Version++;
            var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => changed.SaveChangesAsync());
            Assert.Equal("23514", Assert.IsType<PostgresException>(rejected.InnerException).SqlState);
        }
        await using var first = db.Context(); await using var second = db.Context();
        var a = await first.ExternalIdentityLinks.SingleAsync(); var b = await second.ExternalIdentityLinks.SingleAsync();
        a.EvidenceReference = "synthetic-current"; a.Version++;
        b.EvidenceReference = "synthetic-stale"; b.Version++;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var final = db.Context(); var current = await final.ExternalIdentityLinks.SingleAsync();
        Assert.Equal(key.Subject, current.Subject); Assert.Equal(2, current.Version); Assert.Equal("synthetic-current", current.EvidenceReference);
    }

    private static async Task<bool> TryInsert(IdentityPersistenceDbContext database)
    {
        try { await database.SaveChangesAsync(); return true; }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" }) { return false; }
    }
}
