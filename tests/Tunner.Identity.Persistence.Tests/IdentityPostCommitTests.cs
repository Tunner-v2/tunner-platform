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

public sealed class IdentityPostCommitTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task CommittedPasswordEventProducesOneBoundAuditAndSafeIntentBeforeInboxCompletion()
    {
        await using var h = await Harness.Start(fixture);
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        await using var platform = h.Platform.CreateDbContext();
        var audit = await platform.AuditEvents.SingleAsync(); audit.RequireIntegrity();
        using var json = JsonDocument.Parse(audit.Envelope); var a = json.RootElement;
        Assert.Equal(h.Account, a.GetProperty("actor").GetProperty("principal_id").GetString());
        Assert.Equal(h.Message.OperationId, audit.OperationId); Assert.Equal(h.Message.CorrelationId, audit.CorrelationId);
        Assert.Equal(h.Message.EventId.ToString("N"), a.GetProperty("causation_event_id").GetString());
        Assert.Equal(h.Message.EventId, Assert.Single(h.Dispatcher.Intents).Key);
        Assert.NotNull((await platform.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
        var outward = audit.Envelope + JsonSerializer.Serialize(h.Dispatcher.Intents.Values);
        foreach (var secret in new[] { Password, "person@example.test", "ProtectedTicket", "ProtectedKey", "SecurityStampDigest", "IdentifierDigest" })
            Assert.DoesNotContain(secret, outward, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompletedDuplicateAcknowledgesExactEventWithoutCurrentContractOrOwner()
    {
        await using var h = await Harness.Start(fixture);
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        h.Contracts.Unavailable = true;
        var duplicateProcessor = new IdentityPostCommitProcessor(h.Identity, h.Platform, new EfConsumerInboxStore(h.Platform), [], h.Contracts,
            new("fixture-identity-security", "fixture-generation-v1", TimeSpan.FromMinutes(1)), h.Host.Clock, h.Dispatcher);
        Assert.Equal(IdentityPostCommitOutcome.Completed, await duplicateProcessor.ProcessAsync(h.Message));
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await duplicateProcessor.ProcessAsync(h.Message with { CorrelationId = "forged" }));
        Assert.Equal(1, h.Dispatcher.Attempts);
        await using var platform = h.Platform.CreateDbContext(); Assert.Single(await platform.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task AnotherWorkersActiveReservationDefersWithoutAcknowledgementOrEffects()
    {
        await using var h = await Harness.Start(fixture);
        var consumer = new IdentityPostCommitConsumer("fixture-identity-security", "fixture-generation-v1", TimeSpan.FromMinutes(1));
        var token = Guid.NewGuid(); var at = h.Host.Clock.GetUtcNow();
        Assert.True(await new EfConsumerInboxStore(h.Platform).TryReserveAsync(consumer.Key, h.Message.EventId, token, at, at + consumer.LeaseDuration));
        h.Contracts.Unavailable = true;
        Assert.Equal(IdentityPostCommitOutcome.Deferred, await h.Processor.ProcessAsync(h.Message));
        await using var platform = h.Platform.CreateDbContext(); var receipt = await platform.ConsumerInboxEntries.SingleAsync();
        Assert.Equal(token, receipt.LeaseToken); Assert.Null(receipt.CompletedAtUtc);
        Assert.Empty(await platform.AuditEvents.ToListAsync()); Assert.Empty(h.Dispatcher.Intents); Assert.Equal(0, h.Dispatcher.Attempts);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("operation")]
    [InlineData("correlation")]
    [InlineData("classification")]
    [InlineData("version")]
    [InlineData("time")]
    [InlineData("type")]
    public async Task TransportMustMatchTheEntireCommittedProducerEnvelope(string changed)
    {
        await using var h = await Harness.Start(fixture); var e = h.Message;
        var forged = changed switch
        {
            "payload" => e with { Payload = "{\"reference\":\"forged\",\"password\":\"must-not-leak\"}" },
            "operation" => e with { OperationId = "OP-forged" },
            "correlation" => e with { CorrelationId = "forged" },
            "classification" => e with { DataClassification = "PUBLIC" },
            "version" => e with { ResourceVersion = "99" },
            "time" => e with { OccurredAtUtc = e.OccurredAtUtc.AddTicks(10) },
            _ => e with { EventType = "IdentitySecurityNotificationRequested" }
        };
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(forged));
        await h.AssertNoEffects();
    }

    [Fact]
    public async Task TrackedUncommittedProducerEventIsNotEligible()
    {
        await using var h = await Harness.Start(fixture);
        await using var owner = h.Identity.CreateDbContext(); await using var tx = await owner.Database.BeginTransactionAsync();
        var e = h.Message; var staged = new OutboxMessage(Guid.NewGuid(), e.EnvelopeVersion, e.EventType, e.EventSchemaVersion,
            e.OccurredAtUtc, e.Source, e.Subject, e.DataClassification, e.Payload, e.ResourceVersion, e.OperationId, e.CorrelationId);
        owner.Add(staged); await owner.SaveChangesAsync();
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(IdentityPostCommitProcessor.Project(staged)));
        await h.AssertNoEffects(); await tx.RollbackAsync();
    }

    [Fact]
    public async Task HandoffFailureRedeliveryReusesExactAuditAndIntentIdentity()
    {
        await using var h = await Harness.Start(fixture); h.Dispatcher.FailNext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Processor.ProcessAsync(h.Message));
        await using (var platform = h.Platform.CreateDbContext())
        {
            Assert.Single(await platform.AuditEvents.ToListAsync());
            Assert.Null((await platform.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
        }
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        Assert.Equal(2, h.Dispatcher.Attempts); Assert.Single(h.Dispatcher.Intents);
        await using var final = h.Platform.CreateDbContext(); Assert.Single(await final.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task AuditFailureCannotQueueOrCompleteAndRemainsRetryable()
    {
        await using var h = await Harness.Start(fixture); h.Platform.FailAfterSuccessfulCreates = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Processor.ProcessAsync(h.Message));
        Assert.Equal(0, h.Dispatcher.Attempts);
        await using (var platform = h.Platform.CreateDbContext())
        { Assert.Empty(await platform.AuditEvents.ToListAsync()); Assert.Null((await platform.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc); }
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
    }

    [Fact]
    public async Task LeaseExpiryAfterQueueDoesNotCompleteAndRedeliveryDeduplicatesDurableIntent()
    {
        await using var h = await Harness.Start(fixture);
        h.Dispatcher.AfterQueue = () => h.Host.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(IdentityPostCommitOutcome.LeaseLost, await h.Processor.ProcessAsync(h.Message));
        await using (var platform = h.Platform.CreateDbContext()) Assert.Null((await platform.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
        h.Dispatcher.AfterQueue = null;
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        Assert.Equal(2, h.Dispatcher.Attempts); Assert.Single(h.Dispatcher.Intents);
        await using var final = h.Platform.CreateDbContext(); Assert.Single(await final.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task LeaseTokenReplacedDuringAuditCannotQueueOrCompleteAnotherWorkersReservation()
    {
        await using var h = await Harness.Start(fixture); var replacementToken = Guid.NewGuid();
        var consumer = new IdentityPostCommitConsumer("fixture-identity-security", "fixture-generation-v1", TimeSpan.FromMinutes(1));
        h.Platform.Configure = context => context.SavedChanges += (_, _) =>
        {
            // Inject competing persisted lease ownership at the audit boundary without advancing the decision clock.
            // This discriminates the stored-token guard from checking only our originally calculated expiry.
            using var other = h.Platform.CreateDbContext();
            Assert.Equal(1, other.ConsumerInboxEntries.Where(x => x.ConsumerKey == consumer.Key && x.MessageId == h.Message.EventId)
                .ExecuteUpdate(setters => setters.SetProperty(x => x.LeaseToken, (Guid?)replacementToken)));
        };
        Assert.Equal(IdentityPostCommitOutcome.LeaseLost, await h.Processor.ProcessAsync(h.Message));
        Assert.Equal(0, h.Dispatcher.Attempts); Assert.Empty(h.Dispatcher.Intents);
        await using var platform = h.Platform.CreateDbContext(); var receipt = await platform.ConsumerInboxEntries.SingleAsync();
        Assert.Equal(replacementToken, receipt.LeaseToken); Assert.Null(receipt.CompletedAtUtc);
        Assert.Single(await platform.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task ChangedRetryTemplateCannotReuseAnExistingAuditIdentity()
    {
        await using var h = await Harness.Start(fixture); h.Dispatcher.FailNext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Processor.ProcessAsync(h.Message));
        h.Contracts.TemplateVersion = "fixture-template-v2";
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(h.Message));
        Assert.Equal(1, h.Dispatcher.Attempts); Assert.Empty(h.Dispatcher.Intents);
    }

    [Fact]
    public async Task AuditActorMustBeTheCommittedOwnerActor()
    {
        await using var h = await Harness.Start(fixture); h.Contracts.WrongActor = true;
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(h.Message)); await h.AssertNoEffects();
    }

    [Fact]
    public async Task CurrentCredentialAndAccountVersionChangesDoNotEraseHistoricalAuthenticationFacts()
    {
        await using var h = await Harness.Start(fixture);
        await using (var db = h.Identity.CreateDbContext())
        {
            var user = await db.Users.SingleAsync(x => x.AccountId == h.Account); user.SecurityStamp = Guid.NewGuid().ToString("N");
            var account = await db.Accounts.SingleAsync(x => x.AccountId == h.Account); account.Version++; await db.SaveChangesAsync();
        }
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
    }

    [Fact]
    public async Task StaleOwnerNotificationCompletesWithoutClaimingQueueAcceptanceOrDelivery()
    {
        await using var h = await Harness.Start(fixture); h.Dispatcher.NoLongerDispatchable = true;
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        Assert.Empty(h.Dispatcher.Intents); Assert.Equal(1, h.Dispatcher.Attempts);
        await using var platform = h.Platform.CreateDbContext(); Assert.NotNull((await platform.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task MissingQualifiedContractNeverDefaultsToNotificationOrAuditPolicy()
    {
        await using var h = await Harness.Start(fixture); h.Contracts.Unavailable = true;
        Assert.Equal(IdentityPostCommitOutcome.Unavailable, await h.Processor.ProcessAsync(h.Message)); await h.AssertNoEffects();
    }

    [Theory]
    [InlineData("passkey")]
    [InlineData("totp")]
    public async Task RealAuthenticatorProducerIntentsResolveTypedCommittedOwnerAndDispatchOnce(string family)
    {
        await using var h = await Harness.Start(fixture, family);
        Assert.Equal("IdentitySecurityNotificationRequested", h.Message.EventType);
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(h.Message));
        await using var db = h.Platform.CreateDbContext(); var audit = await db.AuditEvents.SingleAsync(); audit.RequireIntegrity();
        Assert.Equal(h.Message.OperationId, audit.OperationId); Assert.Equal(h.Message.CorrelationId, audit.CorrelationId);
        Assert.Single(h.Dispatcher.Intents);
    }

    private sealed class Factory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        internal int? FailAfterSuccessfulCreates;
        internal Action<T>? Configure;
        public T CreateDbContext()
        {
            if (FailAfterSuccessfulCreates == 0)
            { FailAfterSuccessfulCreates = null; throw new InvalidOperationException("Synthetic audit availability failure."); }
            if (FailAfterSuccessfulCreates is { } remaining) FailAfterSuccessfulCreates = remaining - 1;
            var context = create(); Configure?.Invoke(context); return context;
        }
        public Task<T> CreateDbContextAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }

    private sealed class Contracts(TestClock clock) : IIdentityPostCommitContractSource
    {
        internal bool WrongActor, Unavailable;
        internal string TemplateVersion = "fixture-template-v1";
        public ValueTask<IdentityPostCommitQualification?> RequireCurrentAsync(IdentityPostCommitFacts facts, DateTimeOffset at, CancellationToken cancellationToken)
        {
            var f = facts; var ct = cancellationToken;
            ct.ThrowIfCancellationRequested(); if (Unavailable) return ValueTask.FromResult<IdentityPostCommitQualification?>(null);
            const string version = "fixture-contract-v1", evidence = "fixture-contract-approval";
            var e = f.Event;
            var audit = new AuditEvent(e.EventId, e.OccurredAtUtc, new AuditActor(WrongActor ? "ACC-forged" : f.Owner.ActorAccountId!, "fixture-account-actor", [f.Owner.SessionReference!]),
                "fixture-authentication-recorded", e.OperationId!, e.CorrelationId!, [new(e.Subject, e.ResourceVersion)], "fixture-committed",
                e.DataClassification, "fixture-audit-retention", e.EventId.ToString("N"), policyVersionReferences: [version], evidenceReferences: [evidence]);
            var notice = new IdentitySecurityNotification(e.EventId, e.EventId, f.Digest, version, "fixture-security-class", TemplateVersion,
                "fixture-published-content-v1", "en", "fixture-verified-contact-reference", "fixture-recipient-safety-evidence", "fixture-route-resolution",
                "fixture-processing", "fixture-retention", evidence);
            return ValueTask.FromResult<IdentityPostCommitQualification?>(new(e.EventId, f.Digest, version, evidence,
                new EffectivePolicyInterval(clock.GetUtcNow().AddDays(-1), true, clock.GetUtcNow().AddDays(1), false), audit, notice));
        }
    }

    private sealed class Dispatcher(Factory<TunnerMigrationsDbContext> platform) : IIdentitySecurityNotificationDispatcher
    {
        internal readonly Dictionary<Guid, IdentitySecurityNotification> Intents = [];
        internal int Attempts;
        internal bool FailNext, NoLongerDispatchable;
        internal Action? AfterQueue;
        public async Task<IdentitySecurityNotificationOutcome> QueueAsync(IdentitySecurityNotification notification, CancellationToken cancellationToken)
        {
            var n = notification; var ct = cancellationToken;
            Attempts++;
            await using var db = platform.CreateDbContext(); var audit = await db.AuditEvents.SingleAsync(x => x.EventId == n.SourceEventId, ct); audit.RequireIntegrity();
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Synthetic notification handoff unavailable."); }
            if (NoLongerDispatchable) return IdentitySecurityNotificationOutcome.NoLongerDispatchable;
            if (Intents.TryGetValue(n.IntentId, out var prior)) Assert.Equal(prior, n); else Intents.Add(n.IntentId, n);
            AfterQueue?.Invoke(); return IdentitySecurityNotificationOutcome.Accepted;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        internal required IdentityTestDatabase Database;
        internal required AuthenticationTestHost Host;
        internal required Factory<IdentityPersistenceDbContext> Identity;
        internal required Factory<TunnerMigrationsDbContext> Platform;
        internal required Contracts Contracts;
        internal required Dispatcher Dispatcher;
        internal required IdentityPostCommitProcessor Processor;
        internal required PendingOutboxMessage Message;
        internal required string Account;
        internal static async Task<Harness> Start(SessionListingPostgresFixture fixture, string family = "session")
        {
            var db = await fixture.CreateDatabaseAsync(); AuthenticationTestHost? host = null;
            try
            {
                var totp = family == "totp" ? new TotpTests.Profile() : null;
                host = family switch
                {
                    "passkey" => await PasskeyTests.StartRecoveryFixtureAsync(db, _ => { }),
                    "totp" => await TotpTests.StartTotp(db, totp!),
                    _ => await AuthenticationTestHost.Start(db)
                };
                var account = await host.SeedAsync(db);
                var caller = await SessionListingTests.SignIn(host);
                if (family == "passkey")
                { using var key = new PasskeyAuthenticator(); await PasskeyTests.SeedRecoveryCredentialAsync(host, key); }
                if (family == "totp") await TotpTests.Enroll(host, totp!, await TotpTests.Login(host));
                var identity = new Factory<IdentityPersistenceDbContext>(db.Context);
                var platform = new Factory<TunnerMigrationsDbContext>(() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(db.ConnectionString).Options));
                var contracts = new Contracts(host.Clock); var dispatcher = new Dispatcher(platform);
                await using var source = db.Context();
                var message = IdentityPostCommitProcessor.Project(family == "session"
                    ? await source.Set<OutboxMessage>().AsNoTracking().SingleAsync(x => x.EventType == "TunnerSessionAuthenticated" && x.Subject == caller.SessionId.ToString("N"))
                    : await source.Set<OutboxMessage>().AsNoTracking().SingleAsync(x => x.EventType == "IdentitySecurityNotificationRequested"));
                var inboxPlatform = new Factory<TunnerMigrationsDbContext>(() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(db.ConnectionString).Options));
                var processor = new IdentityPostCommitProcessor(identity, platform, new EfConsumerInboxStore(inboxPlatform),
                    [new IdentitySecurityPostCommitOwnerSource(identity)], contracts, new("fixture-identity-security", "fixture-generation-v1", TimeSpan.FromMinutes(1)), host.Clock, dispatcher);
                return new() { Database = db, Host = host, Identity = identity, Platform = platform, Contracts = contracts, Dispatcher = dispatcher, Processor = processor, Message = message, Account = account };
            }
            catch { if (host is not null) await host.DisposeAsync(); await db.DisposeAsync(); throw; }
        }
        internal async Task AssertNoEffects()
        {
            await using var db = Platform.CreateDbContext(); Assert.Empty(await db.AuditEvents.ToListAsync());
            Assert.Empty(await db.ConsumerInboxEntries.ToListAsync()); Assert.Empty(Dispatcher.Intents); Assert.Equal(0, Dispatcher.Attempts);
        }
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Database.DisposeAsync(); }
    }
}
