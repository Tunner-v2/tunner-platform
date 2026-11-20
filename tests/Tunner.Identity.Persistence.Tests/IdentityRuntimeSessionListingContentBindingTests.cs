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

public sealed class IdentityRuntimeSessionListingContentBindingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ActualAdminBindingPublicationFeedsPublishedSessionsWithCurrentReplayAndForwardRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [Scope()]); var env = admin.Environment;
        var author = await admin.Browser("fixture-security-author"); var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        var original = PublishedSessionListingPresentationTests.ExistingBundle(); var content = new PresentationContentTestEnvironment(db, env.Clock, original.Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        await content.InitializeAsync(); foreach (var (key, item) in original.Content) _ = await content.PublishTextAsync(key, item.Text);
        var binding = Binding(env, "fixture-session-binding-v1") with { ContentScope = content.Scope, VariantReference = content.Variant };
        var command = new { idempotencyKey = Guid.NewGuid(), candidate = binding }; JsonElement p;
        using (var create = await admin.Post("/session-listing-content/draft", command, author)) { Assert.Equal(HttpStatusCode.OK, create.StatusCode); p = (await Body(create)).GetProperty("result").Clone(); }
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post("/session-listing-content/draft", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); admin.Owners.Denied = null;
        using (var replay = await admin.Post("/session-listing-content/draft", command, author)) { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        using (var conflict = await admin.Post("/session-listing-content/draft", new { command.idempotencyKey, candidate = binding with { ConfigurationVersionReference = "conflict" } }, author)) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var update = await admin.Post("/session-listing-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-session-binding-v2" }, proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, author)) { Assert.Equal(HttpStatusCode.OK, update.StatusCode); p = (await Body(update)).GetProperty("result").Clone(); }
        p = await Step(admin, author, "validate", p); p = await Step(admin, approver, "approve", p); var first = await Publish(admin, publisher, p);
        var policy = PublishedSessionListingPresentationTests.TestPolicy(); Assert.Equal("fixture-session-binding-v2", Current(env, policy).ConfigurationVersionReference);
        await using (var receipt = db.Context()) Assert.Equal((await receipt.Set<IdentityConfigurationPublicationRow>().SingleAsync(x => x.Id == first.GetProperty("publicationReference").GetGuid())).ApprovalEventReference.ToString("N"), Current(env, policy).QualificationReference);
        await using var user = await AuthenticationTestHost.Start(db, services =>
        {
            services.AddSingleton<TimeProvider>(env.Clock); services.AddSingleton<IPresentationContentProfileSource>(content); services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization); services.AddTunnerPresentationContent(db.ConnectionString);
            services.AddSingleton<ISessionListingHttpBudgetSource>(new SessionListingHttpBudgetCatalog([new(new("fixture-session-read-budget", "ACTIVE", SessionListingHttpBudget.Scope, env.Interval, null, ["SYNTHETIC-ONLY"]), 2, 4096)]));
            services.AddTunnerPersistedSessionListingContentBinding(); services.AddTunnerPublishedSessionListingPresentation();
        });
        Assert.Equal(user.Clock.GetUtcNow(), env.Clock.GetUtcNow()); await user.SeedAsync(db); var cookies = await TotpTests.Login(user);
        using (var options = await user.Get("/account/sessions/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var body = await Body(options); Assert.Equal(original.Content.Count, body.GetProperty("content").EnumerateObject().Count());
            await using var owner = content.Open(); var bundle = owner.Application.Resolve(new(content.Scope, content.Variant, env.Clock.GetUtcNow()));
            foreach (var item in bundle.Content.Values) { var actual = body.GetProperty("content").GetProperty(item.Key); Assert.Equal(item.Text, actual.GetProperty("text").GetString()); Assert.Equal(item.VersionReference, actual.GetProperty("versionRef").GetString()); Assert.Equal(item.PublicationReference, actual.GetProperty("publicationRef").GetString()); }
        }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var create = await admin.Post("/session-listing-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-session-binding-v3" } }, author)) { Assert.Equal(HttpStatusCode.OK, create.StatusCode); p = (await Body(create)).GetProperty("result").Clone(); }
        p = await Step(admin, author, "validate", p); p = await Step(admin, approver, "approve", p); var latest = await Publish(admin, publisher, p, first.GetProperty("publicationReference").GetGuid()); Assert.Equal("fixture-session-binding-v3", Current(env, policy).ConfigurationVersionReference);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(), supersedesPublicationReference = latest.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow()) }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("fixture-session-binding-v2", Current(env, policy).ConfigurationVersionReference);
        using (var history = await admin.Post("/history/preview", new { scope = WireScope(), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher)) { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal("fixture-session-binding-v2", (await Body(history)).GetProperty("sessionListingContent").GetProperty("configurationVersionReference").GetString()); }
        await using var unchanged = db.Context(); Assert.All(await unchanged.Set<AuthenticationSessionRow>().ToArrayAsync(), x => Assert.Equal("ACTIVE", x.State)); Assert.Empty(await unchanged.Set<StepUpProofRow>().ToArrayAsync());
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("configuration")]
    [InlineData("qualification")]
    [InlineData("scope")]
    [InlineData("variant")]
    [InlineData("interval")]
    public async Task AllSixOwnerBindingInputsAreRequiredBeforeEffects(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var c = Binding(env, "fixture-invalid");
        c = failure switch { "policy" => c with { AuthenticationPolicyVersion = "" }, "configuration" => c with { ConfigurationVersionReference = "" }, "qualification" => c with { QualificationReference = "" }, "scope" => c with { ContentScope = "" }, "variant" => c with { VariantReference = "" }, _ => c with { EffectiveInterval = null! } };
        await using var owner = env.Open(); await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => owner.Application.CreateSessionListingContentDraftAsync(c, "OP-invalid", "fixture-correlation"));
        await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync());
    }

    [Fact]
    public async Task RuntimeBindingCannotQualifyAnotherAuthenticationPolicy()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); _ = await PublishOwner(env, Binding(env, "fixture-bound")); var policy = PublishedSessionListingPresentationTests.TestPolicy();
        Assert.Throws<IdentityConfigurationUnavailableException>(() => Current(env, policy with { Version = "other-policy" })); Assert.Equal("fixture-bound", Current(env, policy).ConfigurationVersionReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingAndPublicationIntervalsFailClosedWithoutFallback(bool future)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var start = env.Clock.GetUtcNow().AddSeconds(future ? 1 : 0); var interval = new EffectivePolicyInterval(start, true, start.AddSeconds(2), false);
        _ = await PublishOwner(env, Binding(env, "fixture-bounded") with { EffectiveInterval = interval }, interval); var policy = PublishedSessionListingPresentationTests.TestPolicy();
        if (future) { Assert.Throws<IdentityConfigurationUnavailableException>(() => Current(env, policy)); env.Clock.Advance(TimeSpan.FromSeconds(1)); }
        Assert.Equal("fixture-bounded", Current(env, policy).ConfigurationVersionReference); env.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.Throws<IdentityConfigurationUnavailableException>(() => Current(env, policy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NarrowerPublicationBoundsSurviveTheFinalPersistedBindingRead(bool includesUntil)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var policy = PublishedSessionListingPresentationTests.TestPolicy();
        var end = env.Clock.GetUtcNow().AddSeconds(1); var publicationInterval = new EffectivePolicyInterval(env.Clock.GetUtcNow(), true, end, includesUntil);
        _ = await PublishOwner(env, Binding(env, "fixture-narrow-publication"), publicationInterval);
        var selected = Current(env, policy); Assert.Equal(publicationInterval.From, selected.EffectiveInterval.From); Assert.Equal(end, selected.EffectiveInterval.Until); Assert.Equal(includesUntil, selected.EffectiveInterval.IncludesUntil);
        Assert.True(Binding(env, "fixture-broad-candidate").EffectiveInterval.Contains(end.AddTicks(10)));
        var bindingSource = new AdvanceAfterFinalBindingRead((ISessionListingContentBindingSource)env.Source,
            () => env.Clock.Advance(TimeSpan.FromSeconds(1) + (includesUntil ? TimeSpan.FromTicks(10) : TimeSpan.Zero)));
        var adapter = new PublishedSessionListingPresentationSource(new FixedContent(selected), bindingSource, env.Clock);
        Assert.Throws<InvalidOperationException>(() => adapter.RequirePublished(policy)); Assert.Equal(2, bindingSource.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservedScheduledSuccessorClipsOldSelectionWithExactBoundaryInclusion(bool successorIncludesFrom)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var policy = PublishedSessionListingPresentationTests.TestPolicy();
        var first = await PublishOwner(env, Binding(env, "fixture-before-schedule")); var transition = env.Clock.GetUtcNow().AddSeconds(1);
        _ = await PublishOwner(env, Binding(env, "fixture-scheduled-successor"), new(transition, successorIncludesFrom, null, false), first.PublicationReference);
        var selected = Current(env, policy); Assert.Equal(transition, selected.EffectiveInterval.Until); Assert.Equal(!successorIncludesFrom, selected.EffectiveInterval.IncludesUntil);
        var atBoundary = ((ISessionListingContentBindingSource)env.Source).RequireQualified(policy, transition);
        Assert.Equal(successorIncludesFrom ? "fixture-scheduled-successor" : "fixture-before-schedule", atBoundary.ConfigurationVersionReference);
        var bindingSource = new AdvanceAfterFinalBindingRead((ISessionListingContentBindingSource)env.Source,
            () => env.Clock.Advance(TimeSpan.FromSeconds(1) + (successorIncludesFrom ? TimeSpan.Zero : TimeSpan.FromTicks(10))));
        var adapter = new PublishedSessionListingPresentationSource(new FixedContent(selected), bindingSource, env.Clock);
        Assert.Throws<InvalidOperationException>(() => adapter.RequirePublished(policy)); Assert.Equal(2, bindingSource.Reads);
        Assert.Equal("fixture-scheduled-successor", Current(env, policy).ConfigurationVersionReference);
    }

    [Fact]
    public async Task StrictHttpRequiresExactAuthScopeAndRejectsExtraLegalField()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [Scope()]); var actor = await admin.Browser("fixture-security-author");
        using (var missing = await admin.Post("/proposals/search", new { scope = new { family = "SessionListingContent" }, pageSize = 1, offset = 0 }, actor)) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var crossed = await admin.Post("/proposals/search", new { scope = new { family = "SessionListingContent", authenticationPolicyVersion = PolicyVersion(), totpPolicyVersion = "unexpected" }, pageSize = 1, offset = 0 }, actor)) Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        var c = Binding(admin.Environment, "fixture-invalid");
        using var extra = await admin.Post("/session-listing-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = new { c.AuthenticationPolicyVersion, c.ConfigurationVersionReference, c.QualificationReference, c.ContentScope, c.VariantReference, c.EffectiveInterval, termsUri = "https://fixture.example.test/terms" } }, actor); Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task TwelveLegacyScopesAndReconstructedBudgetPayloadPreserveBytes(int ordinal)
    {
        // RECONSTRUCTED checkpoint0051f7d3 formats, not retained historical runtime bytes. Earlier full family payload cases remain preserved.
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var scope = new IdentityConfigurationScope((IdentityConfigurationFamily)ordinal, AssuranceScope: ordinal == 1 ? env.AssuranceScope : ordinal == 9 ? StepUpBrowserTests.Scope : null,
            AuthenticationPolicyVersion: ordinal == 2 ? "fixture-auth" : null, RegistrationPolicyVersion: ordinal == 5 ? "fixture-registration" : null,
            TotpPolicyVersion: ordinal == 7 ? "fixture-totp" : null, PasskeyPolicyVersion: ordinal == 8 ? "fixture-passkey" : null, AssurancePolicyVersion: ordinal == 9 ? "fixture-step" : null);
        scope.Require(); var oldScope = JsonSerializer.Serialize(new LegacyScope(ordinal, scope.AssuranceScope, scope.AuthenticationPolicyVersion, scope.RegistrationPolicyVersion, scope.TotpPolicyVersion, scope.PasskeyPolicyVersion, scope.AssurancePolicyVersion)); Assert.Equal(oldScope, scope.Key); Assert.Equal(Sha(oldScope), Sha(scope.Key));
        var budget = new SessionListingHttpBudget(new("fixture-old-budget", "ACTIVE", SessionListingHttpBudget.Scope, env.Interval, null, ["SYNTHETIC-ONLY"]), 2, 4096);
        var payload = JsonSerializer.Serialize(new LegacyBudgetDocument(null, null, null, budget)); var document = IdentityConfigurationDocument.Read(payload); Assert.Equal(payload, document.Snapshot); Assert.Equal(Sha(payload), document.Digest); Assert.Null(document.SessionListingContent); Assert.Equal(12, (int)IdentityConfigurationFamily.SessionListingContent);
    }

    [Fact]
    public void BindingPortIsIndependentOfBudgetAuthenticationPresentationAndRoutes()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration(); Assert.DoesNotContain(old, x => x.ServiceType == typeof(ISessionListingContentBindingSource));
        var services = new ServiceCollection(); services.AddTunnerPersistedSessionListingContentBinding(); Assert.Contains(services, x => x.ServiceType == typeof(ISessionListingContentBindingSource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(ISessionListingHttpBudgetSource) || x.ServiceType == typeof(IAuthenticationPolicySource) || x.ServiceType == typeof(ISessionListingPresentationSource)); Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedSessionListingContentBinding());
    }

    private sealed class AdvanceAfterFinalBindingRead(ISessionListingContentBindingSource inner, Action advance) : ISessionListingContentBindingSource
    {
        internal int Reads;
        public SessionListingContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt)
        { var result = inner.RequireQualified(policy, effectiveAt); Reads++; if (Reads == 2) advance(); return result; }
    }
    private sealed class FixedContent(SessionListingContentBinding binding) : IPresentationContentResolver
    {
        public ContentResolution Resolve(ContentResolutionRequest request) => new(binding.ContentScope, binding.VariantReference, binding.VariantReference, "en",
            PublishedSessionListingPresentationTests.ExistingBundle().Content.ToDictionary(x => x.Key,
                x => new PublishedContent(x.Key, binding.VariantReference, x.Value.VersionRef, x.Value.PublicationRef, x.Value.Locale, x.Value.Text), StringComparer.Ordinal));
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Resolve(request)); }
    }

    private sealed record LegacyBudgetDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionListingHttpBudget? SessionListing);
    private sealed record LegacyScope(int Family, AssurancePolicyScope? AssuranceScope, string? AuthenticationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TotpPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PasskeyPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssurancePolicyVersion);
    private static string PolicyVersion() => PublishedSessionListingPresentationTests.TestPolicy().Version;
    private static IdentityConfigurationScope Scope() => new(IdentityConfigurationFamily.SessionListingContent, AuthenticationPolicyVersion: PolicyVersion());
    private static object WireScope() => new { family = "SessionListingContent", authenticationPolicyVersion = PolicyVersion() };
    private static SessionListingContentBinding Binding(RuntimeConfigurationTestEnvironment env, string version) => new(PolicyVersion(), version, "proposed-not-runtime-authority", "fixture-session-content", "fixture-session-variant", env.Interval);
    private static SessionListingContentBinding Current(RuntimeConfigurationTestEnvironment env, AuthenticationPolicy policy) => ((ISessionListingContentBindingSource)env.Source).RequireQualified(policy, env.Clock.GetUtcNow());
    private static async Task<IdentityConfigurationHistory> PublishOwner(RuntimeConfigurationTestEnvironment env, SessionListingContentBinding c, EffectivePolicyInterval? interval = null, Guid? supersedes = null)
    {
        await using var owner = env.Open(); env.Authorization.Actor = "fixture-security-author"; var p = await owner.Application.CreateSessionListingContentDraftAsync(c, "OP-create", "fixture-correlation"); p = await owner.Application.ValidateAsync(env.Command(p)); env.Authorization.Actor = "fixture-security-approver"; p = await owner.Application.ApproveAsync(env.Command(p)); env.Authorization.Actor = "fixture-security-publisher"; return await owner.Application.PublishAsync(env.Publication(p, supersedes) with { EffectiveInterval = interval ?? new(env.Clock.GetUtcNow(), true, null, false) });
    }
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement p)
    { using var response = await h.Post("/proposals/" + operation, new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, actor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone(); }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement p, Guid? supersedes = null)
    { using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone(); }
}
