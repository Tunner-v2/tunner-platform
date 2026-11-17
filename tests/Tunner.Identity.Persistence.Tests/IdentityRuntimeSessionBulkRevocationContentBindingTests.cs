using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeSessionBulkRevocationContentBindingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ActualAdminBulkPublicationFeedsExistingUserOptionsAndRetainsIndependentHistoryWithoutRevocation()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using var admin = await Host.Start(db, additionalScopes: [Scope()]);
        var env = admin.Environment;
        var author = await admin.Browser("fixture-security-author");
        var approver = await admin.Browser("fixture-security-approver");
        var publisher = await admin.Browser("fixture-security-publisher");
        var content = new PresentationContentTestEnvironment(db, env.Clock, Bundle().Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        await content.InitializeAsync();
        foreach (var item in Bundle().Content.Values) _ = await content.PublishTextAsync(item.Key, item.Text);
        var binding = Binding(env, "fixture-bulk-v1") with { ContentScope = content.Scope, VariantReference = content.Variant };
        JsonElement proposal;
        using (var created = await admin.Post("/session-bulk-revocation-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = binding }, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        using (var updated = await admin.Post("/session-bulk-revocation-content/draft", new
        {
            idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-bulk-v2" },
            proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64()
        }, author))
        { Assert.Equal(HttpStatusCode.OK, updated.StatusCode); proposal = (await Body(updated)).GetProperty("result").Clone(); }
        using (var preview = await admin.Post("/proposals/preview", new
        { proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var body = await Body(preview);
            Assert.Equal("fixture-bulk-v2", body.GetProperty("sessionBulkRevocationContent").GetProperty("configurationVersionReference").GetString());
            Assert.False(body.TryGetProperty("sessionRevocationContent", out _));
        }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var first = await Publish(admin, publisher, proposal);
        var selected = await Current(env);
        Assert.Equal("fixture-bulk-v2", selected.ConfigurationVersionReference);
        await using (var read = db.Context())
            Assert.Equal((await read.Set<IdentityConfigurationPublicationRow>().SingleAsync(x => x.Id == first.GetProperty("publicationReference").GetGuid()))
                .ApprovalEventReference.ToString("N"), selected.QualificationReference);
        var budgets = new BulkBudgets(env.Interval);
        await using var user = await AuthenticationTestHost.Start(db, services =>
        {
            services.AddSingleton<TimeProvider>(env.Clock);
            services.AddSingleton<IPresentationContentProfileSource>(content);
            services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization);
            services.AddTunnerPresentationContent(db.ConnectionString);
            services.AddSingleton<ISessionListingHttpBudgetSource>(new SessionListingHttpBudgetCatalog([
                new(new("fixture-read-budget", "ACTIVE", SessionListingHttpBudget.Scope, env.Interval, null, ["SYNTHETIC-ONLY"]), 2, 4096)]));
            services.AddSingleton<ISessionBulkRevocationDiscoverySource>(budgets);
            services.AddSingleton<ISessionBulkRevocationHttpBudgetSource>(budgets);
            services.AddSingleton<IAssurancePolicySource>(new StepUpBrowserTests.Policies(env.Clock));
            services.AddSingleton<IStepUpPresentationSource, StepUpBrowserTests.Presentation>();
            services.AddTunnerPersistedSessionBulkRevocationContentBinding();
            services.AddTunnerPublishedSessionBulkRevocationPresentation();
            services.AddTunnerSessionBulkRevocation();
        });
        Assert.Equal(env.Clock.GetUtcNow(), user.Clock.GetUtcNow()); await user.SeedAsync(db);
        var cookies = await TotpTests.Login(user);
        await using var snapshot = db.Context();
        var before = await snapshot.Set<AuthenticationSessionRow>().AsNoTracking().OrderBy(x => x.SessionId).Select(x => new { x.SessionId, x.Version, x.State }).ToArrayAsync();
        _ = await Current(env);
        Assert.Equal(before, await snapshot.Set<AuthenticationSessionRow>().AsNoTracking().OrderBy(x => x.SessionId).Select(x => new { x.SessionId, x.Version, x.State }).ToArrayAsync());
        using (var options = await user.Get("/account/sessions/bulk-revocation/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var body = await Body(options);
            Assert.Equal(SessionBulkRevocationPresentation.RequiredKeys.Count, body.GetProperty("content").EnumerateObject().Count());
            await using var read = content.Open(); var published = await read.Application.ResolveAsync(new(content.Scope, content.Variant, env.Clock.GetUtcNow()));
            foreach (var item in published.Content.Values)
            {
                var actual = body.GetProperty("content").GetProperty(item.Key);
                Assert.Equal(item.Text, actual.GetProperty("text").GetString());
                Assert.Equal(item.VersionReference, actual.GetProperty("versionRef").GetString());
                Assert.Equal(item.PublicationReference, actual.GetProperty("publicationRef").GetString());
            }
        }
        // Existing authenticated ticket retrieval records SESSION_USE before the options handler.
        var afterAuthentication = await snapshot.Set<AuthenticationSessionRow>().AsNoTracking().OrderBy(x => x.SessionId).Select(x => new { x.SessionId, x.Version, x.State }).ToArrayAsync();
        Assert.Equal(before.Select(x => (x.SessionId, x.State)), afterAuthentication.Select(x => (x.SessionId, x.State)));
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var created = await admin.Post("/session-bulk-revocation-content/draft",
            new { idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-bulk-v3" } }, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var next = await Publish(admin, publisher, proposal, first.GetProperty("publicationReference").GetGuid());
        Assert.Equal("fixture-bulk-v3", (await Current(env)).ConfigurationVersionReference);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new
        {
            idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = next.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow())
        }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("fixture-bulk-v2", (await Current(env)).ConfigurationVersionReference);
        using (var history = await admin.Post("/history/preview", new
        { scope = new { family = "SessionBulkRevocationContent", authenticationPolicyVersion = Policy().Version }, publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal("fixture-bulk-v2", (await Body(history)).GetProperty("sessionBulkRevocationContent").GetProperty("configurationVersionReference").GetString()); }
        Assert.Equal(afterAuthentication, await snapshot.Set<AuthenticationSessionRow>().AsNoTracking().OrderBy(x => x.SessionId).Select(x => new { x.SessionId, x.Version, x.State }).ToArrayAsync());
        Assert.Empty(await snapshot.Set<StepUpProofRow>().ToArrayAsync());
        await using var platform = env.Platform(); Assert.False(await platform.OutboxMessages.AnyAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Fact]
    public async Task BulkBindingCannotBorrowTheSingleScopeOrAnotherPolicyAndUsesItsOwnApproval()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var single = await PublishOwner(env, Binding(env, "fixture-single-only"), single: true);
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => Current(env).AsTask());
        var bulk = await PublishOwner(env, Binding(env, "fixture-bulk-only"));
        Assert.Equal(bulk.Scope, Scope());
        Assert.Equal("fixture-bulk-only", (await Current(env)).ConfigurationVersionReference);
        Assert.NotEqual(single.PublicationReference, bulk.PublicationReference);
        Assert.Equal("fixture-single-only", (await ((ISessionRevocationContentBindingSource)env.Source)
            .RequireQualifiedAsync(Policy(), env.Clock.GetUtcNow(), CancellationToken.None)).ConfigurationVersionReference);
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => ((ISessionBulkRevocationContentBindingSource)env.Source)
            .RequireQualifiedAsync(Policy() with { Version = "different-policy" }, env.Clock.GetUtcNow(), CancellationToken.None).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NarrowPublicationBoundsRemainAuthoritativeAfterTheFinalAwaitedBulkRead(bool includesUntil)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var end = env.Clock.GetUtcNow().AddSeconds(1);
        _ = await PublishOwner(env, Binding(env, "fixture-bulk-end"), new(env.Clock.GetUtcNow(), true, end, includesUntil));
        var selected = await Current(env);
        Assert.Equal(end, selected.EffectiveInterval.Until); Assert.Equal(includesUntil, selected.EffectiveInterval.IncludesUntil);
        var source = new AdvanceAfterFinalRead((ISessionBulkRevocationContentBindingSource)env.Source,
            () => env.Clock.Advance(TimeSpan.FromSeconds(1) + (includesUntil ? TimeSpan.FromTicks(10) : TimeSpan.Zero)));
        var adapter = new PublishedSessionBulkRevocationPresentationSource(new FixedContent(selected), source, env.Clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.RequirePublishedAsync(Policy(), CancellationToken.None).AsTask());
        Assert.Equal(2, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservedBulkSuccessorClipsOnlyItsOwnScopeWithExactBoundaryInclusion(bool includesFrom)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        _ = await PublishOwner(env, Binding(env, "fixture-single-current"), single: true);
        var first = await PublishOwner(env, Binding(env, "fixture-bulk-current")); var at = env.Clock.GetUtcNow().AddSeconds(2);
        _ = await PublishOwner(env, Binding(env, "fixture-bulk-future"), new(at, includesFrom, null, false), first.PublicationReference);
        var selected = await Current(env);
        Assert.Equal(at, selected.EffectiveInterval.Until); Assert.Equal(!includesFrom, selected.EffectiveInterval.IncludesUntil);
        env.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(includesFrom ? "fixture-bulk-future" : "fixture-bulk-current", (await Current(env)).ConfigurationVersionReference);
        Assert.Null((await ((ISessionRevocationContentBindingSource)env.Source)
            .RequireQualifiedAsync(Policy(), env.Clock.GetUtcNow(), CancellationToken.None)).EffectiveInterval.Until);
    }

    [Fact]
    public void PreviousSingleBindingPayloadAndScopeBytesArePreservedWithBulkOmitted()
    {
        // RECONSTRUCTED afaf12e8 single-binding format; not a retained database payload.
        var binding = new SessionActionContentBinding("fixture-auth", "fixture-single-v1", "fixture-qualification", "fixture-scope", "fixture-variant",
            new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, null, false));
        var prior = JsonSerializer.Serialize(new LegacySingleDocument(null, null, null, binding));
        var current = IdentityConfigurationDocument.Read(prior);
        Assert.Equal(prior, current.Snapshot); Assert.Equal(Sha(prior), current.Digest); Assert.Null(current.SessionBulkRevocationContent);
        Assert.Equal("{\"Family\":13,\"AssuranceScope\":null,\"AuthenticationPolicyVersion\":\"fixture-auth\"}", current.Scope.Key);
        Assert.Equal(13, (int)IdentityConfigurationFamily.SessionRevocationContent); Assert.Equal(14, (int)IdentityConfigurationFamily.SessionBulkRevocationContent);
    }

    [Fact]
    public void ExplicitBulkBindingRegistrationDoesNotSelectSingleOrRuntimeCapabilities()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration();
        Assert.DoesNotContain(old, x => x.ServiceType == typeof(ISessionBulkRevocationContentBindingSource));
        var services = new ServiceCollection(); services.AddTunnerPersistedSessionBulkRevocationContentBinding();
        Assert.Contains(services, x => x.ServiceType == typeof(ISessionBulkRevocationContentBindingSource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(ISessionRevocationContentBindingSource) ||
            x.ServiceType == typeof(ISessionBulkRevocationDiscoverySource) || x.ServiceType == typeof(ISessionBulkRevocationHttpBudgetSource) ||
            x.ServiceType == typeof(ISessionListingHttpBudgetSource) || x.ServiceType == typeof(IAuthenticationPolicySource) ||
            x.ServiceType == typeof(ISessionBulkRevocationPresentationSource) || x.ServiceType == typeof(SessionBulkRevocationBrowserService));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedSessionBulkRevocationContentBinding());
    }

    private sealed class BulkBudgets(EffectivePolicyInterval interval) : ISessionBulkRevocationDiscoverySource, ISessionBulkRevocationHttpBudgetSource
    {
        public SessionBulkRevocationDiscoveryBudget RequireEffective(DateTimeOffset at) =>
            new(new("fixture-discovery-v1", "ACTIVE", SessionBulkRevocationDiscoveryBudget.Scope, interval, null, ["SYNTHETIC-ONLY"]), 20);
        SessionBulkRevocationHttpBudget ISessionBulkRevocationHttpBudgetSource.RequireEffective(DateTimeOffset at) =>
            new(new("fixture-transport-v1", "ACTIVE", SessionBulkRevocationHttpBudget.Scope, interval, null, ["SYNTHETIC-ONLY"]), 4096, 4096);
    }
    private sealed class AdvanceAfterFinalRead(ISessionBulkRevocationContentBindingSource inner, Action advance) : ISessionBulkRevocationContentBindingSource
    {
        internal int Reads;
        public async ValueTask<SessionActionContentBinding> RequireQualifiedAsync(AuthenticationPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
        { var value = await inner.RequireQualifiedAsync(policy, effectiveAt, cancellationToken); if (++Reads == 2) advance(); return value; }
    }
    private sealed class FixedContent(SessionActionContentBinding binding) : IPresentationContentResolver
    {
        public ContentResolution Resolve(ContentResolutionRequest request) => throw new InvalidOperationException("Only asynchronous Content resolution is allowed.");
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ContentResolution(binding.ContentScope, binding.VariantReference, binding.VariantReference, "en",
                Bundle().Content.ToDictionary(x => x.Key, x => new PublishedContent(x.Key, binding.VariantReference,
                    x.Value.VersionRef, x.Value.PublicationRef, x.Value.Locale, x.Value.Text), StringComparer.Ordinal))
            { EffectiveInterval = binding.EffectiveInterval });
        }
    }
    private sealed record LegacySingleDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionActionContentBinding? SessionRevocationContent);
    private static AuthenticationPolicy Policy() => PublishedSessionListingPresentationTests.TestPolicy();
    private static IdentityConfigurationScope Scope() => new(IdentityConfigurationFamily.SessionBulkRevocationContent, AuthenticationPolicyVersion: Policy().Version);
    private static SessionActionContentBinding Binding(RuntimeConfigurationTestEnvironment env, string version) =>
        new(Policy().Version, version, "proposed-not-runtime-authority", "fixture-bulk-content", "fixture-bulk-variant", env.Interval);
    private static SessionBulkRevocationPresentation Bundle() => new("en", SessionBulkRevocationPresentation.RequiredKeys.Select(key =>
        new RegistrationPublishedText(key, "fixture-variant", "fixture-version", "fixture-publication", "en", "PUBLISHED", "Fixture " + key)));
    private static ValueTask<SessionActionContentBinding> Current(RuntimeConfigurationTestEnvironment env) =>
        ((ISessionBulkRevocationContentBindingSource)env.Source).RequireQualifiedAsync(Policy(), env.Clock.GetUtcNow(), CancellationToken.None);
    private static async Task<IdentityConfigurationHistory> PublishOwner(RuntimeConfigurationTestEnvironment env, SessionActionContentBinding binding,
        EffectivePolicyInterval? interval = null, Guid? supersedes = null, bool single = false)
    {
        await using var owner = env.Open(); env.Authorization.Actor = "fixture-security-author";
        var proposal = single ? await owner.Application.CreateSessionRevocationContentDraftAsync(binding, "OP-create", "fixture-correlation")
            : await owner.Application.CreateSessionBulkRevocationContentDraftAsync(binding, "OP-create", "fixture-correlation");
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        proposal = await owner.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
        return await owner.Application.PublishAsync(env.Publication(proposal, supersedes) with { EffectiveInterval = interval ?? new(env.Clock.GetUtcNow(), true, null, false) });
    }
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Step(Host host, Browser actor, string operation, JsonElement proposal)
    {
        using var response = await host.Post("/proposals/" + operation, new
        { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Publish(Host host, Browser actor, JsonElement proposal, Guid? supersedes = null)
    {
        using var response = await host.Post("/proposals/publish", new
        {
            idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64(),
            effectiveInterval = Interval(host.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes
        }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
}
