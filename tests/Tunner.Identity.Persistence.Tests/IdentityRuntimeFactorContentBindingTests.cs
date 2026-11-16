using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeFactorContentBindingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualAdminPublicationFeedsAuthenticatedFactorOptionsWithReplayHistoryAndForwardRollback(bool passkey)
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using var admin = await Host.Start(db, additionalScopes: [Scope(passkey)]);
        var env = admin.Environment; var author = await admin.Browser("fixture-security-author");
        var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        var profile = new TotpTests.Profile(); var original = passkey ? PublishedPasskeyPresentationTests.ExistingBundle().Content : profile.RequirePublished(profile.Policy).Content;
        var content = new PresentationContentTestEnvironment(db, env.Clock, original.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        await content.InitializeAsync(); foreach (var (key, value) in original) _ = await content.PublishTextAsync(key, value.Text);
        object Binding(string version) => passkey
            ? new PasskeyContentBinding(PolicyVersion(true), version, "proposed-not-runtime-authority", content.Scope, content.Variant, env.Interval)
            : new TotpContentBinding(PolicyVersion(false), version, "proposed-not-runtime-authority", content.Scope, content.Variant, env.Interval);
        var command = new { idempotencyKey = Guid.NewGuid(), candidate = Binding("fixture-binding-v1") }; JsonElement proposal;
        using (var created = await admin.Post(Path(passkey), command, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post(Path(passkey), command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        admin.Owners.Denied = null;
        using (var replay = await admin.Post(Path(passkey), command, author))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        using (var conflict = await admin.Post(Path(passkey), new { command.idempotencyKey, candidate = Binding("fixture-conflict") }, author)) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var edited = await admin.Post(Path(passkey), new { idempotencyKey = Guid.NewGuid(), candidate = Binding("fixture-binding-v2"),
            proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, edited.StatusCode); proposal = (await Body(edited)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var first = await Publish(admin, publisher, proposal);
        void Compose(IServiceCollection services)
        {
            services.AddSingleton<TimeProvider>(env.Clock); services.AddSingleton<IPresentationContentProfileSource>(content);
            services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization); services.AddTunnerPresentationContent(db.ConnectionString);
            if (passkey)
            {
                services.RemoveAll<IPasskeyPresentationSource>(); services.AddTunnerPersistedPasskeyContentBinding(); services.AddTunnerPublishedPasskeyPresentation();
            }
            else
            {
                services.AddTunnerPersistedTotpContentBinding(); services.AddTunnerPublishedTotpPresentation();
                services.AddSingleton<ITotpPolicySource>(profile); services.AddSingleton<ITotpSeedStore>(profile); services.AddSingleton<ITotpMutationEligibilitySource>(profile);
                services.AddSingleton<ITotpMfaRequirementSource>(profile); services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
            }
        }
        await using var user = passkey ? await PasskeyTests.StartRecoveryFixtureAsync(db, Compose) : await AuthenticationTestHost.Start(db, Compose);
        Assert.Equal(user.Clock.GetUtcNow(), env.Clock.GetUtcNow()); await user.SeedAsync(db); var cookies = await TotpTests.Login(user);
        using (var options = await user.Get(passkey ? "/account/passkeys/options" : "/account/totp/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var body = await Body(options);
            await using var owner = content.Open(); var resolved = owner.Application.Resolve(new(content.Scope, content.Variant, env.Clock.GetUtcNow()));
            Assert.Equal(original.Count, body.GetProperty("content").EnumerateObject().Count());
            foreach (var item in resolved.Content.Values)
            {
                var actual = body.GetProperty("content").GetProperty(item.Key); Assert.Equal(item.Text, actual.GetProperty("text").GetString());
                Assert.Equal(item.VersionReference, actual.GetProperty("versionRef").GetString()); Assert.Equal(item.PublicationReference, actual.GetProperty("publicationRef").GetString());
            }
        }
        var qualified = Current(env, passkey); Assert.Equal("fixture-binding-v2", qualified.Version); Assert.NotEqual("proposed-not-runtime-authority", qualified.Qualification);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var created = await admin.Post(Path(passkey), new { idempotencyKey = Guid.NewGuid(), candidate = Binding("fixture-binding-v3") }, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var latest = await Publish(admin, publisher, proposal, first.GetProperty("publicationReference").GetGuid()); Assert.Equal("fixture-binding-v3", Current(env, passkey).Version);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = latest.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow()) }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("fixture-binding-v2", Current(env, passkey).Version);
        using (var history = await admin.Post("/history/preview", new { scope = WireScope(passkey), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal("fixture-binding-v2", (await Body(history)).GetProperty(passkey ? "passkeyContent" : "totpContent").GetProperty("configurationVersionReference").GetString()); }
        await using var unchanged = db.Context(); Assert.Empty(await unchanged.Set<PasskeyCredentialRow>().ToArrayAsync()); Assert.Empty(await unchanged.Set<PasskeyCeremonyRow>().ToArrayAsync());
        Assert.Empty(await unchanged.Set<TotpAuthenticatorRow>().ToArrayAsync()); Assert.Empty(await unchanged.Set<TotpOperationRow>().ToArrayAsync()); Assert.Equal(0, profile.Creates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingApprovedBindingInvalidatesApprovalAndCannotCrossPolicyScope(bool passkey)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var owner = env.Open();
        var proposal = await Create(owner.Application, env, passkey, "fixture-binding-v1");
        Assert.Throws<IdentityConfigurationUnavailableException>(() => Current(env, passkey));
        proposal = await owner.Application.ValidateAsync(env.Command(proposal));
        await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => owner.Application.ApproveAsync(env.Command(proposal)));
        env.Authorization.Actor = "fixture-security-approver"; proposal = await owner.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-author";
        proposal = passkey ? await owner.Application.UpdatePasskeyContentDraftAsync(proposal.ProposalReference, proposal.Revision, PasskeyBinding(env, "fixture-binding-v2"), "OP-edit", "fixture-correlation")
            : await owner.Application.UpdateTotpContentDraftAsync(proposal.ProposalReference, proposal.Revision, TotpBinding(env, "fixture-binding-v2"), "OP-edit", "fixture-correlation");
        Assert.Equal("DRAFT", proposal.State); env.Authorization.Actor = "fixture-security-publisher";
        await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => owner.Application.PublishAsync(env.Publication(proposal)));
        env.Authorization.Actor = "fixture-security-author"; proposal = await owner.Application.ValidateAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-approver"; proposal = await owner.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-publisher"; var publication = await owner.Application.PublishAsync(env.Publication(proposal));
        Assert.Equal("fixture-binding-v2", Current(env, passkey).Version);
        var row = await owner.Database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().SingleAsync(x => x.Id == publication.PublicationReference);
        Assert.Equal(row.ApprovalEventReference.ToString("N"), Current(env, passkey).Qualification);
        var otherScope = passkey ? Scope(passkey) with { PasskeyPolicyVersion = "other-policy" } : Scope(passkey) with { TotpPolicyVersion = "other-policy" };
        await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => owner.Application.HistoryPreviewAsync(otherScope, publication.PublicationReference, "OP-wrong-scope", "fixture-correlation"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeBindingRequiresExactPolicyAndEffectivePublication(bool passkey)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var end = env.Clock.GetUtcNow().AddSeconds(2); var interval = new EffectivePolicyInterval(env.Clock.GetUtcNow(), true, end, false);
        await using var owner = env.Open(); var proposal = passkey
            ? await owner.Application.CreatePasskeyContentDraftAsync(PasskeyBinding(env, "fixture-bounded") with { EffectiveInterval = interval, PasskeyPolicyVersion = "other-policy" }, "OP-create", "fixture-correlation")
            : await owner.Application.CreateTotpContentDraftAsync(TotpBinding(env, "fixture-bounded") with { EffectiveInterval = interval, TotpPolicyVersion = "other-policy" }, "OP-create", "fixture-correlation");
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        proposal = await owner.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
        _ = await owner.Application.PublishAsync(env.Publication(proposal) with { EffectiveInterval = interval });
        Assert.Throws<IdentityConfigurationUnavailableException>(() => Current(env, passkey));
        env.Authorization.Actor = "fixture-security-author"; proposal = await Create(owner.Application, env, passkey, "fixture-current");
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        proposal = await owner.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
        _ = await owner.Application.PublishAsync(env.Publication(proposal) with { EffectiveInterval = interval }); Assert.Equal("fixture-current", Current(env, passkey).Version);
        env.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.Throws<IdentityConfigurationUnavailableException>(() => Current(env, passkey));
    }

    [Theory]
    [InlineData(false, "policy")]
    [InlineData(false, "configuration")]
    [InlineData(false, "qualification")]
    [InlineData(false, "scope")]
    [InlineData(false, "variant")]
    [InlineData(false, "interval")]
    [InlineData(true, "policy")]
    [InlineData(true, "configuration")]
    [InlineData(true, "qualification")]
    [InlineData(true, "scope")]
    [InlineData(true, "variant")]
    [InlineData(true, "interval")]
    public async Task IncompleteBindingFailsBeforeOwnerEffects(bool passkey, string field)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var owner = env.Open();
        var t = TotpBinding(env, "fixture-invalid"); var p = PasskeyBinding(env, "fixture-invalid");
        t = field switch { "policy" => t with { TotpPolicyVersion = "" }, "configuration" => t with { ConfigurationVersionReference = "" }, "qualification" => t with { QualificationReference = "" }, "scope" => t with { ContentScope = "" }, "variant" => t with { VariantReference = "" }, _ => t with { EffectiveInterval = null! } };
        p = field switch { "policy" => p with { PasskeyPolicyVersion = "" }, "configuration" => p with { ConfigurationVersionReference = "" }, "qualification" => p with { QualificationReference = "" }, "scope" => p with { ContentScope = "" }, "variant" => p with { VariantReference = "" }, _ => p with { EffectiveInterval = null! } };
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => passkey ? owner.Application.CreatePasskeyContentDraftAsync(p, "OP-invalid", "fixture-correlation") : owner.Application.CreateTotpContentDraftAsync(t, "OP-invalid", "fixture-correlation"));
        await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictHttpScopeAndSixFieldPayloadRejectExtraneousDimensionsAndLegalFields(bool passkey)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [Scope(passkey)]); var actor = await admin.Browser("fixture-security-author");
        using (var legacy = await admin.Post("/proposals/search", new { scope = new { family = "Authentication", totpPolicyVersion = "wrong" }, pageSize = 1, offset = 0 }, actor)) Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);
        using (var crossed = await admin.Post("/proposals/search", new { scope = new { family = passkey ? "PasskeyContent" : "TotpContent", totpPolicyVersion = "wrong", passkeyPolicyVersion = "wrong" }, pageSize = 1, offset = 0 }, actor)) Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        object invalid = passkey ? new { passkeyPolicyVersion = PolicyVersion(true), configurationVersionReference = "fixture-invalid", qualificationReference = "fixture-only", contentScope = "scope", variantReference = "variant", effectiveInterval = Interval(admin.Environment.Clock.GetUtcNow()), termsUri = "https://fixture.example.test/terms" }
            : new { totpPolicyVersion = PolicyVersion(false), configurationVersionReference = "fixture-invalid", qualificationReference = "fixture-only", contentScope = "scope", variantReference = "variant", effectiveInterval = Interval(admin.Environment.Clock.GetUtcNow()), termsUri = "https://fixture.example.test/terms" };
        using (var extra = await admin.Post(Path(passkey), new { idempotencyKey = Guid.NewGuid(), candidate = invalid }, actor)) Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        object mismatched = passkey ? PasskeyBinding(admin.Environment, "fixture-wrong-scope") with { PasskeyPolicyVersion = "unqualified-policy" } : TotpBinding(admin.Environment, "fixture-wrong-scope") with { TotpPolicyVersion = "unqualified-policy" };
        using var wrong = await admin.Post(Path(passkey), new { idempotencyKey = Guid.NewGuid(), candidate = mismatched }, actor); Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ReconstructedSevenFamilyPayloadAndScopeBytesRemainUnchanged(int ordinal)
    {
        // RECONSTRUCTED from committed91fe8696 format with explicit synthetic fixture values; not retained historic DB bytes.
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var family = (IdentityConfigurationFamily)ordinal;
        var registration = IdentityRuntimeAdditionalConfigurationTests.Registration(env, "fixture-registration-v2");
        var legacy = new LegacyDocument(ordinal == 0 ? env.Authentication("fixture-old") : null, ordinal == 1 ? env.Assurance("fixture-old") : null, ordinal == 2 ? env.Binding("fixture-old") : null,
            ordinal == 3 ? registration : null, ordinal == 4 ? IdentityRuntimeAdditionalConfigurationTests.Totp(env, "fixture-old") : null,
            ordinal == 5 ? new(registration.Values.Version, registration.Values.TermsVersion, registration.Values.PrivacyVersion, "fixture-old", "fixture-qualification", "fixture-content", "fixture-variant", env.Interval, new("https://fixture.example.test/terms"), new("https://fixture.example.test/privacy"), null) : null,
            ordinal == 6 ? new(new("fixture-passkey-v1", "ACTIVE", "PASSKEY", env.Interval, null, ["SYNTHETIC-ONLY"]), PasskeyValues()) : null);
        var golden = JsonSerializer.Serialize(legacy); var current = IdentityConfigurationDocument.Read(golden);
        Assert.Equal(Encoding.UTF8.GetBytes(golden), Encoding.UTF8.GetBytes(current.Snapshot)); Assert.Equal(Sha(golden), current.Digest); Assert.Equal(family, current.Scope.Family);
        var scope = current.Scope; var goldenScope = JsonSerializer.Serialize(new LegacyScope(ordinal, scope.AssuranceScope, scope.AuthenticationPolicyVersion, scope.RegistrationPolicyVersion));
        Assert.Equal(goldenScope, scope.Key); Assert.Equal(Sha(goldenScope), Sha(scope.Key)); Assert.Null(current.TotpContent); Assert.Null(current.PasskeyContent);
        Assert.Throws<IdentityConfigurationUnavailableException>(() => (scope with { TotpPolicyVersion = "unexpected" }).Require());
        Assert.Throws<IdentityConfigurationUnavailableException>(() => (scope with { PasskeyPolicyVersion = "unexpected" }).Require());
        Assert.Equal(7, (int)IdentityConfigurationFamily.TotpContent); Assert.Equal(8, (int)IdentityConfigurationFamily.PasskeyContent);
    }

    [Fact]
    public void BindingPortsRequireSeparateOptInsWithoutPolicyPresentationOrWorkflowActivation()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration();
        Assert.DoesNotContain(old, x => x.ServiceType == typeof(ITotpContentBindingSource) || x.ServiceType == typeof(IPasskeyContentBindingSource));
        var services = new ServiceCollection(); services.AddTunnerPersistedTotpContentBinding();
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IPasskeyContentBindingSource)); services.AddTunnerPersistedPasskeyContentBinding();
        Assert.Contains(services, x => x.ServiceType == typeof(ITotpContentBindingSource)); Assert.Contains(services, x => x.ServiceType == typeof(IPasskeyContentBindingSource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(ITotpPolicySource) || x.ServiceType == typeof(IPasskeyPolicySource) || x.ServiceType == typeof(ITotpPresentationSource) || x.ServiceType == typeof(IPasskeyPresentationSource) || x.ServiceType == typeof(TotpApplication) || x.ServiceType == typeof(PasskeyApplication));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedTotpContentBinding()); Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedPasskeyContentBinding());
    }

    private sealed record LegacyDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationPolicyVersion? Registration,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpPolicyVersion? Totp,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationContentBinding? RegistrationContent,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyConfigurationVersion? Passkey);
    private sealed record LegacyScope(int Family, AssurancePolicyScope? AssuranceScope, string? AuthenticationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion);
    private static PasskeyConfigurationValues PasskeyValues() => new("fixture-passkey-v1", "authentication.example.test", AuthenticationTestHost.Origin, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), PasskeyRevocationSessionEffect.PreserveExistingSessions,
        "required", new[] { -7 }, 32, 32768, 4, 4, "fixture-enrollment-v1", "fixture-revocation-v1", "fixture-processing", "fixture-retention", "required", "none",
        new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-passkey-assurance", [PasskeyPolicy.AssuranceKey(true, false)] = "fixture-syncable-unbacked-assurance", [PasskeyPolicy.AssuranceKey(true, true)] = "fixture-syncable-backed-assurance" });
    private static string PolicyVersion(bool passkey) => passkey ? PublishedPasskeyPresentationTests.TestPolicy().Version : new TotpTests.Profile().Policy.Version;
    private static IdentityConfigurationScope Scope(bool passkey) => passkey ? new(IdentityConfigurationFamily.PasskeyContent, PasskeyPolicyVersion: PolicyVersion(true)) : new(IdentityConfigurationFamily.TotpContent, TotpPolicyVersion: PolicyVersion(false));
    private static object WireScope(bool passkey) => passkey ? new { family = "PasskeyContent", passkeyPolicyVersion = PolicyVersion(true) } : new { family = "TotpContent", totpPolicyVersion = PolicyVersion(false) };
    private static string Path(bool passkey) => passkey ? "/passkey-content/draft" : "/totp-content/draft";
    private static TotpContentBinding TotpBinding(RuntimeConfigurationTestEnvironment env, string version) => new(PolicyVersion(false), version, "proposed-not-runtime-authority", "fixture-factor-content", "fixture-factor-variant", env.Interval);
    private static PasskeyContentBinding PasskeyBinding(RuntimeConfigurationTestEnvironment env, string version) => new(PolicyVersion(true), version, "proposed-not-runtime-authority", "fixture-factor-content", "fixture-factor-variant", env.Interval);
    private static Task<IdentityConfigurationProposal> Create(IdentityRuntimeConfigurationApplication app, RuntimeConfigurationTestEnvironment env, bool passkey, string version) => passkey
        ? app.CreatePasskeyContentDraftAsync(PasskeyBinding(env, version), "OP-create", "fixture-correlation") : app.CreateTotpContentDraftAsync(TotpBinding(env, version), "OP-create", "fixture-correlation");
    private static (string Version, string Qualification) Current(RuntimeConfigurationTestEnvironment env, bool passkey)
    {
        if (passkey) { var b = env.Source.RequireQualified(PublishedPasskeyPresentationTests.TestPolicy(), env.Clock.GetUtcNow()); return (b.ConfigurationVersionReference, b.QualificationReference); }
        var t = env.Source.RequireQualified(new TotpTests.Profile().Policy, env.Clock.GetUtcNow()); return (t.ConfigurationVersionReference, t.QualificationReference);
    }
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement proposal)
    {
        using var response = await h.Post("/proposals/" + operation, new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement proposal, Guid? supersedes = null)
    {
        using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
}
