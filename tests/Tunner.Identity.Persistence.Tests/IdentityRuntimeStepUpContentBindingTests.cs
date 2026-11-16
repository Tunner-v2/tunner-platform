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

public sealed class IdentityRuntimeStepUpContentBindingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ActualAdminBindingPublicationFeedsSharedStepUpOptionsWithCurrentReplayHistoryAndForwardRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await PublishedStepUpPresentationIntegrationTests.InitializeTargetAsync(db);
        await using var admin = await Host.Start(db, additionalScopes: [Scope()]); var env = admin.Environment;
        var author = await admin.Browser("fixture-security-author"); var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        var original = PublishedStepUpPresentationTests.ExistingBundle(env.Clock); var content = new PresentationContentTestEnvironment(db, env.Clock, original.Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        await content.InitializeAsync(); foreach (var (key, item) in original.Content) _ = await content.PublishTextAsync(key, item.Text);
        var binding = Binding(env, "fixture-stepup-binding-v1") with { ContentScope = content.Scope, VariantReference = content.Variant };
        var command = new { idempotencyKey = Guid.NewGuid(), candidate = binding }; JsonElement proposal;
        using (var created = await admin.Post("/step-up-content/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post("/step-up-content/draft", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        admin.Owners.Denied = null;
        using (var replay = await admin.Post("/step-up-content/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        using (var conflict = await admin.Post("/step-up-content/draft", new { command.idempotencyKey, candidate = binding with { ConfigurationVersionReference = "changed-body" } }, author)) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var edited = await admin.Post("/step-up-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-stepup-binding-v2" },
            proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, edited.StatusCode); proposal = (await Body(edited)).GetProperty("result").Clone(); }
        using (var cap = await admin.Post("/capabilities", new { scope = WireScope(), target = "Proposal", proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, cap.StatusCode); Assert.Contains("UpdateDraft", (await Body(cap)).GetProperty("availableActions").EnumerateArray().Select(x => x.GetString())); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal); var first = await Publish(admin, publisher, proposal);
        var policy = PublishedStepUpPresentationTests.Policy(env.Clock); var current = env.Source.RequireQualified(policy, env.Clock.GetUtcNow());
        Assert.Equal("fixture-stepup-binding-v2", current.ConfigurationVersionReference); Assert.NotEqual(binding.QualificationReference, current.QualificationReference);
        await using var user = await AuthenticationTestHost.Start(db, services =>
        {
            services.AddSingleton<TimeProvider>(env.Clock); PublishedStepUpPresentationIntegrationTests.AddContent(services, db, content);
            services.AddSingleton<IAssurancePolicySource>(new AssurancePolicyCatalog([policy])); services.AddSingleton<IUserStepUpOperationSource>(new StepUpBrowserTests.Owner(db));
            services.AddTunnerPersistedStepUpContentBinding(); services.AddTunnerPublishedStepUpPresentation();
        });
        Assert.Equal(user.Clock.GetUtcNow(), env.Clock.GetUtcNow()); await user.SeedAsync(db); var cookies = await TotpTests.Login(user);
        using (var options = await user.Get("/authentication/step-up/options", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var body = await Body(options); Assert.Equal("PENDING", body.GetProperty("status").GetString());
            Assert.True(body.GetProperty("passwordAvailable").GetBoolean()); Assert.False(body.GetProperty("totpAvailable").GetBoolean()); Assert.False(body.GetProperty("passkeyAvailable").GetBoolean());
            await using var owner = content.Open(); var resolved = owner.Application.Resolve(new(content.Scope, content.Variant, env.Clock.GetUtcNow()));
            foreach (var item in resolved.Content.Values)
            { var projected = body.GetProperty("content").GetProperty(item.Key); Assert.Equal(item.Text, projected.GetProperty("text").GetString()); Assert.Equal(item.VersionReference, projected.GetProperty("versionRef").GetString()); Assert.Equal(item.PublicationReference, projected.GetProperty("publicationRef").GetString()); }
        }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var created = await admin.Post("/step-up-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = binding with { ConfigurationVersionReference = "fixture-stepup-binding-v3" } }, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var latest = await Publish(admin, publisher, proposal, first.GetProperty("publicationReference").GetGuid()); Assert.Equal("fixture-stepup-binding-v3", env.Source.RequireQualified(policy, env.Clock.GetUtcNow()).ConfigurationVersionReference);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(), supersedesPublicationReference = latest.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow()) }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("fixture-stepup-binding-v2", env.Source.RequireQualified(policy, env.Clock.GetUtcNow()).ConfigurationVersionReference);
        using (var historical = await admin.Post("/history/preview", new { scope = WireScope(), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, historical.StatusCode); Assert.Equal("fixture-stepup-binding-v2", (await Body(historical)).GetProperty("stepUpContent").GetProperty("configurationVersionReference").GetString()); }
        await PublishedStepUpPresentationIntegrationTests.AssertNoProofAsync(db);
    }

    [Fact]
    public async Task UpdatingApprovedBindingResetsApprovalAndRuntimeUsesActualStoredQualification()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var owner = env.Open();
        var binding = Binding(env, "fixture-binding-v1"); var policy = PublishedStepUpPresentationTests.Policy(env.Clock);
        var proposal = await owner.Application.CreateStepUpContentDraftAsync(binding, "OP-create", "fixture-correlation");
        Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireQualified(policy, env.Clock.GetUtcNow()));
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => owner.Application.ApproveAsync(env.Command(proposal)));
        env.Authorization.Actor = "fixture-security-approver"; proposal = await owner.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-author";
        proposal = await owner.Application.UpdateStepUpContentDraftAsync(proposal.ProposalReference, proposal.Revision, binding with { ConfigurationVersionReference = "fixture-binding-v2" }, "OP-update", "fixture-correlation"); Assert.Equal("DRAFT", proposal.State);
        await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => owner.Application.PublishAsync(env.Publication(proposal)));
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver"; proposal = await owner.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-publisher"; var published = await owner.Application.PublishAsync(env.Publication(proposal)); var result = env.Source.RequireQualified(policy, env.Clock.GetUtcNow());
        var row = await owner.Database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().SingleAsync(x => x.Id == published.PublicationReference);
        Assert.Equal("fixture-binding-v2", result.ConfigurationVersionReference); Assert.Equal(row.ApprovalEventReference.ToString("N"), result.QualificationReference);
        var preview = await owner.Application.HistoryPreviewAsync(Scope(), published.PublicationReference, "OP-history", "fixture-correlation"); Assert.NotNull(preview.StepUpContent); Assert.Null(preview.Assurance);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("context")]
    [InlineData("version")]
    [InlineData("inactive")]
    public async Task RuntimeRequiresExactActiveAssurancePolicyAndBothScopeIdentifiers(string mismatch)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var policy = PublishedStepUpPresentationTests.Policy(env.Clock);
        var publication = await PublishOwner(env, Binding(env, "fixture-bound"));
        var scope = mismatch == "operation" ? new AssurancePolicyScope("other-operation", policy.Metadata.Scope.Context)
            : mismatch == "context" ? new AssurancePolicyScope(policy.Metadata.Scope.Operation, "other-context") : policy.Metadata.Scope;
        var changed = new AssurancePolicyVersion(new(mismatch == "version" ? "other-version" : policy.Metadata.VersionId, mismatch == "inactive" ? "DRAFT" : "ACTIVE", scope,
            policy.Metadata.EffectiveInterval, null, ["SYNTHETIC-ONLY"]), policy.RequiredAssurance, policy.ProofLifetime, policy.MethodAssurances);
        Assert.ThrowsAny<InvalidOperationException>(() => env.Source.RequireQualified(changed, env.Clock.GetUtcNow()));
        Assert.Equal("fixture-bound", env.Source.RequireQualified(policy, env.Clock.GetUtcNow()).ConfigurationVersionReference);
        await using var owner = env.Open(); await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => owner.Application.HistoryPreviewAsync(Scope() with { AssurancePolicyVersion = "other-version" }, publication.PublicationReference, "OP-other-history", "fixture-correlation"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingAndPublicationIntervalsControlAvailabilityWithoutHistoricalFallback(bool future)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var policy = PublishedStepUpPresentationTests.Policy(env.Clock);
        var start = env.Clock.GetUtcNow().AddSeconds(future ? 1 : 0); var interval = new EffectivePolicyInterval(start, true, start.AddSeconds(2), false);
        _ = await PublishOwner(env, Binding(env, "fixture-bounded") with { EffectiveInterval = interval }, interval);
        if (future) { Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireQualified(policy, env.Clock.GetUtcNow())); env.Clock.Advance(TimeSpan.FromSeconds(1)); }
        Assert.Equal("fixture-bounded", env.Source.RequireQualified(policy, env.Clock.GetUtcNow()).ConfigurationVersionReference);
        env.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireQualified(policy, env.Clock.GetUtcNow()));
    }

    [Theory]
    [InlineData("assurance-scope")]
    [InlineData("policy")]
    [InlineData("configuration")]
    [InlineData("qualification")]
    [InlineData("content-scope")]
    [InlineData("variant")]
    [InlineData("interval")]
    public async Task AllSevenOwnerInputsAreRequiredBeforeEffects(string field)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var binding = Binding(env, "fixture-invalid");
        binding = field switch { "assurance-scope" => binding with { AssuranceScope = null! }, "policy" => binding with { AssurancePolicyVersion = "" }, "configuration" => binding with { ConfigurationVersionReference = "" }, "qualification" => binding with { QualificationReference = "" }, "content-scope" => binding with { ContentScope = "" }, "variant" => binding with { VariantReference = "" }, _ => binding with { EffectiveInterval = null! } };
        await using var owner = env.Open(); await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => owner.Application.CreateStepUpContentDraftAsync(binding, "OP-invalid", "fixture-correlation"));
        await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync());
    }

    [Fact]
    public async Task StrictHttpRejectsOldFamilyDimensionCrossScopeAndExtraLegalField()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [Scope()]); var author = await admin.Browser("fixture-security-author");
        using (var legacy = await admin.Post("/proposals/search", new { scope = new { family = "Assurance", assuranceScope = StepUpBrowserTests.Scope, assurancePolicyVersion = "unexpected" }, pageSize = 1, offset = 0 }, author)) Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);
        using (var missing = await admin.Post("/proposals/search", new { scope = new { family = "StepUpContent", assurancePolicyVersion = "fixture-step-v1" }, pageSize = 1, offset = 0 }, author)) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var extra = await admin.Post("/step-up-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = new { assuranceScope = StepUpBrowserTests.Scope, assurancePolicyVersion = "fixture-step-v1", configurationVersionReference = "fixture-invalid", qualificationReference = "fixture-only", contentScope = "content", variantReference = "variant", effectiveInterval = Interval(admin.Environment.Clock.GetUtcNow()), termsUri = "https://fixture.example.test/terms" } }, author)) Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        using var crossed = await admin.Post("/step-up-content/draft", new { idempotencyKey = Guid.NewGuid(), candidate = Binding(admin.Environment, "fixture-wrong") with { AssuranceScope = new(StepUpBrowserTests.Scope.Operation, "other-context") } }, author); Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode);
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
    public async Task ReconstructedNineFamilyScopeAndPayloadBytesRemainUnchanged(int ordinal)
    {
        // RECONSTRUCTED checkpoint a5e034db serialization using explicit fixture inputs, not retained historic database bytes.
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var registration = IdentityRuntimeAdditionalConfigurationTests.Registration(env, "fixture-registration-v2");
        var legacy = new LegacyDocument(ordinal == 0 ? env.Authentication("fixture-old") : null, ordinal == 1 ? env.Assurance("fixture-old") : null, ordinal == 2 ? env.Binding("fixture-old") : null,
            ordinal == 3 ? registration : null, ordinal == 4 ? IdentityRuntimeAdditionalConfigurationTests.Totp(env, "fixture-old") : null,
            ordinal == 5 ? new(registration.Values.Version, registration.Values.TermsVersion, registration.Values.PrivacyVersion, "fixture-old", "fixture-only", "fixture-content", "fixture-variant", env.Interval, new("https://fixture.example.test/terms"), new("https://fixture.example.test/privacy"), null) : null,
            ordinal == 6 ? new(new("fixture-passkey-v1", "ACTIVE", "PASSKEY", env.Interval, null, ["SYNTHETIC-ONLY"]), PasskeyValues()) : null,
            ordinal == 7 ? new("fixture-totp-v1", "fixture-old", "fixture-only", "fixture-content", "fixture-variant", env.Interval) : null,
            ordinal == 8 ? new("fixture-passkey-v1", "fixture-old", "fixture-only", "fixture-content", "fixture-variant", env.Interval) : null);
        var golden = JsonSerializer.Serialize(legacy); var current = IdentityConfigurationDocument.Read(golden); Assert.Equal(Encoding.UTF8.GetBytes(golden), Encoding.UTF8.GetBytes(current.Snapshot)); Assert.Equal(Sha(golden), current.Digest);
        var scope = current.Scope; var oldScope = JsonSerializer.Serialize(new LegacyScope(ordinal, scope.AssuranceScope, scope.AuthenticationPolicyVersion, scope.RegistrationPolicyVersion, scope.TotpPolicyVersion, scope.PasskeyPolicyVersion));
        Assert.Equal(oldScope, scope.Key); Assert.Equal(Sha(oldScope), Sha(scope.Key)); Assert.Equal(ordinal, (int)scope.Family); Assert.Null(current.StepUpContent); Assert.Null(scope.AssurancePolicyVersion);
        Assert.Throws<IdentityConfigurationUnavailableException>(() => (scope with { AssurancePolicyVersion = "unexpected" }).Require()); Assert.Equal(9, (int)IdentityConfigurationFamily.StepUpContent);
    }

    [Fact]
    public void PersistedBindingOptInDoesNotInstallPresentationAssuranceOwnerOrProofWorkflow()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration(); Assert.DoesNotContain(old, x => x.ServiceType == typeof(IStepUpContentBindingSource));
        var services = new ServiceCollection(); services.AddTunnerPersistedStepUpContentBinding(); Assert.Contains(services, x => x.ServiceType == typeof(IStepUpContentBindingSource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IStepUpPresentationSource) || x.ServiceType == typeof(IAssurancePolicySource) || x.ServiceType == typeof(IUserStepUpOperationSource) || x.ServiceType == typeof(StepUpApplication) || x.ServiceType == typeof(StepUpBrowserService));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedStepUpContentBinding());
    }

    private sealed record LegacyDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationPolicyVersion? Registration,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpPolicyVersion? Totp,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationContentBinding? RegistrationContent,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyConfigurationVersion? Passkey,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpContentBinding? TotpContent,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyContentBinding? PasskeyContent);
    private sealed record LegacyScope(int Family, AssurancePolicyScope? AssuranceScope, string? AuthenticationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TotpPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PasskeyPolicyVersion);
    private static PasskeyConfigurationValues PasskeyValues() => new("fixture-passkey-v1", "authentication.example.test", AuthenticationTestHost.Origin, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), PasskeyRevocationSessionEffect.PreserveExistingSessions,
        "required", new[] { -7 }, 32, 32768, 4, 4, "fixture-enrollment-v1", "fixture-revocation-v1", "fixture-processing", "fixture-retention", "required", "none",
        new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-passkey-assurance", [PasskeyPolicy.AssuranceKey(true, false)] = "fixture-syncable-unbacked-assurance", [PasskeyPolicy.AssuranceKey(true, true)] = "fixture-syncable-backed-assurance" });
    private static IdentityConfigurationScope Scope() => new(IdentityConfigurationFamily.StepUpContent, StepUpBrowserTests.Scope, AssurancePolicyVersion: "fixture-step-v1");
    private static object WireScope() => new { family = "StepUpContent", assuranceScope = StepUpBrowserTests.Scope, assurancePolicyVersion = "fixture-step-v1" };
    private static StepUpContentBinding Binding(RuntimeConfigurationTestEnvironment env, string version) => new(StepUpBrowserTests.Scope, "fixture-step-v1", version, "proposed-not-runtime-authority", "fixture-stepup-content", "fixture-stepup-variant", env.Interval);
    private static async Task<IdentityConfigurationHistory> PublishOwner(RuntimeConfigurationTestEnvironment env, StepUpContentBinding binding, EffectivePolicyInterval? interval = null)
    {
        await using var owner = env.Open(); env.Authorization.Actor = "fixture-security-author"; var p = await owner.Application.CreateStepUpContentDraftAsync(binding, "OP-create", "fixture-correlation");
        p = await owner.Application.ValidateAsync(env.Command(p)); env.Authorization.Actor = "fixture-security-approver"; p = await owner.Application.ApproveAsync(env.Command(p)); env.Authorization.Actor = "fixture-security-publisher";
        return await owner.Application.PublishAsync(env.Publication(p) with { EffectiveInterval = interval ?? new(env.Clock.GetUtcNow(), true, null, false) });
    }
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement p)
    {
        using var response = await h.Post("/proposals/" + operation, new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement p, Guid? supersedes = null)
    {
        using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
}
