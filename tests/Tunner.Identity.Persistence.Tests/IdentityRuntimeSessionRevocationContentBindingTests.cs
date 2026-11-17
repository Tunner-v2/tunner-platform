using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeSessionRevocationContentBindingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ActualAdminPublicationFeedsExistingUserOptionsWithReplayReauthorizationAndForwardRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using var admin = await Host.Start(db, additionalScopes: [Scope()]);
        var env = admin.Environment;
        var author = await admin.Browser("fixture-security-author");
        var approver = await admin.Browser("fixture-security-approver");
        var publisher = await admin.Browser("fixture-security-publisher");
        using (var bad = await admin.Post("/session-revocation-content/draft",
            new { idempotencyKey = Guid.NewGuid(), candidate = new { authenticationPolicyVersion = Policy().Version, unknown = "not-an-owner-input" } }, author))
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        await using (var empty = db.Context()) Assert.Empty(await empty.Set<IdentityConfigurationProposalRow>().ToArrayAsync());
        var content = new PresentationContentTestEnvironment(db, env.Clock, Bundle().Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        await content.InitializeAsync();
        foreach (var item in Bundle().Content.Values) _ = await content.PublishTextAsync(item.Key, item.Text);
        var binding = Binding(env, "fixture-signout-v1") with { ContentScope = content.Scope, VariantReference = content.Variant };
        var command = new { idempotencyKey = Guid.NewGuid(), candidate = binding };
        JsonElement proposal;
        using (var created = await admin.Post("/session-revocation-content/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post("/session-revocation-content/draft", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        admin.Owners.Denied = null;
        using (var replay = await admin.Post("/session-revocation-content/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        using (var conflict = await admin.Post("/session-revocation-content/draft",
            new { command.idempotencyKey, candidate = binding with { ConfigurationVersionReference = "different-body" } }, author))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var updated = await admin.Post("/session-revocation-content/draft", new
        {
            idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-signout-v2" },
            proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64()
        }, author))
        { Assert.Equal(HttpStatusCode.OK, updated.StatusCode); proposal = (await Body(updated)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal);
        using (var selfApproval = await admin.Post("/proposals/approve", new
        { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
            Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
        proposal = await Step(admin, approver, "approve", proposal);
        var first = await Publish(admin, publisher, proposal);
        var current = await Current(env);
        Assert.Equal("fixture-signout-v2", current.ConfigurationVersionReference);
        await using (var read = db.Context())
            Assert.Equal((await read.Set<IdentityConfigurationPublicationRow>().SingleAsync(x => x.Id == first.GetProperty("publicationReference").GetGuid()))
                .ApprovalEventReference.ToString("N"), current.QualificationReference);
        await using var user = await AuthenticationTestHost.Start(db, services =>
        {
            services.AddSingleton<TimeProvider>(env.Clock);
            services.AddSingleton<IPresentationContentProfileSource>(content);
            services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization);
            services.AddTunnerPresentationContent(db.ConnectionString);
            services.AddSingleton<ISessionListingHttpBudgetSource>(new SessionListingHttpBudgetCatalog([
                new(new("fixture-read-budget", "ACTIVE", SessionListingHttpBudget.Scope, env.Interval, null, ["SYNTHETIC-ONLY"]), 2, 4096)]));
            services.AddSingleton<IAssurancePolicySource>(new StepUpBrowserTests.Policies(env.Clock));
            services.AddSingleton<IStepUpPresentationSource, StepUpBrowserTests.Presentation>();
            services.AddTunnerPersistedSessionRevocationContentBinding();
            services.AddTunnerPublishedSessionRevocationPresentation();
            services.AddTunnerSessionRevocation();
        });
        Assert.Equal(env.Clock.GetUtcNow(), user.Clock.GetUtcNow());
        await user.SeedAsync(db);
        var cookies = await TotpTests.Login(user);
        using (var options = await user.Get("/account/sessions/revocation/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            var body = await Body(options);
            Assert.Equal(SessionRevocationPresentation.RequiredKeys.Count, body.GetProperty("content").EnumerateObject().Count());
            await using var read = content.Open();
            var bundle = await read.Application.ResolveAsync(new(content.Scope, content.Variant, env.Clock.GetUtcNow()));
            foreach (var item in bundle.Content.Values)
            {
                var actual = body.GetProperty("content").GetProperty(item.Key);
                Assert.Equal(item.Text, actual.GetProperty("text").GetString());
                Assert.Equal(item.VersionReference, actual.GetProperty("versionRef").GetString());
                Assert.Equal(item.PublicationReference, actual.GetProperty("publicationRef").GetString());
            }
        }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var created = await admin.Post("/session-revocation-content/draft",
            new { idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-signout-v3" } }, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var second = await Publish(admin, publisher, proposal, first.GetProperty("publicationReference").GetGuid());
        Assert.Equal("fixture-signout-v3", (await Current(env)).ConfigurationVersionReference);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new
        {
            idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = second.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow())
        }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("fixture-signout-v2", (await Current(env)).ConfigurationVersionReference);
        using (var history = await admin.Post("/history/preview", new
        { scope = WireScope(), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal("fixture-signout-v2", (await Body(history)).GetProperty("sessionRevocationContent").GetProperty("configurationVersionReference").GetString()); }
        await using var unchanged = db.Context();
        Assert.All(await unchanged.Set<AuthenticationSessionRow>().ToArrayAsync(), row => Assert.Equal("ACTIVE", row.State));
        Assert.Empty(await unchanged.Set<StepUpProofRow>().ToArrayAsync());
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("configuration")]
    [InlineData("qualification")]
    [InlineData("scope")]
    [InlineData("variant")]
    [InlineData("interval")]
    public async Task EveryExistingBindingInputIsRequiredBeforeOwnerEffects(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var binding = Binding(env, "fixture-invalid");
        binding = failure switch
        {
            "policy" => binding with { AuthenticationPolicyVersion = "" }, "configuration" => binding with { ConfigurationVersionReference = "" },
            "qualification" => binding with { QualificationReference = "" }, "scope" => binding with { ContentScope = "" },
            "variant" => binding with { VariantReference = "" }, _ => binding with { EffectiveInterval = null! }
        };
        await using var owner = env.Open();
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => owner.Application.CreateSessionRevocationContentDraftAsync(binding, "OP-invalid", "fixture-correlation"));
        await using var platform = env.Platform();
        Assert.Empty(await platform.AuditEvents.ToArrayAsync()); Assert.Empty(await platform.OutboxMessages.ToArrayAsync());
    }

    [Fact]
    public async Task MissingPublicationDifferentPolicyAndCallerCancellationFailClosed()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => Current(env).AsTask());
        _ = await PublishOwner(env, Binding(env, "fixture-bound"));
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => ((ISessionRevocationContentBindingSource)env.Source)
            .RequireQualifiedAsync(Policy() with { Version = "different-authentication-policy" }, env.Clock.GetUtcNow(), CancellationToken.None).AsTask());
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ((ISessionRevocationContentBindingSource)env.Source)
            .RequireQualifiedAsync(Policy(), env.Clock.GetUtcNow(), canceled.Token).AsTask());
        Assert.Equal("fixture-bound", (await Current(env)).ConfigurationVersionReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualPublicationBoundsClipTheBroaderCandidateWithExactEndInclusion(bool includesUntil)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var end = env.Clock.GetUtcNow().AddSeconds(1);
        _ = await PublishOwner(env, Binding(env, "fixture-narrow"), new(env.Clock.GetUtcNow(), true, end, includesUntil));
        var selected = await Current(env);
        Assert.Equal(end, selected.EffectiveInterval.Until); Assert.Equal(includesUntil, selected.EffectiveInterval.IncludesUntil);
        env.Clock.Advance(TimeSpan.FromSeconds(1));
        if (includesUntil) Assert.Equal("fixture-narrow", (await Current(env)).ConfigurationVersionReference);
        else await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => Current(env).AsTask());
        env.Clock.Advance(TimeSpan.FromTicks(10));
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => Current(env).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryAfterTheFinalAwaitedBindingReadCannotReturnPublishedOptions(bool includesUntil)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var end = env.Clock.GetUtcNow().AddSeconds(1);
        _ = await PublishOwner(env, Binding(env, "fixture-final-bound"), new(env.Clock.GetUtcNow(), true, end, includesUntil));
        var selected = await Current(env);
        var source = new AdvanceAfterFinalRead((ISessionRevocationContentBindingSource)env.Source,
            () => env.Clock.Advance(TimeSpan.FromSeconds(1) + (includesUntil ? TimeSpan.FromTicks(10) : TimeSpan.Zero)));
        var adapter = new PublishedSessionRevocationPresentationSource(new FixedContent(selected), source, env.Clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.RequirePublishedAsync(Policy(), CancellationToken.None).AsTask());
        Assert.Equal(2, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyCommittedFutureSelectionClipsTheCurrentBindingWithInvertedStartInclusion(bool includesFrom)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var first = await PublishOwner(env, Binding(env, "fixture-before"));
        var frontier = env.Clock.GetUtcNow().AddSeconds(2);
        _ = await PublishOwner(env, Binding(env, "fixture-after"), new(frontier, includesFrom, null, false), first.PublicationReference);
        var selected = await Current(env);
        Assert.Equal(frontier, selected.EffectiveInterval.Until); Assert.Equal(!includesFrom, selected.EffectiveInterval.IncludesUntil);
        env.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(includesFrom ? "fixture-after" : "fixture-before", (await Current(env)).ConfigurationVersionReference);
        env.Clock.Advance(TimeSpan.FromTicks(10));
        Assert.Equal("fixture-after", (await Current(env)).ConfigurationVersionReference);
    }

    [Fact]
    public async Task PersistedBindingExecutesRealAsynchronousDatabaseReads()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        _ = await PublishOwner(env, Binding(env, "fixture-async"));
        var interceptor = new AsyncOnlyReads();
        var services = new ServiceCollection();
        services.AddDbContext<IdentityPersistenceDbContext>(options => options.UseNpgsql(db.ConnectionString).AddInterceptors(interceptor));
        using var provider = services.BuildServiceProvider();
        var source = new PersistedIdentityRuntimeConfigurationSource(provider.GetRequiredService<IServiceScopeFactory>());
        Assert.Equal("fixture-async", (await ((ISessionRevocationContentBindingSource)source)
            .RequireQualifiedAsync(Policy(), env.Clock.GetUtcNow(), CancellationToken.None)).ConfigurationVersionReference);
        Assert.True(interceptor.Reads >= 4);
    }

    [Fact]
    public async Task ThirteenLegacyScopeKeysAndOldPayloadBytesRemainUnchanged()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        // RECONSTRUCTED prior 0..12 wire formats, not captured historical runtime bytes.
        for (var ordinal = 0; ordinal <= 12; ordinal++)
        {
            var scope = new IdentityConfigurationScope((IdentityConfigurationFamily)ordinal,
                AssuranceScope: ordinal == 1 ? env.AssuranceScope : ordinal == 9 ? StepUpBrowserTests.Scope : null,
                AuthenticationPolicyVersion: ordinal is 2 or 12 ? "fixture-auth" : null, RegistrationPolicyVersion: ordinal == 5 ? "fixture-registration" : null,
                TotpPolicyVersion: ordinal == 7 ? "fixture-totp" : null, PasskeyPolicyVersion: ordinal == 8 ? "fixture-passkey" : null,
                AssurancePolicyVersion: ordinal == 9 ? "fixture-step" : null);
            scope.Require();
            var prior = JsonSerializer.Serialize(new LegacyScope(ordinal, scope.AssuranceScope, scope.AuthenticationPolicyVersion, scope.RegistrationPolicyVersion,
                scope.TotpPolicyVersion, scope.PasskeyPolicyVersion, scope.AssurancePolicyVersion));
            Assert.Equal(prior, scope.Key); Assert.Equal(Sha(prior), Sha(scope.Key));
        }
        var original = JsonSerializer.Serialize(new LegacyDocument(env.Authentication(), null, null, null));
        var read = IdentityConfigurationDocument.Read(original); Assert.Equal(original, read.Snapshot); Assert.Equal(Sha(original), read.Digest);
        var listing = new SessionListingContentBinding(Policy().Version, "fixture-old-binding", "fixture-evidence", "fixture-scope", "fixture-variant", env.Interval);
        var last = JsonSerializer.Serialize(new LegacyDocument(null, null, null, listing));
        Assert.Equal(last, IdentityConfigurationDocument.Read(last).Snapshot); Assert.Null(read.SessionRevocationContent);
        Assert.Equal(13, (int)IdentityConfigurationFamily.SessionRevocationContent);
        Assert.DoesNotContain("SessionRevocationContent", JsonSerializer.Serialize(new IdentityConfigurationPreview(
            new(Guid.NewGuid(), 1, "DRAFT", new(IdentityConfigurationFamily.Authentication), "fixture-version", null, "fixture-digest", false), env.Authentication(), null, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void SeparateBindingOptInCannotActivateBulkBudgetsPoliciesPresentationOrRoutes()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration();
        Assert.DoesNotContain(old, item => item.ServiceType == typeof(ISessionRevocationContentBindingSource));
        var services = new ServiceCollection(); services.AddTunnerPersistedSessionRevocationContentBinding();
        Assert.Contains(services, item => item.ServiceType == typeof(ISessionRevocationContentBindingSource));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(ISessionBulkRevocationContentBindingSource) ||
            item.ServiceType == typeof(ISessionListingHttpBudgetSource) || item.ServiceType == typeof(IAuthenticationPolicySource) ||
            item.ServiceType == typeof(ISessionRevocationPresentationSource) || item.ServiceType == typeof(SessionRevocationBrowserService));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedSessionRevocationContentBinding());
    }

    private sealed class AsyncOnlyReads : DbCommandInterceptor
    {
        internal int Reads;
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => throw new InvalidOperationException("The asynchronous binding must not execute synchronous database reads.");
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult(result); }
    }
    private sealed class AdvanceAfterFinalRead(ISessionRevocationContentBindingSource inner, Action advance) : ISessionRevocationContentBindingSource
    {
        internal int Reads;
        public async ValueTask<SessionActionContentBinding> RequireQualifiedAsync(AuthenticationPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
        {
            var selected = await inner.RequireQualifiedAsync(policy, effectiveAt, cancellationToken);
            if (++Reads == 2) advance();
            return selected;
        }
    }
    private sealed class FixedContent(SessionActionContentBinding binding) : IPresentationContentResolver
    {
        public ContentResolution Resolve(ContentResolutionRequest request) => throw new InvalidOperationException("This asynchronous projection must not read Content synchronously.");
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ContentResolution(binding.ContentScope, binding.VariantReference, binding.VariantReference, "en",
                Bundle().Content.ToDictionary(x => x.Key, x => new PublishedContent(x.Key, binding.VariantReference,
                    x.Value.VersionRef, x.Value.PublicationRef, x.Value.Locale, x.Value.Text), StringComparer.Ordinal))
            { EffectiveInterval = binding.EffectiveInterval });
        }
    }
    private sealed record LegacyScope(int Family, AssurancePolicyScope? AssuranceScope, string? AuthenticationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TotpPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PasskeyPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssurancePolicyVersion);
    private sealed record LegacyDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionListingContentBinding? SessionListingContent);
    private static AuthenticationPolicy Policy() => PublishedSessionListingPresentationTests.TestPolicy();
    private static IdentityConfigurationScope Scope() => new(IdentityConfigurationFamily.SessionRevocationContent, AuthenticationPolicyVersion: Policy().Version);
    private static object WireScope() => new { family = "SessionRevocationContent", authenticationPolicyVersion = Policy().Version };
    private static SessionRevocationPresentation Bundle() => new("en", SessionRevocationPresentation.RequiredKeys.Select(key =>
        new RegistrationPublishedText(key, "fixture-variant", "fixture-version", "fixture-publication", "en", "PUBLISHED", "Fixture " + key)));
    private static SessionActionContentBinding Binding(RuntimeConfigurationTestEnvironment env, string version) =>
        new(Policy().Version, version, "proposed-not-runtime-authority", "fixture-signout-content", "fixture-signout-variant", env.Interval);
    private static ValueTask<SessionActionContentBinding> Current(RuntimeConfigurationTestEnvironment env) =>
        ((ISessionRevocationContentBindingSource)env.Source).RequireQualifiedAsync(Policy(), env.Clock.GetUtcNow(), CancellationToken.None);
    private static async Task<IdentityConfigurationHistory> PublishOwner(RuntimeConfigurationTestEnvironment env, SessionActionContentBinding binding,
        EffectivePolicyInterval? interval = null, Guid? supersedes = null)
    {
        await using var owner = env.Open(); env.Authorization.Actor = "fixture-security-author";
        var proposal = await owner.Application.CreateSessionRevocationContentDraftAsync(binding, "OP-create", "fixture-correlation");
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        proposal = await owner.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
        return await owner.Application.PublishAsync(env.Publication(proposal, supersedes) with { EffectiveInterval = interval ?? new(env.Clock.GetUtcNow(), true, null, false) });
    }
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
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
