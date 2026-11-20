using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeRegistrationContentBindingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task BindingUpdateResetsApprovalAndOnlyExactApprovedPublicationSuppliesRuntimeQualification()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var policy = Policy(env);
        await using var owner = env.Open(); var binding = Binding(env, policy.Values, "fixture-binding-v1");
        var proposal = await owner.Application.CreateRegistrationContentDraftAsync(binding, "OP-binding-create", "fixture-correlation");
        Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireQualified(policy.Values, env.Clock.GetUtcNow()));
        proposal = await owner.Application.ValidateAsync(env.Command(proposal));
        await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => owner.Application.ApproveAsync(env.Command(proposal)));
        env.Authorization.Actor = "fixture-security-approver"; proposal = await owner.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-author"; binding = binding with { ConfigurationVersionReference = "fixture-binding-v2" };
        proposal = await owner.Application.UpdateRegistrationContentDraftAsync(proposal.ProposalReference, proposal.Revision, binding, "OP-binding-edit", "fixture-correlation");
        Assert.Equal("DRAFT", proposal.State); env.Authorization.Actor = "fixture-security-publisher";
        await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => owner.Application.PublishAsync(env.Publication(proposal)));
        env.Authorization.Actor = "fixture-security-author"; proposal = await owner.Application.ValidateAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-approver"; proposal = await owner.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-publisher"; var publication = await owner.Application.PublishAsync(env.Publication(proposal));
        var current = env.Source.RequireQualified(policy.Values, env.Clock.GetUtcNow());
        Assert.Equal(binding.ConfigurationVersionReference, current.ConfigurationVersionReference); Assert.Equal(binding.TermsVersion, current.TermsVersion);
        Assert.Equal(binding.PrivacyVersion, current.PrivacyVersion); Assert.NotEqual(binding.QualificationReference, current.QualificationReference);
        var receipt = await owner.Database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().SingleAsync(x => x.Id == publication.PublicationReference);
        Assert.Equal(receipt.ApprovalEventReference.ToString("N"), current.QualificationReference);
        var preview = await owner.Application.HistoryPreviewAsync(Scope(policy.Values), publication.PublicationReference, "OP-binding-history", "fixture-correlation");
        Assert.Equal(JsonSerializer.Serialize(binding), JsonSerializer.Serialize(preview.RegistrationContent)); Assert.Null(preview.ContentBinding); Assert.Null(preview.Registration);
        await using var platform = env.Platform(); Assert.All(await platform.AuditEvents.ToArrayAsync(), x => Assert.True(x.VerifyIntegrity()));
        Assert.All(await platform.OutboxMessages.ToArrayAsync(), x => Assert.DoesNotContain("TermsUri", x.Payload, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ActualAdminBindingCommandsFeedPublishedRegistrationOptionsAndSupportCurrentReplayAndProspectiveRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var seed = new RuntimeConfigurationTestEnvironment(db); var policy = Policy(seed);
        await using var admin = await Host.Start(db, additionalScopes: [Scope(policy.Values)]);
        var env = admin.Environment; var author = await admin.Browser("fixture-security-author");
        var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        var original = new RegistrationBffTests.Presentation().RequirePublished(policy.Values);
        var content = new PresentationContentTestEnvironment(db, env.Clock, original.Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        await content.InitializeAsync(); foreach (var (key, value) in original.Content) _ = await content.PublishTextAsync(key, value.Text);
        var binding = Binding(env, policy.Values, "fixture-http-binding-v1") with
        { ContentScope = content.Scope, VariantReference = content.Variant, TermsUri = original.TermsUri, PrivacyUri = original.PrivacyUri, SupportUri = original.SupportUri };
        var command = new { idempotencyKey = Guid.NewGuid(), candidate = binding }; JsonElement proposal;
        using (var created = await admin.Post("/registration-content/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        using (var cap = await admin.Post("/capabilities", new { scope = WireScope(policy.Values), target = "Proposal", proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, cap.StatusCode); Assert.Contains("UpdateDraft", (await Body(cap)).GetProperty("availableActions").EnumerateArray().Select(x => x.GetString())); }
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post("/registration-content/draft", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        admin.Owners.Denied = null;
        using (var replay = await admin.Post("/registration-content/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        binding = binding with { ConfigurationVersionReference = "fixture-http-binding-v2" };
        using (var edited = await admin.Post("/registration-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = binding,
            proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, edited.StatusCode); proposal = (await Body(edited)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var first = await Publish(admin, publisher, proposal);
        await using var user = await AuthenticationTestHost.Start(db, services => ComposeUser(services, db, env, content, policy));
        using (var options = await user.Get("/registration/options"))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var body = await Body(options);
            Assert.Equal(policy.Values.TermsVersion, body.GetProperty("termsVersion").GetString()); Assert.Equal(policy.Values.PrivacyVersion, body.GetProperty("privacyVersion").GetString());
            Assert.Equal(binding.PrivacyUri.AbsoluteUri, body.GetProperty("privacyUri").GetString());
            await using var owner = content.Open(); var resolved = owner.Application.Resolve(new(content.Scope, content.Variant, env.Clock.GetUtcNow()));
            Assert.Equal(original.Content.Count, body.GetProperty("content").EnumerateObject().Count());
            foreach (var expected in resolved.Content.Values)
            {
                var projected = body.GetProperty("content").GetProperty(expected.Key);
                Assert.Equal(expected.VersionReference, projected.GetProperty("versionRef").GetString());
                Assert.Equal(expected.PublicationReference, projected.GetProperty("publicationRef").GetString()); Assert.Equal(expected.Text, projected.GetProperty("text").GetString());
            }
        }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); var replacement = binding with { ConfigurationVersionReference = "fixture-http-binding-v3", PrivacyUri = new("https://fixture.example.test/privacy-revised") };
        using (var created = await admin.Post("/registration-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = replacement }, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var latest = await Publish(admin, publisher, proposal, first.GetProperty("publicationReference").GetGuid());
        using (var options = await user.Get("/registration/options"))
        { Assert.Equal(HttpStatusCode.OK, options.StatusCode); Assert.Equal(replacement.PrivacyUri.AbsoluteUri, (await Body(options)).GetProperty("privacyUri").GetString()); }
        env.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = latest.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow()) }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        using (var options = await user.Get("/registration/options"))
        { Assert.Equal(HttpStatusCode.OK, options.StatusCode); Assert.Equal(binding.PrivacyUri.AbsoluteUri, (await Body(options)).GetProperty("privacyUri").GetString()); }
        using (var history = await admin.Post("/history/preview", new { scope = WireScope(policy.Values), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal("fixture-http-binding-v2", (await Body(history)).GetProperty("registrationContent").GetProperty("configurationVersionReference").GetString()); }
        await using var unchanged = db.Context(); Assert.Empty(await unchanged.Accounts.ToArrayAsync()); Assert.Empty(await unchanged.Set<AccountPersonalInformationRow>().ToArrayAsync());
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("terms")]
    [InlineData("privacy")]
    public async Task PublishedBindingCannotQualifyAnotherCurrentPolicyOrNoticeIdentity(string mismatch)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var policy = Policy(env).Values;
        _ = await PublishOwner(env, Binding(env, policy, "fixture-notice-binding"));
        var changed = mismatch switch { "policy" => policy with { Version = "fixture-other-policy" }, "terms" => policy with { TermsVersion = "fixture-other-terms" }, _ => policy with { PrivacyVersion = "fixture-other-privacy" } };
        Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireQualified(changed, env.Clock.GetUtcNow()));
        Assert.Equal(policy.TermsVersion, env.Source.RequireQualified(policy, env.Clock.GetUtcNow()).TermsVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingAndPublicationIntervalsControlRuntimeAvailability(bool future)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var policy = Policy(env).Values;
        var start = future ? env.Clock.GetUtcNow().AddSeconds(1) : env.Clock.GetUtcNow(); var end = start.AddSeconds(2);
        var binding = Binding(env, policy, "fixture-bounded-binding") with { EffectiveInterval = new(start, true, end, false) };
        _ = await PublishOwner(env, binding, new(start, true, end, false));
        if (future) { Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireQualified(policy, env.Clock.GetUtcNow())); env.Clock.Advance(TimeSpan.FromSeconds(1)); }
        Assert.Equal(binding.ConfigurationVersionReference, env.Source.RequireQualified(policy, env.Clock.GetUtcNow()).ConfigurationVersionReference);
        env.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireQualified(policy, env.Clock.GetUtcNow()));
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("missing-link")]
    [InlineData("credential-link")]
    public async Task OwnerRejectsIncompleteOrUnsafeBindingBeforeWritingEffects(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var binding = Binding(env, Policy(env).Values, "fixture-invalid-binding");
        binding = failure switch { "reference" => binding with { PrivacyVersion = "" }, "missing-link" => binding with { TermsUri = null! }, _ => binding with { TermsUri = new("https://fixture:private@fixture.example.test/legal") } };
        await using var owner = env.Open(); await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => owner.Application.CreateRegistrationContentDraftAsync(binding, "OP-invalid-binding", "fixture-correlation"));
        await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Authentication)]
    [InlineData(IdentityConfigurationFamily.Assurance)]
    [InlineData(IdentityConfigurationFamily.AuthenticationContent)]
    [InlineData(IdentityConfigurationFamily.Registration)]
    [InlineData(IdentityConfigurationFamily.Totp)]
    public async Task Reconstructed397FiveFamilyScopeAndPayloadBytesRemainUnchanged(IdentityConfigurationFamily family)
    {
        // RECONSTRUCTION from committed39747663 source format, not a retained historic execution/database.
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var legacy = new LegacyDocument(family == IdentityConfigurationFamily.Authentication ? env.Authentication("fixture-legacy") : null,
            family == IdentityConfigurationFamily.Assurance ? env.Assurance("fixture-legacy") : null,
            family == IdentityConfigurationFamily.AuthenticationContent ? env.Binding("fixture-legacy") : null,
            family == IdentityConfigurationFamily.Registration ? IdentityRuntimeAdditionalConfigurationTests.Registration(env, "fixture-legacy") : null,
            family == IdentityConfigurationFamily.Totp ? IdentityRuntimeAdditionalConfigurationTests.Totp(env, "fixture-legacy") : null);
        var golden = JsonSerializer.Serialize(legacy); var current = IdentityConfigurationDocument.Read(golden);
        Assert.Equal(Encoding.UTF8.GetBytes(golden), Encoding.UTF8.GetBytes(current.Snapshot)); Assert.Equal(Sha(golden), current.Digest);
        Assert.Equal((int)family, family switch { IdentityConfigurationFamily.Authentication => 0, IdentityConfigurationFamily.Assurance => 1, IdentityConfigurationFamily.AuthenticationContent => 2, IdentityConfigurationFamily.Registration => 3, _ => 4 });
        var scope = current.Scope; var goldenScope = JsonSerializer.Serialize(new { Family = (int)family, scope.AssuranceScope, scope.AuthenticationPolicyVersion });
        Assert.Equal(Encoding.UTF8.GetBytes(goldenScope), Encoding.UTF8.GetBytes(scope.Key)); Assert.Equal(Sha(goldenScope), Sha(scope.Key));
        Assert.Null(current.RegistrationContent); Assert.Null(scope.RegistrationPolicyVersion);
        Assert.Throws<IdentityConfigurationUnavailableException>(() => (scope with { RegistrationPolicyVersion = "unexpected-new-dimension" }).Require());
    }

    [Fact]
    public void BindingSourceOptInDoesNotRegisterPolicyPresentationOrWorkflowAndRejectsDuplicateSources()
    {
        var services = new ServiceCollection(); services.AddTunnerPersistedRegistrationContentBinding();
        Assert.Contains(services, x => x.ServiceType == typeof(IRegistrationContentBindingSource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IRegistrationPolicySource) || x.ServiceType == typeof(IRegistrationPresentationSource) || x.ServiceType == typeof(IRegistrationApplication));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedRegistrationContentBinding());
        var existing = new ServiceCollection(); existing.AddTunnerPersistedIdentityRuntimeConfiguration();
        Assert.DoesNotContain(existing, x => x.ServiceType == typeof(IRegistrationContentBindingSource) || x.ServiceType == typeof(IRegistrationPolicySource));
    }

    [Fact]
    public async Task NewScopeDimensionIsRejectedOnLegacyHttpScopeAndBindingRequiresItsExactScope()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db);
        var author = await admin.Browser("fixture-security-author"); var policy = Policy(admin.Environment).Values;
        using (var invalid = await admin.Post("/proposals/search", new { scope = new { family = "Authentication", registrationPolicyVersion = policy.Version }, pageSize = 1, offset = 0 }, author))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var unavailable = await admin.Post("/registration-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = Binding(admin.Environment, policy, "fixture-out-of-scope") }, author);
        Assert.Equal(HttpStatusCode.Forbidden, unavailable.StatusCode);
    }

    private sealed record LegacyDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationPolicyVersion? Registration,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpPolicyVersion? Totp);
    private static RegistrationPolicyVersion Policy(RuntimeConfigurationTestEnvironment env) => IdentityRuntimeAdditionalConfigurationTests.Registration(env, "fixture-registration-v2");
    private static IdentityConfigurationScope Scope(RegistrationPolicy policy) => new(IdentityConfigurationFamily.RegistrationContent, RegistrationPolicyVersion: policy.Version);
    private static object WireScope(RegistrationPolicy policy) => new { family = "RegistrationContent", registrationPolicyVersion = policy.Version };
    private static RegistrationContentBinding Binding(RuntimeConfigurationTestEnvironment env, RegistrationPolicy policy, string version) => new(policy.Version, policy.TermsVersion, policy.PrivacyVersion,
        version, "client-proposed-qualification-is-not-authority", "fixture-registration-content", "fixture-registration-variant", env.Interval,
        new("https://fixture.example.test/terms"), new("https://fixture.example.test/privacy"), null);
    private static async Task<IdentityConfigurationHistory> PublishOwner(RuntimeConfigurationTestEnvironment env, RegistrationContentBinding binding, Tunner.Platform.Policies.EffectivePolicyInterval? interval = null)
    {
        await using var owner = env.Open(); env.Authorization.Actor = "fixture-security-author";
        var proposal = await owner.Application.CreateRegistrationContentDraftAsync(binding, "OP-binding-create", "fixture-correlation");
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        proposal = await owner.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
        return await owner.Application.PublishAsync(env.Publication(proposal) with
        { EffectiveInterval = interval ?? new(env.Clock.GetUtcNow(), true, binding.EffectiveInterval.Until, binding.EffectiveInterval.IncludesUntil) });
    }
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static object Write(JsonElement p) => new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() };
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement proposal)
    { using var response = await h.Post("/proposals/" + operation, Write(proposal), actor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone(); }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement proposal, Guid? supersedes = null)
    {
        using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(),
            expectedRevision = proposal.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static void ComposeUser(IServiceCollection services, IdentityTestDatabase db, RuntimeConfigurationTestEnvironment env, PresentationContentTestEnvironment content, RegistrationPolicyVersion policy)
    {
        services.AddSingleton<TimeProvider>(env.Clock); services.AddSingleton<IPresentationContentProfileSource>(content); services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization);
        services.AddTunnerPresentationContent(db.ConnectionString); services.AddTunnerPersistedRegistrationContentBinding(); services.AddTunnerPublishedRegistrationPresentation();
        // Only this isolated test explicitly activates existing Registration providers; binding opt-in itself does not.
        services.AddSingleton<IRegistrationPolicySource>(new RegistrationPolicyCatalog([policy])); var age = new FixtureAccountAgePolicies();
        services.AddSingleton<IAccountAgePolicySource>(age); services.AddSingleton<IAccountConsentEvidenceSource>(age);
        services.AddSingleton<IRegistrationPasswordBlocklist, Blocklist>(); services.AddSingleton<IRegistrationNotificationDispatcher, Notices>();
        services.AddSingleton(_ => new RegistrationNetworkPartitionKey(RandomNumberGenerator.GetBytes(32)));
    }
    private sealed class Blocklist : IRegistrationPasswordBlocklist
    {
        public ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(false); }
    }
    private sealed class Notices : IRegistrationNotificationDispatcher
    {
        public Task QueueAsync(RegistrationNotification notification, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
}
