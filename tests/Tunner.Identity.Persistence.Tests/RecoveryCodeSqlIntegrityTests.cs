using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tunner.Accounts;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// PUBLIC NONFUNCTIONAL METADATA ONLY. Root-approved isolated PostgreSQL required; no valid native verifier/code/session ticket.
[Trait("Boundary", "RecoverySqlPublicMetadata")]
public sealed class RecoveryCodeSqlIntegrityTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string Public = "PUBLIC-NONFUNCTIONAL";
    private sealed record Seed(string Account, Guid Operation, Guid Set, Guid Entry)
    {
        public override string ToString() => nameof(Seed);
    }
    private static RecoveryCodeOperationRow Operation(string account, Guid session) => new()
    {
        OperationId = Guid.NewGuid(), RequestId = Guid.NewGuid(), SetReference = Guid.NewGuid(), AccountId = account, SessionId = session,
        Origin = "https://public-recovery.example.test", Kind = "GENERATE", State = "PENDING_PROOF", ExpectedVersion = 1,
        ContextDigest = new('A', 64), PriorSetPlanDigest = new('A', 64), ImpactFactsDigest = new('A', 64), PolicyVersion = Public, PolicyDigest = new('A', 64),
        AuthenticationPolicyVersion = Public, SecurityStampDigest = new('B', 64), CryptoProfileVersion = Public, FormatProfileVersion = "NATIVE_HEX32_V1", DeliveryProfileVersion = Public,
        CodesPerSet = 1, MaximumCodesPerSet = 1, RetirementPlanJson = "[]", PublishedImpactKey = Public, PublicationDigest = new('A', 64), PublicationVersion = Public,
        PublicationFrom = At, PublicationIncludesFrom = true, PublicationUntil = At.AddMinutes(5), PublicationIncludesUntil = false,
        AdmissionPolicyVersion = Public, AdmissionEvidenceReference = Public, AdmissionFrom = At, AdmissionIncludesFrom = true,
        AdmissionUntil = At.AddMinutes(5), AdmissionIncludesUntil = false, AssurancePolicyVersion = Public, CreatedAt = At, ExpiresAt = At.AddMinutes(5),
        ResultRecoveryExpiresAt = At.AddMinutes(10), ProcessingReference = Public, RetentionReference = Public
    };
    private static RecoveryCodeSetRow Set(string account, Guid reference, Guid operation) => new()
    {
        SetReference = reference, AccountId = account, IssuanceOperationId = operation, State = "ACTIVE", CreatedAt = At,
        PolicyVersion = Public, CryptoProfileVersion = Public, ProcessingReference = Public, RetentionReference = Public
    };
    private static async Task<Seed> SeedAsync(IdentityTestDatabase db, bool issue = true)
    {
        await using var context = db.Context(); var account = "ACC-" + Guid.NewGuid().ToString("N"); var session = Guid.NewGuid();
        context.Accounts.Add(new AccountRow { AccountId = account, State = AccountState.Active, Version = 1, RecordedAtUtc = At, OperationId = Public, CorrelationId = Public, EvidenceReference = Public });
        context.Add(new AuthenticationSessionRow { SessionId = session, AccountId = account, KeyDigest = AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N")), BrowserDigest = new('A', 64),
            IdempotencyKey = Guid.NewGuid(), IdentifierDigest = new('A', 64), SecurityStampDigest = new('B', 64), ProtectedKey = Public, ProtectedTicket = Public,
            Scheme = AuthenticationIdentity.Scheme, Origin = "https://public-recovery.example.test", AuthenticationMethods = "pwd", Assurance = Public,
            AuthenticatedAt = At, LastSeenAt = At, ExpiresAt = At.AddMinutes(30), IdleTicks = TimeSpan.FromMinutes(5).Ticks, Version = 1,
            PolicyVersion = Public, AccessPolicyVersion = Public, AccessEvidenceReference = Public, ProcessingReference = Public, RetentionReference = Public, OperationId = Public, CorrelationId = Public });
        var operation = Operation(account, session); context.Add(operation); await context.SaveChangesAsync();
        var entry = Guid.NewGuid();
        if (issue)
        {
            context.Add(Set(account, operation.SetReference, operation.OperationId));
            context.Add(new RecoveryCodeEntryRow { AccountId = account, SetReference = operation.SetReference, EntryReference = entry, Verifier = new('A', 84), CryptoProfileVersion = Public });
            await context.SaveChangesAsync();
        }
        return new(account, operation.OperationId, operation.SetReference, entry);
    }
    private static async Task<bool> Rejected(Func<Task> mutation, string sqlState)
    {
        try { await mutation(); return false; }
        catch (DbUpdateException error) when (error.InnerException is PostgresException pg) { return pg.SqlState == sqlState; }
        catch (PostgresException error) { return error.SqlState == sqlState; }
    }
    private static async Task<bool> RejectSet(IdentityTestDatabase db, RecoveryCodeSetRow set)
    {
        await using var context = db.Context(); context.Add(set); return await Rejected(async () => { _ = await context.SaveChangesAsync(); }, "23503");
    }

    [Fact]
    public async Task IssuanceRejectsForeignOwnerWrongPlannedSetAndOrphanOperation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var first = await SeedAsync(db, false); var foreign = await SeedAsync(db, false);
        Assert.True(await RejectSet(db, Set(foreign.Account, first.Set, first.Operation)));
        Assert.True(await RejectSet(db, Set(first.Account, Guid.NewGuid(), first.Operation)));
        Assert.True(await RejectSet(db, Set(first.Account, first.Set, Guid.NewGuid())));
        await using var check = db.Context(); Assert.True(!await check.Set<RecoveryCodeSetRow>().AnyAsync());
    }

    [Fact]
    public async Task RetirementRejectsForeignOwnerAndOrphanCause()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var first = await SeedAsync(db); var foreign = await SeedAsync(db, false);
        foreach (var cause in new[] { foreign.Operation, Guid.NewGuid() })
        {
            await using var context = db.Context(); var set = await context.Set<RecoveryCodeSetRow>().SingleAsync(x => x.SetReference == first.Set);
            set.State = "REPLACED"; set.LifecycleVersion++; set.RetiredAt = At; set.RetiredByOperationId = cause;
            Assert.True(await Rejected(async () => { _ = await context.SaveChangesAsync(); }, "23503"));
        }
        await using var check = db.Context(); Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.State == "ACTIVE" && x.LifecycleVersion == 1));
    }

    private static async Task ConsumePublicMetadata(IdentityTestDatabase db, Seed seed)
    {
        await using var context = db.Context();
        var entry = await context.Set<RecoveryCodeEntryRow>().SingleAsync(x => x.EntryReference == seed.Entry);
        entry.ConsumedAt = At; entry.ConsumedByOperationId = Public; entry.ConsumptionCorrelationId = Public; entry.ConsumptionContextDigest = new('A', 64);
        entry.ConsumptionPolicyVersion = Public; entry.ConsumptionOwnerPolicyVersion = Public; entry.ConsumptionOutcomeReference = Public; entry.ConsumptionSetLifecycleVersion = 1; entry.Version = 2;
        var set = await context.Set<RecoveryCodeSetRow>().SingleAsync(x => x.SetReference == seed.Set); set.UsageVersion++;
        await context.SaveChangesAsync();
    }
    [Fact]
    public async Task ConsumedEntryCannotRearmRotateVerifierOrRewriteOwnerOutcome()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var seed = await SeedAsync(db); await ConsumePublicMetadata(db, seed);
        foreach (var mutation in new[] { "rearm", "verifier", "owner", "outcome", "lifecycle" })
        {
            await using var context = db.Context(); var entry = await context.Set<RecoveryCodeEntryRow>().SingleAsync(x => x.EntryReference == seed.Entry);
            if (mutation == "rearm") { entry.ConsumedAt = null; entry.ConsumedByOperationId = null; entry.ConsumptionCorrelationId = null; entry.ConsumptionContextDigest = null;
                entry.ConsumptionPolicyVersion = null; entry.ConsumptionOwnerPolicyVersion = null; entry.ConsumptionOutcomeReference = null; entry.ConsumptionSetLifecycleVersion = null; entry.Version = 1; }
            else if (mutation == "verifier") entry.Verifier = new('B', 84);
            else if (mutation == "owner") entry.ConsumedByOperationId = "PUBLIC-OTHER";
            else if (mutation == "outcome") entry.ConsumptionOutcomeReference = "PUBLIC-OTHER";
            else entry.ConsumptionSetLifecycleVersion = 2;
            Assert.True(await Rejected(async () => { _ = await context.SaveChangesAsync(); }, "23514"));
        }
    }

    [Fact]
    public async Task SetUsageCannotRewindOrBecomeLifecycleAndRetirementIsTerminal()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var seed = await SeedAsync(db); await ConsumePublicMetadata(db, seed);
        await using (var context = db.Context())
        {
            var set = await context.Set<RecoveryCodeSetRow>().SingleAsync(x => x.SetReference == seed.Set); set.UsageVersion = 0;
            Assert.True(await Rejected(async () => { _ = await context.SaveChangesAsync(); }, "23514"));
        }
        await using (var context = db.Context())
        {
            var set = await context.Set<RecoveryCodeSetRow>().SingleAsync(x => x.SetReference == seed.Set); set.LifecycleVersion++;
            Assert.True(await Rejected(async () => { _ = await context.SaveChangesAsync(); }, "23514"));
        }
        await using (var context = db.Context())
        {
            var session = await context.Set<AuthenticationSessionRow>().SingleAsync(x => x.AccountId == seed.Account); var replacement = Operation(seed.Account, session.SessionId); replacement.Kind = "REPLACE";
            context.Add(replacement); await context.SaveChangesAsync();
            var set = await context.Set<RecoveryCodeSetRow>().SingleAsync(x => x.SetReference == seed.Set); set.State = "REPLACED"; set.LifecycleVersion++; set.RetiredAt = At; set.RetiredByOperationId = replacement.OperationId; await context.SaveChangesAsync();
        }
        await using (var context = db.Context())
        {
            var set = await context.Set<RecoveryCodeSetRow>().SingleAsync(x => x.SetReference == seed.Set); set.State = "ACTIVE"; set.LifecycleVersion++; set.RetiredAt = null; set.RetiredByOperationId = null;
            Assert.True(await Rejected(async () => { _ = await context.SaveChangesAsync(); }, "23514"));
        }
    }

    [Fact]
    public async Task TerminalOperationCannotReopenOrRewriteContextPublicationOrFirstResponse()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var seed = await SeedAsync(db, false);
        await using (var context = db.Context())
        {
            var op = await context.Set<RecoveryCodeOperationRow>().SingleAsync(x => x.OperationId == seed.Operation);
            op.State = "COMPLETED"; op.CompletedAt = At; op.FirstResponseCommittedAt = At; op.ProofReference = Guid.NewGuid(); op.Version = 2; await context.SaveChangesAsync();
        }
        foreach (var mutation in new[] { "reopen", "context", "publication", "marker" })
        {
            await using var context = db.Context(); var op = await context.Set<RecoveryCodeOperationRow>().SingleAsync(x => x.OperationId == seed.Operation); op.Version++;
            if (mutation == "reopen") { op.State = "PENDING_PROOF"; op.CompletedAt = null; op.FirstResponseCommittedAt = null; op.ProofReference = null; }
            else if (mutation == "context") op.ContextDigest = new('B', 64);
            else if (mutation == "publication") op.PublicationDigest = new('B', 64);
            else op.FirstResponseCommittedAt = At.AddSeconds(1);
            Assert.True(await Rejected(async () => { _ = await context.SaveChangesAsync(); }, "23514"));
        }
    }
}
