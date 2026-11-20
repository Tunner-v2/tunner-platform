using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimePasskeyConfigurationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AllTwentyInputsRoundTripThroughActualApprovalAndExistingRuntimePolicy()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        // Serialization/order test only; no native algorithm/provider capability is selected or exercised.
        var candidate = Candidate(env, "fixture-passkey-v1"); candidate = candidate with { Values = candidate.Values with { AllowedAlgorithms = new[] { -257, -7, -257 } } };
        Assert.Equal(20, JsonSerializer.SerializeToElement(candidate.Values).EnumerateObject().Count());
        await using var owner = env.Open(); var proposal = await owner.Application.CreatePasskeyDraftAsync(candidate, "OP-passkey-create", "fixture-correlation");
        Assert.Throws<IdentityConfigurationUnavailableException>(() => Runtime(env));
        var preview = await owner.Application.PreviewAsync(env.Command(proposal)); Assert.NotNull(preview.Passkey);
        Assert.Equal(JsonSerializer.Serialize(candidate.Values), JsonSerializer.Serialize(preview.Passkey.Values));
        Assert.Equal("required", preview.Passkey.Values.ResidentKeyRequirement); Assert.Equal("required", preview.Passkey.Values.UserVerificationRequirement);
        Assert.Equal("none", preview.Passkey.Values.AttestationConveyancePreference);
        proposal = await owner.Application.ValidateAsync(env.Command(proposal)); Assert.Throws<IdentityConfigurationUnavailableException>(() => Runtime(env));
        await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => owner.Application.ApproveAsync(env.Command(proposal)));
        env.Authorization.Actor = "fixture-security-approver"; proposal = await owner.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-publisher"; var publication = await owner.Application.PublishAsync(env.Publication(proposal));
        var current = Runtime(env); Assert.Equal(candidate.Values.Version, current.Version); Assert.Equal(candidate.Values.AllowedAlgorithms, current.AllowedAlgorithms);
        Assert.Equal(JsonSerializer.Serialize(candidate.Values.ToPolicy()), JsonSerializer.Serialize(current));
        var historical = await owner.Application.HistoryPreviewAsync(new(IdentityConfigurationFamily.Passkey), publication.PublicationReference, "OP-passkey-history", "fixture-correlation");
        Assert.NotNull(historical.Passkey); Assert.Equal(JsonSerializer.Serialize(candidate.Values), JsonSerializer.Serialize(historical.Passkey.Values));
        await using var platform = env.Platform(); Assert.All(await platform.AuditEvents.ToArrayAsync(), x => Assert.True(x.VerifyIntegrity()));
        Assert.All(await platform.OutboxMessages.ToArrayAsync(), x => Assert.DoesNotContain("AssuranceMappings", x.Payload, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("resident")]
    [InlineData("uv")]
    [InlineData("attestation")]
    public async Task FixedConstructorInvariantsAreExplicitAndUnsupportedValuesFailBeforeEffects(string field)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var candidate = Candidate(env, "fixture-invalid-fixed");
        candidate = candidate with { Values = field switch { "resident" => candidate.Values with { ResidentKeyRequirement = "preferred" }, "uv" => candidate.Values with { UserVerificationRequirement = "preferred" }, _ => candidate.Values with { AttestationConveyancePreference = "direct" } } };
        await using var owner = env.Open(); await Assert.ThrowsAsync<ArgumentException>(() => owner.Application.CreatePasskeyDraftAsync(candidate, "OP-invalid-fixed", "fixture-correlation"));
        await AssertNoEffects(env);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("timeout")]
    [InlineData("algorithms")]
    [InlineData("assurance")]
    public async Task ExistingDomainConstructorRemainsAuthoritativeForOwnerBoundsAndMappings(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var candidate = Candidate(env, "fixture-invalid-domain");
        candidate = candidate with { Values = failure switch
        {
            "origin" => candidate.Values with { Origin = "https://foreign.example.test" },
            "timeout" => candidate.Values with { AuthenticatorTimeout = candidate.Values.CeremonyLifetime + TimeSpan.FromTicks(1) },
            "algorithms" => candidate.Values with { AllowedAlgorithms = Array.Empty<int>() },
            _ => candidate.Values with { AssuranceMappings = new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "AAL3" } }
        } };
        await using var owner = env.Open(); await Assert.ThrowsAsync<ArgumentException>(() => owner.Application.CreatePasskeyDraftAsync(candidate, "OP-invalid-domain", "fixture-correlation"));
        await AssertNoEffects(env);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("status")]
    [InlineData("identity")]
    public async Task MetadataMustNameExactActivePasskeyScopeAndVersion(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var candidate = Candidate(env, "fixture-policy");
        candidate = candidate with { Metadata = new(failure == "identity" ? "fixture-other-version" : candidate.Values.Version,
            failure == "status" ? "DRAFT" : "ACTIVE", failure == "scope" ? "AUTHENTICATION" : "PASSKEY", env.Interval, null, ["client-proposed-evidence-is-not-authority"]) };
        await using var owner = env.Open();
        if (failure == "identity") await Assert.ThrowsAsync<ArgumentException>(() => owner.Application.CreatePasskeyDraftAsync(candidate, "OP-invalid-metadata", "fixture-correlation"));
        else await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => owner.Application.CreatePasskeyDraftAsync(candidate, "OP-invalid-metadata", "fixture-correlation"));
        await AssertNoEffects(env);
    }

    [Fact]
    public async Task CreateAndUpdateDetachAlgorithmsAndMappingsBeforeActualAuthorizationAwait()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var context = env.Context();
        var authorization = new PausingAuthorization(env.Authorization); var app = new IdentityRuntimeConfigurationApplication(context, env, authorization, env.Clock);
        IdentityConfigurationProposal? proposal = null;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var algorithms = new List<int> { -257, -7, -257 }; var mappings = new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-original-" + iteration };
            var candidate = Candidate(env, "fixture-copy-" + iteration); candidate = candidate with { Values = candidate.Values with { AllowedAlgorithms = algorithms, AssuranceMappings = mappings } };
            var expected = new IdentityConfigurationDocument(Passkey: candidate with { Values = candidate.Values.Snapshot() }); var payload = expected.Snapshot; var digest = expected.Digest;
            var pause = authorization.PauseNext();
            var write = proposal is null ? app.CreatePasskeyDraftAsync(candidate, "OP-copy-create", "fixture-correlation")
                : app.UpdatePasskeyDraftAsync(proposal.ProposalReference, proposal.Revision, candidate, "OP-copy-update", "fixture-correlation");
            var request = await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30)); algorithms.Reverse(); algorithms.Add(42);
            mappings[PasskeyPolicy.AssuranceKey(false, false)] = "fixture-caller-mutated"; pause.Continue.TrySetResult(true); proposal = await write;
            Assert.Equal(digest, request.CandidateDigest); Assert.Equal(digest, proposal.CandidateDigest);
            await using var read = env.Context(); var row = await read.Set<IdentityConfigurationProposalRow>().SingleAsync(); Assert.Equal(payload, row.Payload); Assert.Equal(digest, row.Digest);
        }
        proposal = await app.ValidateAsync(env.Command(proposal!)); env.Authorization.Actor = "fixture-security-approver"; proposal = await app.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-publisher"; _ = await app.PublishAsync(env.Publication(proposal));
        var current = Runtime(env); Assert.Equal(new[] { -257, -7, -257 }, current.AllowedAlgorithms); Assert.Equal("fixture-original-1", current.AssuranceMappings[PasskeyPolicy.AssuranceKey(false, false)]);
    }

    [Fact]
    public async Task TypedHttpsLifecycleRetainsCurrentAuthorizationReplayAndProspectiveRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Host.Start(db, additionalScopes: [new(IdentityConfigurationFamily.Passkey)]);
        var env = host.Environment; var author = await host.Browser("fixture-security-author"); var approver = await host.Browser("fixture-security-approver"); var publisher = await host.Browser("fixture-security-publisher");
        var candidate = Candidate(env, "fixture-http-passkey-v1"); var command = new { idempotencyKey = Guid.NewGuid(), candidate };
        var invalid = JsonSerializer.SerializeToNode(command, WebJson)!; invalid["candidate"]!["values"]!["userVerificationRequirement"] = "preferred";
        using (var rejected = await host.Post("/passkey/draft", invalid, author)) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using (var context = env.Context()) Assert.Empty(await context.Set<IdentityConfigurationProposalRow>().ToArrayAsync());
        JsonElement proposal;
        using (var created = await host.Post("/passkey/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        host.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await host.Post("/passkey/draft", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        host.Owners.Denied = null;
        using (var replay = await host.Post("/passkey/draft", command, author))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        candidate = Candidate(env, "fixture-http-passkey-v2");
        using (var updated = await host.Post("/passkey/draft", new { idempotencyKey = Guid.NewGuid(), candidate, proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, updated.StatusCode); proposal = (await Body(updated)).GetProperty("result").Clone(); }
        proposal = await Step(host, author, "validate", proposal); proposal = await Step(host, approver, "approve", proposal); var first = await Publish(host, publisher, proposal);
        Assert.Equal(candidate.Values.Version, Runtime(env).Version);
        using (var historical = await host.Post("/history/preview", new { scope = new { family = "Passkey" }, publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, historical.StatusCode); var values = (await Body(historical)).GetProperty("passkey").GetProperty("values"); Assert.Equal(20, values.EnumerateObject().Count()); Assert.Equal("required", values.GetProperty("residentKeyRequirement").GetString()); Assert.Equal("none", values.GetProperty("attestationConveyancePreference").GetString()); }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); candidate = Candidate(env, "fixture-http-passkey-v3");
        using (var created = await host.Post("/passkey/draft", new { idempotencyKey = Guid.NewGuid(), candidate }, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        proposal = await Step(host, author, "validate", proposal); proposal = await Step(host, approver, "approve", proposal);
        var latest = await Publish(host, publisher, proposal, first.GetProperty("publicationReference").GetGuid()); Assert.Equal(candidate.Values.Version, Runtime(env).Version);
        env.Clock.Advance(TimeSpan.FromSeconds(1));
        using var rollback = await host.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = latest.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow()) }, publisher);
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode); Assert.Equal("fixture-http-passkey-v2", Runtime(env).Version);
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Authentication)]
    [InlineData(IdentityConfigurationFamily.Assurance)]
    [InlineData(IdentityConfigurationFamily.AuthenticationContent)]
    [InlineData(IdentityConfigurationFamily.Registration)]
    [InlineData(IdentityConfigurationFamily.Totp)]
    [InlineData(IdentityConfigurationFamily.RegistrationContent)]
    public async Task PrePasskeySixFamilyPayloadAndScopeBytesRemainUnchanged(IdentityConfigurationFamily family)
    {
        // Reconstruction of the pre-Passkey six-family shape; not captured historical runtime data.
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var registration = IdentityRuntimeAdditionalConfigurationTests.Registration(env, "fixture-registration-v2");
        var binding = new RegistrationContentBinding(registration.Values.Version, registration.Values.TermsVersion, registration.Values.PrivacyVersion, "fixture-legacy-binding", "fixture-only-qualification",
            "fixture-registration-content", "fixture-registration-variant", env.Interval, new("https://fixture.example.test/terms"), new("https://fixture.example.test/privacy"), null);
        var legacy = new LegacyDocument(family == IdentityConfigurationFamily.Authentication ? env.Authentication("fixture-legacy") : null,
            family == IdentityConfigurationFamily.Assurance ? env.Assurance("fixture-legacy") : null, family == IdentityConfigurationFamily.AuthenticationContent ? env.Binding("fixture-legacy") : null,
            family == IdentityConfigurationFamily.Registration ? registration : null, family == IdentityConfigurationFamily.Totp ? IdentityRuntimeAdditionalConfigurationTests.Totp(env, "fixture-legacy") : null,
            family == IdentityConfigurationFamily.RegistrationContent ? binding : null);
        var golden = JsonSerializer.Serialize(legacy); var current = IdentityConfigurationDocument.Read(golden); Assert.Equal(Encoding.UTF8.GetBytes(golden), Encoding.UTF8.GetBytes(current.Snapshot)); Assert.Equal(Sha(golden), current.Digest);
        var scope = current.Scope; var oldScope = new LegacyScope((int)family, scope.AssuranceScope, scope.AuthenticationPolicyVersion, scope.RegistrationPolicyVersion);
        Assert.Equal(JsonSerializer.Serialize(oldScope), scope.Key); Assert.Null(current.Passkey); Assert.Equal(6, (int)IdentityConfigurationFamily.Passkey);
    }

    [Fact]
    public void PasskeySourceRequiresIndependentOptInAndDoesNotRegisterPresentationOrProviderPorts()
    {
        var services = new ServiceCollection(); services.AddTunnerPersistedIdentityRuntimeConfiguration(); Assert.DoesNotContain(services, x => x.ServiceType == typeof(IPasskeyPolicySource));
        services.AddTunnerPersistedPasskeyConfiguration(); Assert.Contains(services, x => x.ServiceType == typeof(IPasskeyPolicySource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IPasskeyPresentationSource) || x.ServiceType == typeof(IPasskeyRevocationEligibilitySource));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedPasskeyConfiguration());
    }

    private sealed record LegacyDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationPolicyVersion? Registration,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpPolicyVersion? Totp,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationContentBinding? RegistrationContent);
    private sealed record LegacyScope(int Family, AssurancePolicyScope? AssuranceScope, string? AuthenticationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion);
    private sealed class PausingAuthorization(IIdentityConfigurationAuthorizationSource inner) : IIdentityConfigurationAuthorizationSource
    {
        internal sealed class Pause
        {
            internal TaskCompletionSource<IdentityConfigurationAuthorizationRequest> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<bool> Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private Pause? next;
        internal Pause PauseNext() { next = new(); return next; }
        public async ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken)
        { var pause = next; next = null; if (pause is not null) { pause.Entered.TrySetResult(request); await pause.Continue.Task.WaitAsync(cancellationToken); } return await inner.AuthorizeAsync(request, cancellationToken); }
    }
    private static PasskeyConfigurationVersion Candidate(RuntimeConfigurationTestEnvironment env, string version) => new(
        new(version, "ACTIVE", "PASSKEY", env.Interval, null, ["client-proposed-evidence-is-not-authority"]),
        new(version, "authentication.example.test", AuthenticationTestHost.Origin, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), PasskeyRevocationSessionEffect.PreserveExistingSessions,
            "required", new[] { -7 }, 32, 32768, 4, 4, "fixture-enrollment-v1", "fixture-revocation-v1", "fixture-processing", "fixture-retention", "required", "none",
            new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-passkey-assurance", [PasskeyPolicy.AssuranceKey(true, false)] = "fixture-syncable-unbacked-assurance", [PasskeyPolicy.AssuranceKey(true, true)] = "fixture-syncable-backed-assurance" }));
    private static PasskeyPolicy Runtime(RuntimeConfigurationTestEnvironment env) => ((IPasskeyPolicySource)env.Source).RequireEffective(env.Clock.GetUtcNow());
    private static async Task AssertNoEffects(RuntimeConfigurationTestEnvironment env)
    { await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync()); }
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static object Write(JsonElement p) => new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() };
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement proposal)
    { using var response = await h.Post("/proposals/" + operation, Write(proposal), actor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone(); }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement proposal, Guid? supersedes = null)
    {
        using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(),
            expectedRevision = proposal.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
}
