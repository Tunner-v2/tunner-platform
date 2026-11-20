using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tunner.Database;
using Tunner.Database.Audit;
using Tunner.Database.DurableWork;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Real native-store owner joins using the shared PostgreSQL/password fixture. These do not rerun the Product protocol suite.</summary>
public sealed class ProductAuthorizationPostCommitOwnerTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Theory]
    [InlineData("ProductAuthorizationGranted", true)]
    [InlineData("ProductAuthorizationRedeemed", false)]
    public async Task CommittedNativeAuthorizationUsesActualUserOrBoundClientActorWithoutNotification(string eventType, bool userActor)
    {
        await using var h = await Harness.Start(fixture); var message = await h.AuthorizationEvent(eventType);
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(message));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(message));
        await using var platform = h.Platform.CreateDbContext(); var audit = await platform.AuditEvents.SingleAsync(); audit.RequireIntegrity();
        using var json = JsonDocument.Parse(audit.Envelope);
        Assert.Equal(userActor ? h.Account : h.Request.ClientId, json.RootElement.GetProperty("actor").GetProperty("principal_id").GetString());
        Assert.Equal(h.Account, h.Contracts.LastFacts!.Owner.References["targetAccountReference"]);
        Assert.Equal(userActor ? h.Account : null, h.Contracts.LastFacts.Owner.ActorAccountId);
        Assert.Equal(message.OperationId, audit.OperationId); Assert.Equal(message.CorrelationId, audit.CorrelationId);
        Assert.NotNull((await platform.ConsumerInboxEntries.SingleAsync()).CompletedAtUtc);
        Assert.DoesNotContain(Password, audit.Envelope, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Lineage.SecurityStampDigest, audit.Envelope, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitDeniedNativeRequestUsesConsumedReviewActorAndOriginalProtectedRequest()
    {
        await using var h = await Harness.Start(fixture); var message = await h.DenialEvent();
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(message));
        var facts = h.Contracts.LastFacts!.Owner;
        Assert.Equal(h.Account, facts.ActorAccountId); Assert.Equal(h.Session.ToString("N"), facts.SessionReference);
        Assert.Equal(h.Request.Digest, facts.References["originalRequestDigest"]);
        Assert.Equal("fixture-denial-publication-v1", facts.PolicyVersion);
        await using var platform = h.Platform.CreateDbContext(); Assert.Single(await platform.AuditEvents.ToListAsync());
        await using var identity = h.Database.Context();
        Assert.False(await identity.Set<OutboxMessage>().AnyAsync(x => x.EventType == "ProductAuthorizationGranted"));
    }

    [Theory]
    [InlineData("missing-consumed")]
    [InlineData("different-review-actor")]
    [InlineData("different-request")]
    public async Task DenialWithoutExactImmutableActorAndRequestReceiptIsRejected(string mismatch)
    {
        await using var h = await Harness.Start(fixture); var message = await h.DenialEvent(mismatch);
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(message));
        await using var platform = h.Platform.CreateDbContext(); Assert.Empty(await platform.AuditEvents.ToListAsync());
        Assert.Empty(await platform.ConsumerInboxEntries.ToListAsync());
    }

    [Fact]
    public async Task HistoricalGrantStillResolvesAfterSourceAccountAndNativeArtifactAreRevoked()
    {
        await using var h = await Harness.Start(fixture); var message = await h.AuthorizationEvent("ProductAuthorizationGranted");
        await using (var scope = h.Host.Services.CreateAsyncScope())
        {
            var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var authorization = await authorizations.FindByIdAsync(message.Subject); Assert.NotNull(authorization);
            Assert.True(await authorizations.TryRevokeAsync(authorization!));
        }
        await using (var db = h.Database.Context())
        {
            var account = await db.Accounts.SingleAsync(x => x.AccountId == h.Account); account.Version++;
            var user = await db.Users.SingleAsync(x => x.AccountId == h.Account); user.SecurityStamp = Guid.NewGuid().ToString("N"); await db.SaveChangesAsync();
        }
        h.Host.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(message));
        Assert.Equal(h.Account, h.Contracts.LastFacts!.Owner.ActorAccountId);
    }

    [Theory]
    [InlineData("ProductAuthorizationRevoked")]
    [InlineData("ProductAuthorizationRefreshed")]
    [InlineData("ProductAuthorizationRefreshDenied")]
    [InlineData("ProductAuthorizationIntrospected")]
    public async Task LifecycleDigestWithoutRecordedRequestingActorDoesNotBecomeAUserAudit(string eventType)
    {
        await using var h = await Harness.Start(fixture);
        var message = await h.SaveEvent(eventType, "fixture-authorization", "{}", "fixture-operation", "fixture-correlation");
        Assert.False(h.Owner.Owns(message));
        Assert.Equal(IdentityPostCommitOutcome.Unavailable, await h.Processor.ProcessAsync(message));
        await using var platform = h.Platform.CreateDbContext(); Assert.Empty(await platform.AuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData("ProductAuthorizationRefreshed", "None", true)]
    [InlineData("ProductAuthorizationRefreshDenied", "None", false)]
    [InlineData("ProductAuthorizationIntrospected", "None", true)]
    [InlineData("ProductAuthorizationRevoked", "PresentedToken", false)]
    [InlineData("ProductAuthorizationRevoked", "AuthorizationChain", false)]
    public async Task SchemaTwoLifecycleJoinsNativeArtifactsAndAuditsRecordedClientSeparatelyFromTarget(string eventType, string effect, bool active)
    {
        await using var h = await Harness.Start(fixture); var message = await h.LifecycleEvent(eventType, effect, active);
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(message));
        var facts = h.Contracts.LastFacts!.Owner; Assert.Null(facts.ActorAccountId); Assert.Null(facts.SessionReference);
        Assert.Equal(h.Account, facts.References["targetAccountReference"]);
        Assert.Equal("fixture-distinct-lifecycle-client", facts.References["actorClientReference"]);
        await using var platform = h.Platform.CreateDbContext(); var audit = await platform.AuditEvents.SingleAsync(); audit.RequireIntegrity();
        using var json = JsonDocument.Parse(audit.Envelope);
        Assert.Equal("fixture-distinct-lifecycle-client", json.RootElement.GetProperty("actor").GetProperty("principal_id").GetString());
    }

    [Theory]
    [InlineData("missing-client")]
    [InlineData("missing-native-client")]
    [InlineData("different-token")]
    [InlineData("different-lineage")]
    [InlineData("different-operation")]
    [InlineData("different-result")]
    public async Task SchemaTwoLifecycleRequiresRecordedActorOriginalLineageAndExactTokenResult(string mismatch)
    {
        await using var h = await Harness.Start(fixture); var message = await h.LifecycleEvent("ProductAuthorizationRefreshed", "None", true, mismatch);
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(message));
        await using var platform = h.Platform.CreateDbContext(); Assert.Empty(await platform.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task RecordedLifecycleResultSurvivesLaterNativeStatusChanges()
    {
        await using var h = await Harness.Start(fixture); var message = await h.LifecycleEvent("ProductAuthorizationIntrospected", "None", true);
        using var payload = JsonDocument.Parse(message.Payload); var tokenId = payload.RootElement.GetProperty("tokenReference").GetString()!;
        await using (var scope = h.Host.Services.CreateAsyncScope())
        {
            var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            Assert.True(await authorizations.TryRevokeAsync((await authorizations.FindByIdAsync(message.Subject))!));
            var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            Assert.True(await tokens.TryRevokeAsync((await tokens.FindByIdAsync(tokenId))!));
        }
        Assert.Equal(IdentityPostCommitOutcome.Completed, await h.Processor.ProcessAsync(message));
        Assert.Equal("valid", h.Contracts.LastFacts!.Owner.References["tokenStatus"]);
    }

    [Theory]
    [InlineData("ProductAuthorizationGranted")]
    [InlineData("TunnerSessionAuthenticated")]
    public async Task SchemaTwoIsNotEnabledForUnrelatedEventContracts(string eventType)
    {
        await using var h = await Harness.Start(fixture);
        var message = await h.SaveEvent(eventType, "fixture-unrelated", "{}", "fixture-operation", "fixture-correlation", "2");
        Assert.Equal(IdentityPostCommitOutcome.Rejected, await h.Processor.ProcessAsync(message));
        await using var platform = h.Platform.CreateDbContext(); Assert.Empty(await platform.AuditEvents.ToListAsync());
    }

    private sealed class Factory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => create();
        public Task<T> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }

    private sealed class Contracts : IIdentityPostCommitContractSource
    {
        internal IdentityPostCommitFacts? LastFacts;
        public ValueTask<IdentityPostCommitQualification?> RequireCurrentAsync(IdentityPostCommitFacts facts, DateTimeOffset at, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); LastFacts = facts;
            var e = facts.Event; var principal = facts.Owner.ActorAccountId ?? facts.Owner.References["actorClientReference"];
            var actor = new AuditActor(principal, facts.Owner.ActorAccountId is null ? "fixture-native-client" : "fixture-account", [facts.Owner.OwnerReference]);
            const string contract = "fixture-product-audit-contract-v1", evidence = "fixture-product-audit-approval";
            var audit = new AuditEvent(e.EventId, e.OccurredAtUtc, actor, e.EventType, e.OperationId!, e.CorrelationId!,
                [new(e.Subject, e.ResourceVersion)], "fixture-committed", e.DataClassification, "fixture-audit-retention", e.EventId.ToString("N"),
                policyVersionReferences: [contract], evidenceReferences: [evidence]);
            return ValueTask.FromResult<IdentityPostCommitQualification?>(new(e.EventId, facts.Digest, contract, evidence,
                new(at.AddDays(-1), true, at.AddDays(1), false), audit));
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        internal required IdentityTestDatabase Database;
        internal required AuthenticationTestHost Host;
        internal required Factory<TunnerMigrationsDbContext> Platform;
        internal required ProductAuthorizationPostCommitOwnerSource Owner;
        internal required IdentityPostCommitProcessor Processor;
        internal required Contracts Contracts;
        internal required ProductAuthorizationRequest Request;
        internal required ProductAuthorizationLineage Lineage;
        internal required string Account;
        internal required Guid Session;
        internal required string ApplicationId;

        internal static async Task<Harness> Start(SessionListingPostgresFixture fixture)
        {
            var db = await fixture.CreateDatabaseAsync(); AuthenticationTestHost? host = null;
            try
            {
                host = await AuthenticationTestHost.Start(db); var account = await host.SeedAsync(db); var caller = await SessionListingTests.SignIn(host);
                await using var scope = host.Services.CreateAsyncScope(); var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                var application = await applications.CreateAsync(new OpenIddictApplicationDescriptor
                { ClientId = "fixture-postcommit-product-client", ClientType = OpenIddictConstants.ClientTypes.Public });
                var applicationId = (await applications.GetIdAsync(application))!;
                await using var read = db.Context(); var session = await read.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == caller.SessionId);
                var request = new ProductAuthorizationRequest("PRD-FIXTURE-POSTCOMMIT", "SANDBOX", "fixture-postcommit-product-client", "https://fixture-product.example.test/callback",
                    ["openid"], ["https://fixture-product.example.test/api"], new('A', 43), "S256", "code", "form_post", null, null);
                var at = host.Clock.GetUtcNow(); var interval = new EffectivePolicyInterval(at.AddDays(-1), true, at.AddDays(1), false);
                var policy = new ProductAuthorizationPolicy(new("fixture-product-policy-v1", "ACTIVE", Origin, interval, null, ["fixture-product-approval"]),
                    Origin, "fixture-product-evidence", "fixture-processing", "fixture-retention", "RESTRICTED_SECURITY", ["pwd"], [session.Assurance],
                    TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), false, null, 128);
                var registration = new ProductAuthorizationRegistration(request.Digest, request.ProductId, 1, "ACTIVE", request.Environment, 1, "ACTIVE", request.ClientId,
                    1, "ACTIVE", applicationId, "public", request.RedirectUri, request.Scopes, request.Resources, "fixture-registry-v1", "fixture-registry-evidence", true, interval);
                var operation = "OP-" + Guid.NewGuid().ToString("N"); var correlation = Guid.NewGuid().ToString("N");
                var relationshipRequest = new ProductRelationshipRequest(account, 1, caller.SessionId, request.ProductId, 1, request.Environment, 1, request.ClientId, 1, request.Digest, operation, correlation);
                var relationship = new ProductRelationshipDecision(relationshipRequest.Digest, "fixture-product-relationship", 1, true, true, "fixture-relationship-policy-v1",
                    "fixture-relationship-evidence", "fixture-product-scoped-subject", "fixture-subject-mapping-v1", "fixture-subject-evidence", interval);
                var lineage = new ProductAuthorizationLineage(1, request, account, 1, caller.SessionId, Origin, session.AuthenticatedAt, session.AuthenticationMethods,
                    session.Assurance, session.PolicyVersion, session.SecurityStampDigest, "{}", "{}", policy.Snapshot, registration, relationshipRequest, relationship,
                    operation, correlation, at, at.AddMinutes(1));
                var platform = new Factory<TunnerMigrationsDbContext>(() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(db.ConnectionString).Options));
                var owner = new ProductAuthorizationPostCommitOwnerSource(host.Services.GetRequiredService<IServiceScopeFactory>(), host.Services.GetRequiredService<IDataProtectionProvider>());
                var contracts = new Contracts();
                var processor = new IdentityPostCommitProcessor(new Factory<IdentityPersistenceDbContext>(db.Context), platform, new EfConsumerInboxStore(platform), [owner],
                    contracts, new("fixture-product-audit", "fixture-generation-v1", TimeSpan.FromMinutes(1)), host.Clock);
                return new() { Database = db, Host = host, Platform = platform, Owner = owner, Processor = processor, Contracts = contracts,
                    Request = request, Lineage = lineage, Account = account, Session = caller.SessionId, ApplicationId = applicationId };
            }
            catch { if (host is not null) await host.DisposeAsync(); await db.DisposeAsync(); throw; }
        }

        internal async Task<PendingOutboxMessage> AuthorizationEvent(string eventType)
        {
            await using var scope = Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var descriptor = new OpenIddictAuthorizationDescriptor { ApplicationId = ApplicationId, Subject = Lineage.Relationship.Subject,
                Status = OpenIddictConstants.Statuses.Valid, Type = OpenIddictConstants.AuthorizationTypes.AdHoc, CreationDate = Lineage.AuthorizedAt };
            descriptor.Scopes.UnionWith(Request.Scopes);
            descriptor.Properties.Add(ProductAuthorizationApplication.LineageProperty, JsonSerializer.SerializeToElement(Lineage));
            var authorization = await manager.CreateAsync(descriptor); var id = (await manager.GetIdAsync(authorization))!;
            return await SaveEvent(eventType, id, JsonSerializer.Serialize(new { reference = id, product = Request.ProductId, environment = Request.Environment,
                relationship = Lineage.Relationship.RelationshipId, relationshipVersion = Lineage.Relationship.Version, policyVersion = "fixture-product-policy-v1",
                originalRequestDigest = Request.Digest }), Lineage.OperationId, Lineage.CorrelationId);
        }

        internal async Task<PendingOutboxMessage> DenialEvent(string? mismatch = null)
        {
            await using var scope = Host.Services.CreateAsyncScope(); var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var at = Host.Clock.GetUtcNow(); var binding = new string('A', 64);
            var material = JsonSerializer.Serialize(new { Request, NativeApplicationId = ApplicationId, Lineage.OperationId, Lineage.CorrelationId,
                Origin, CreatedAt = at, ExpiresAt = at.AddMinutes(1), PolicySnapshot = "fixture-continuation-policy", BindingDigest = binding });
            var protector = Host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.Identity.ProductAuthorization.Continuation.Material.v1");
            var descriptor = new OpenIddictTokenDescriptor { ApplicationId = ApplicationId, CreationDate = at, ExpirationDate = at.AddMinutes(1),
                Type = OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken, Status = OpenIddictConstants.Statuses.Valid, Payload = protector.Protect(material) };
            descriptor.Properties.Add("tunner.product.continuation.v1", JsonSerializer.SerializeToElement(new { Origin,
                MaterialDigest = AuthenticationIdentity.Digest(material), BindingDigest = binding, Lineage.OperationId, Lineage.CorrelationId }));
            if (mismatch != "missing-consumed") descriptor.Properties.Add("tunner.product.continuation.consumed.v1", JsonSerializer.SerializeToElement(new
            { AccountId = Account, SessionId = Session, Lineage.OperationId, RequestDigest = mismatch == "different-request" ? new string('B', 64) : Request.Digest }));
            descriptor.Properties.Add("tunner.product.continuation.review.v1", JsonSerializer.SerializeToElement(new
            { Generation = 1, AccountId = mismatch == "different-review-actor" ? "ACC-other" : Account, SessionId = Session, ApprovalDigest = (string?)null, Denied = true, DenialDigest = new string('C', 64) }));
            var token = await tokens.CreateAsync(descriptor); Assert.True(await tokens.TryRedeemAsync(token)); var id = (await tokens.GetIdAsync(token))!;
            return await SaveEvent("ProductAuthorizationDeclined", id, JsonSerializer.Serialize(new { reference = id, product = Request.ProductId, environment = Request.Environment,
                originalRequestDigest = Request.Digest, presentationVersion = "fixture-denial-publication-v1", EvidenceReference = "fixture-denial-publication-evidence",
                ProcessingReference = "fixture-processing", RetentionReference = "fixture-retention" }), Lineage.OperationId, Lineage.CorrelationId);
        }

        internal async Task<PendingOutboxMessage> LifecycleEvent(string type, string effect, bool active, string? mismatch = null)
        {
            var grant = await AuthorizationEvent("ProductAuthorizationGranted");
            await using var scope = Host.Services.CreateAsyncScope();
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var requester = await applications.CreateAsync(new OpenIddictApplicationDescriptor
            { ClientId = "fixture-distinct-lifecycle-client", ClientType = OpenIddictConstants.ClientTypes.Public });
            var requesterId = (await applications.GetIdAsync(requester))!;
            var operation = type is "ProductAuthorizationRefreshed" or "ProductAuthorizationRefreshDenied" ? "Refresh" :
                type == "ProductAuthorizationIntrospected" ? "Introspection" : "Revocation";
            var tokenType = operation == "Refresh" ? OpenIddictConstants.TokenTypeIdentifiers.RefreshToken : OpenIddictConstants.TokenTypeIdentifiers.AccessToken;
            var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var token = await tokens.CreateAsync(new OpenIddictTokenDescriptor { ApplicationId = ApplicationId, AuthorizationId = grant.Subject,
                Subject = Lineage.Relationship.Subject, Type = tokenType, Status = OpenIddictConstants.Statuses.Valid,
                CreationDate = Host.Clock.GetUtcNow(), ExpirationDate = Host.Clock.GetUtcNow().AddHours(1) });
            var tokenId = (await tokens.GetIdAsync(token))!;
            var authorizationStatus = effect == "AuthorizationChain" ? "revoked" : "valid";
            var tokenStatus = effect != "None" ? "revoked" : operation == "Refresh" && active ? "redeemed" : "valid";
            if (tokenStatus == "redeemed") Assert.True(await tokens.TryRedeemAsync(token));
            if (tokenStatus == "revoked") Assert.True(await tokens.TryRevokeAsync(token));
            if (authorizationStatus == "revoked")
            {
                var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
                Assert.True(await authorizations.TryRevokeAsync((await authorizations.FindByIdAsync(grant.Subject))!));
            }
            var payload = JsonSerializer.Serialize(new { reference = grant.Subject,
                tokenReference = mismatch == "different-token" ? Guid.NewGuid().ToString("N") : tokenId,
                product = Request.ProductId, environment = Request.Environment, originalRequestDigest = Request.Digest,
                factsDigest = new string('A', 64), policyVersion = "fixture-lifecycle-policy-v1", evidenceReference = "fixture-lifecycle-evidence",
                processingReference = "fixture-lifecycle-processing", retentionReference = "fixture-lifecycle-retention",
                operation = mismatch == "different-operation" ? "Revocation" : operation, active, effect, originalOperation = Lineage.OperationId,
                requestingClientId = mismatch == "missing-client" ? "" : "fixture-distinct-lifecycle-client",
                requestingNativeApplicationId = mismatch == "missing-native-client" ? "" : requesterId,
                originalLineageDigest = mismatch == "different-lineage" ? new string('B', 64) : AuthenticationIdentity.Digest(JsonSerializer.Serialize(Lineage)),
                authorizationStatus, tokenStatus = mismatch == "different-result" ? "revoked" : tokenStatus, tokenType });
            return await SaveEvent(type, grant.Subject, payload, "OP-" + Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "2");
        }

        internal async Task<PendingOutboxMessage> SaveEvent(string type, string subject, string payload, string operation, string correlation, string schema = "1")
        {
            await using var db = Database.Context(); var e = new OutboxMessage(Guid.NewGuid(), "1.0", type, schema, Host.Clock.GetUtcNow(), "tunner/identity",
                subject, "RESTRICTED_SECURITY", payload, "1", operation, correlation); db.Add(e); await db.SaveChangesAsync();
            await db.Entry(e).ReloadAsync(); return IdentityPostCommitProcessor.Project(e);
        }
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Database.DisposeAsync(); }
    }
}
