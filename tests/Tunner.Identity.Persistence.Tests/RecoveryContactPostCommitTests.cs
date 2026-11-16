using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database;
using Tunner.Database.Audit;
using Tunner.Database.DurableWork;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class RecoveryContactPostCommitTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task RealContactHttpEventRetriesExistingQueueWithOneAuditAndStableDelivery()
    { await using var db = await fixture.CreateDatabaseAsync(); await RecoveryContactRepairPrivateIntegrationTests.VerifyPostCommit(db, "retry"); }

    [Theory]
    [InlineData("expired")]
    [InlineData("contact")]
    public async Task ContactNoticeAlreadyQueuedBeforeInvalidationCompletesWithoutAnotherHandoff(string change)
    { await using var db = await fixture.CreateDatabaseAsync(); await RecoveryContactRepairPrivateIntegrationTests.VerifyPostCommit(db, change); }

    [Fact]
    public async Task ContactAdapterRejectsMismatchedPersistedNotificationQualification()
    { await using var db = await fixture.CreateDatabaseAsync(); await RecoveryContactRepairPrivateIntegrationTests.VerifyPostCommit(db, "qualification"); }
}

internal sealed partial class RecoveryContactRepairPrivateIntegrationTests
{
    internal static async Task VerifyPostCommit(IdentityTestDatabase db, string scenario)
    {
        var authority = new Authority(); var notices = new Notices();
        await using var host = await Host(db, authority, notices); var seed = await SeedAsync(db, host);
        var bootstrap = await host.Options(); var cookies = bootstrap.Cookies; var request = Guid.NewGuid();
        using (var start = await host.Post("/recovery/start", new { requestId = request, email = "person@example.test", goalReference = authority.Policy.GoalReference }, cookies, bootstrap.Csrf))
        { Assert.Equal(HttpStatusCode.OK, start.StatusCode); Assert.True(NoLoginCookie(start)); cookies = Merge(cookies, start); }
        Guid challengeId;
        using (var options = await host.Post("/recovery/contact/options", new { requestId = request }, cookies, bootstrap.Csrf))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); Assert.True(NoLoginCookie(options));
            challengeId = (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        }
        PendingOutboxMessage message; RecoveryContactNotificationDecision decision; Guid delivery;
        await using (var read = db.Context())
        {
            var challenge = await read.Set<RecoveryContactChallengeRow>().AsNoTracking().SingleAsync(x => x.ChallengeId == challengeId);
            delivery = challenge.DeliveryId; decision = JsonSerializer.Deserialize<RecoveryContactNotificationDecision>(challenge.NotificationSnapshot)!;
            message = IdentityPostCommitProcessor.Project(await read.Set<OutboxMessage>().AsNoTracking().SingleAsync(x => x.EventId == challenge.NotificationOutboxEventId));
        }
        var identity = new PostCommitFactory<IdentityPersistenceDbContext>(db.Context);
        var platform = new PostCommitFactory<TunnerMigrationsDbContext>(() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(db.ConnectionString).Options));
        var owner = new IdentitySecurityPostCommitOwnerSource(identity);
        var dispatcher = new RecoveryContactPostCommitNotificationDispatcher(identity, host.Services.GetRequiredService<IServiceScopeFactory>());
        var contracts = new ContactPostCommitContracts(host.Clock);
        var processor = new IdentityPostCommitProcessor(identity, platform, new EfConsumerInboxStore(platform), [owner], contracts,
            new("fixture-recovery-contact", "fixture-generation-v1", TimeSpan.FromMinutes(1)), host.Clock, dispatcher);
        var facts = Assert.IsType<IdentityPostCommitOwnerFacts>(await owner.ReadCommittedAsync(message, default));
        Assert.Null(facts.ActorAccountId); Assert.Null(facts.SessionReference);
        Assert.Equal(seed.Account, facts.References["targetAccountReference"]);
        Assert.Equal(seed.Contact.ToString("N"), facts.References["contactReference"]);
        Assert.Equal(decision.TemplateVersion, facts.References["templateVersion"]);
        if (scenario == "qualification")
        {
            var notice = ContactPostCommitContracts.Notice(new(message, facts, new string('A', 64)));
            foreach (var changed in new[]
            {
                notice with { IntentId = Guid.NewGuid() }, notice with { SourceEventId = Guid.NewGuid() },
                notice with { ContractVersion = "fixture-unrelated-contract" }, notice with { NotificationClass = "fixture-unrelated-class" },
                notice with { TemplateVersion = "fixture-unrelated-template" }, notice with { Locale = "fixture-unrelated-locale" },
                notice with { RecipientReference = Guid.NewGuid().ToString("N") }, notice with { RecipientEvidenceReference = "fixture-unrelated-recipient-evidence" },
                notice with { RouteResolutionReference = "fixture-unrelated-route" }, notice with { ProcessingReference = "fixture-unrelated-processing" },
                notice with { RetentionReference = "fixture-unrelated-retention" }, notice with { EvidenceReference = "fixture-unrelated-evidence" }
            }) await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.QueueAsync(changed, default));
            Assert.Equal(0, notices.Calls); await using var empty = platform.CreateDbContext();
            Assert.Empty(await empty.AuditEvents.ToListAsync()); Assert.Empty(await empty.ConsumerInboxEntries.ToListAsync()); return;
        }
        if (scenario == "retry")
        {
            notices.FailNext = true;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(message));
            Assert.Equal("FINITE_CONTACT_QUEUE_UNAVAILABLE", error.Message); Assert.Equal(0, notices.Count);
            await using (var failed = platform.CreateDbContext())
            { Assert.Single(await failed.AuditEvents.ToListAsync()); Assert.Null((await failed.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc); }
        }
        else
        {
            Assert.True(await Dispatch(host, delivery)); Assert.Equal(1, notices.Count);
            if (scenario == "expired") host.Clock.Advance(authority.Policy.ContactRecoveryProfile!.ProofLifetime);
            else
            {
                await using var mutation = db.Context(); var contact = await mutation.VerifiedContacts.SingleAsync(x => x.ContactId == seed.Contact);
                contact.Version++; await mutation.SaveChangesAsync();
            }
            Assert.Equal(IdentitySecurityNotificationOutcome.NoLongerDispatchable,
                await dispatcher.QueueAsync(ContactPostCommitContracts.Notice(new(message, facts, new string('A', 64))), default));
            Assert.NotNull(await owner.ReadCommittedAsync(message, default));
        }
        Assert.Equal(IdentityPostCommitOutcome.Completed, await processor.ProcessAsync(message));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await processor.ProcessAsync(message));
        Assert.Equal(1, notices.Count); Assert.Equal(scenario == "retry" ? 2 : 1, notices.Calls);
        await using var stored = platform.CreateDbContext(); var audit = Assert.Single(await stored.AuditEvents.ToListAsync()); audit.RequireIntegrity();
        Assert.NotNull((await stored.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
        Assert.Equal(message.OperationId, audit.OperationId); Assert.Equal(message.CorrelationId, audit.CorrelationId);
        var safe = audit.Envelope + JsonSerializer.Serialize(facts) + JsonSerializer.Serialize(contracts.LastNotice);
        foreach (var secret in new[] { notices.Code(challengeId), "person@example.test", Password, "ProtectedCode", "ProtectedAddress", "DestinationDigest" })
            Assert.True(!safe.Contains(secret, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ContactPostCommitContracts(TestClock clock) : IIdentityPostCommitContractSource
    {
        internal IdentitySecurityNotification? LastNotice;
        internal static IdentitySecurityNotification Notice(IdentityPostCommitFacts facts)
        {
            var r = facts.Owner.References;
            return new(Guid.ParseExact(r["deliveryReference"], "N"), facts.Event.EventId, facts.Digest, r["notificationContractReference"],
                r["notificationClass"], r["templateVersion"], "fixture-explicit-published-content-version", r["locale"], r["contactReference"],
                r["recipientEvidenceReference"], r["routeResolutionReference"], facts.Owner.ProcessingReference, facts.Owner.RetentionReference, r["notificationEvidenceReference"]);
        }
        public ValueTask<IdentityPostCommitQualification?> RequireCurrentAsync(IdentityPostCommitFacts facts, DateTimeOffset at, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); var e = facts.Event;
            var audit = new AuditEvent(e.EventId, e.OccurredAtUtc, new("fixture-anonymous-recovery", "fixture-anonymous", []), "fixture-recovery-contact-requested",
                e.OperationId!, e.CorrelationId!, [new(e.Subject, e.ResourceVersion)], "fixture-notification-requested", e.DataClassification, facts.Owner.RetentionReference,
                e.EventId.ToString("N"), policyVersionReferences: ["fixture-post-commit-contract"], evidenceReferences: ["fixture-post-commit-approval"]);
            LastNotice = Notice(facts);
            return ValueTask.FromResult<IdentityPostCommitQualification?>(new(e.EventId, facts.Digest, "fixture-post-commit-contract", "fixture-post-commit-approval",
                new(clock.GetUtcNow().AddMinutes(-1), true, clock.GetUtcNow().AddMinutes(1), false), audit, LastNotice));
        }
    }
    private sealed class PostCommitFactory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => create();
        public Task<T> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }
}
