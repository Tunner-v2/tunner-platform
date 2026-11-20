using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database;
using Tunner.Database.Audit;
using Tunner.Database.DurableWork;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class AuthenticationRejectionPostCommitTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealFailedSignInProducesOnlyAnonymousReferenceBoundAudit(bool knownAccount)
    {
        await using var h = await Create(knownAccount);
        var facts = Assert.IsType<IdentityPostCommitOwnerFacts>(await h.Owner.ReadCommittedAsync(h.Message, default));
        Assert.Null(facts.ActorAccountId); Assert.Null(facts.SessionReference);
        Assert.Equal(h.Message.Subject, facts.OwnerReference);
        Assert.Equal(h.Profile.Value!.Metadata.VersionId, facts.PolicyVersion);
        Assert.Equal(h.Message.OccurredAtUtc, h.Profile.ObservedAt);
        Assert.Contains("fixture-rejection-profile-approval", facts.References["auditProfileEvidenceReferences"], StringComparison.Ordinal);
        // Historic audit authority is selected at the committed event, not the later consumer's clock.
        h.Host.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(h.Profile.Value.Metadata.EffectiveInterval.Contains(h.Host.Clock.GetUtcNow()));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        await using var platform = h.Platform.CreateDbContext();
        var audit = Assert.Single(await platform.AuditEvents.ToListAsync()); audit.RequireIntegrity();
        using var envelope = JsonDocument.Parse(audit.Envelope);
        var actor = envelope.RootElement.GetProperty("actor");
        Assert.Equal("fixture-anonymous-authentication", actor.GetProperty("principal_id").GetString());
        Assert.Equal(h.Message.OperationId, audit.OperationId); Assert.Equal(h.Message.CorrelationId, audit.CorrelationId);
        Assert.Equal(h.Message.OccurredAtUtc, audit.OccurredAtUtc);
        Assert.NotNull((await platform.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
        await using var identity = h.Database.Context();
        Assert.Empty(await identity.Set<AuthenticationSessionRow>().ToListAsync());
        var outward = audit.Envelope + JsonSerializer.Serialize(facts) + h.Message.Payload;
        foreach (var secret in new[] { h.Account, "person@example.test", "unknown@example.test", Password, "fixture wrong password" })
            Assert.DoesNotContain(secret, outward, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("classification")]
    [InlineData("scope")]
    [InlineData("draft")]
    public async Task AbsentOrUnqualifiedHistoricalProfileCreatesNoAuditOrInbox(string condition)
    {
        await using var h = await Create(true);
        var prior = h.Profile.Value!;
        h.Profile.Value = condition switch
        {
            "missing" => null,
            "classification" => prior with { DataClassification = "fixture-other-classification" },
            _ => prior with { Metadata = new(prior.Metadata.VersionId, condition == "draft" ? "DRAFT" : "ACTIVE",
                condition == "scope" ? "fixture-unrelated-scope" : AuthenticationRejectionAuditProfile.Scope,
                condition == "expired" ? new(h.Message.OccurredAtUtc.AddMinutes(-1), true, h.Message.OccurredAtUtc, false) : prior.Metadata.EffectiveInterval,
                null, ["fixture-rejection-profile-approval"]) }
        };
        Assert.Null(await h.Owner.ReadCommittedAsync(h.Message, default));
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(h.Message));
        await AssertNoEffects(h);
    }

    [Fact]
    public async Task OwnerIndependentlyRejectsAlteredAndUncommittedOutboxWitnesses()
    {
        await using var h = await Create(false); var e = h.Message;
        foreach (var changed in new[]
        {
            e with { Source = "fixture-unrelated-source" }, e with { EnvelopeVersion = "2.0" },
            e with { EventSchemaVersion = "2" }, e with { ResourceVersion = "2" },
            e with { OperationId = "OP-" + Guid.NewGuid().ToString("N") },
            e with { CorrelationId = Guid.NewGuid().ToString("N") },
            e with { OccurredAtUtc = e.OccurredAtUtc.AddTicks(10) },
            e with { Payload = JsonSerializer.Serialize(new { reference = e.OperationId, account = "fixture-untrusted" }) }
        }) Assert.Null(await h.Owner.ReadCommittedAsync(changed, default));
        await using var producer = h.Database.Context(); await using var transaction = await producer.Database.BeginTransactionAsync();
        var staged = new OutboxMessage(Guid.NewGuid(), e.EnvelopeVersion, e.EventType, e.EventSchemaVersion, e.OccurredAtUtc,
            e.Source, e.Subject, e.DataClassification, e.Payload, e.ResourceVersion, e.OperationId, e.CorrelationId);
        producer.Add(staged); await producer.SaveChangesAsync();
        Assert.Null(await h.Owner.ReadCommittedAsync(IdentityPostCommitProcessor.Project(staged), default));
        await transaction.RollbackAsync(); await AssertNoEffects(h);
    }

    [Fact]
    public async Task HistoricalOwnerProfileDoesNotReplaceCurrentGenericAuditQualification()
    {
        await using var h = await Create(true); h.Contracts.Available = false;
        Assert.NotNull(await h.Owner.ReadCommittedAsync(h.Message, default));
        Assert.Equal(IdentityPostCommitOutcome.Unavailable, await h.Processor.ProcessAsync(h.Message));
        await AssertNoEffects(h);
    }

    private async Task<Scenario> Create(bool knownAccount)
    {
        var database = await fixture.CreateDatabaseAsync(); AuthenticationTestHost? host = null;
        try
        {
            host = await Start(database); var account = await host.SeedAsync(database);
            var bootstrap = await host.Options();
            using var rejected = await host.Post("/authentication/sign-in",
                Command(bootstrap.Key, password: "fixture wrong password", email: knownAccount ? "person@example.test" : "unknown@example.test"),
                bootstrap.Cookies, bootstrap.Csrf);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            await using var read = database.Context();
            var message = IdentityPostCommitProcessor.Project(await read.Set<OutboxMessage>().AsNoTracking().SingleAsync(x => x.EventType == "AuthenticationRejected"));
            var profile = new Profile { Value = new(new("fixture-rejection-profile-v1", "ACTIVE", AuthenticationRejectionAuditProfile.Scope,
                new(message.OccurredAtUtc.AddMinutes(-1), true, message.OccurredAtUtc.AddMinutes(1), false), null, ["fixture-rejection-profile-approval"]),
                "fixture-rejection-processing", "fixture-rejection-retention", "RESTRICTED_SECURITY") };
            var owner = new AuthenticationRejectionPostCommitOwnerSource(new Factory<IdentityPersistenceDbContext>(database.Context), profile);
            var platform = new Factory<TunnerMigrationsDbContext>(() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(database.ConnectionString).Options));
            var contracts = new Contracts(host.Clock);
            var processor = new IdentityPostCommitProcessor(new Factory<IdentityPersistenceDbContext>(database.Context), platform,
                new EfConsumerInboxStore(platform), [owner], contracts, new("fixture-rejected-sign-in", "fixture-generation-v1", TimeSpan.FromMinutes(1)), host.Clock);
            return new(database, host, account, message, profile, owner, platform, contracts, processor);
        }
        catch { if (host is not null) await host.DisposeAsync(); await database.DisposeAsync(); throw; }
    }

    private static async Task AssertNoEffects(Scenario h)
    {
        await using var db = h.Platform.CreateDbContext();
        Assert.Empty(await db.AuditEvents.ToListAsync()); Assert.Empty(await db.ConsumerInboxEntries.ToListAsync());
    }
    private sealed class Profile : IAuthenticationRejectionAuditProfileSource
    {
        internal AuthenticationRejectionAuditProfile? Value;
        internal DateTimeOffset? ObservedAt;
        public AuthenticationRejectionAuditProfile? RequireEffective(DateTimeOffset eventOccurredAt) { ObservedAt = eventOccurredAt; return Value; }
    }
    private sealed class Contracts(TestClock clock) : IIdentityPostCommitContractSource
    {
        internal bool Available = true;
        public ValueTask<IdentityPostCommitQualification?> RequireCurrentAsync(IdentityPostCommitFacts facts, DateTimeOffset at, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Available) return ValueTask.FromResult<IdentityPostCommitQualification?>(null);
            var e = facts.Event;
            var audit = new AuditEvent(e.EventId, e.OccurredAtUtc, new("fixture-anonymous-authentication", "fixture-anonymous", []),
                "fixture-authentication-rejected", e.OperationId!, e.CorrelationId!, [new(e.Subject, e.ResourceVersion)], "fixture-rejected",
                e.DataClassification, facts.Owner.RetentionReference, e.EventId.ToString("N"),
                policyVersionReferences: ["fixture-current-audit-contract", facts.Owner.PolicyVersion], evidenceReferences: ["fixture-current-audit-approval"]);
            return ValueTask.FromResult<IdentityPostCommitQualification?>(new(e.EventId, facts.Digest, "fixture-current-audit-contract", "fixture-current-audit-approval",
                new(clock.GetUtcNow().AddMinutes(-1), true, clock.GetUtcNow().AddMinutes(1), false), audit));
        }
    }
    private sealed class Factory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => create();
        public Task<T> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }
    private sealed class Scenario(IdentityTestDatabase database, AuthenticationTestHost host, string account, PendingOutboxMessage message,
        Profile profile, AuthenticationRejectionPostCommitOwnerSource owner, Factory<TunnerMigrationsDbContext> platform,
        Contracts contracts, IdentityPostCommitProcessor processor) : IAsyncDisposable
    {
        internal IdentityTestDatabase Database { get; } = database;
        internal AuthenticationTestHost Host { get; } = host;
        internal string Account { get; } = account;
        internal PendingOutboxMessage Message { get; } = message;
        internal Profile Profile { get; } = profile;
        internal AuthenticationRejectionPostCommitOwnerSource Owner { get; } = owner;
        internal Factory<TunnerMigrationsDbContext> Platform { get; } = platform;
        internal Contracts Contracts { get; } = contracts;
        internal IdentityPostCommitProcessor Processor { get; } = processor;
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Database.DisposeAsync(); }
    }
}
