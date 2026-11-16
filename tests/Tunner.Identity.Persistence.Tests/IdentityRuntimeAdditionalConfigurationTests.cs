using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeAdditionalConfigurationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task TotpCreateAndUpdateDetachCallerMapBeforeAwaitedAuthorizationAndPublishOnlyAuthorizedSnapshot()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        await using var context = env.Context(); var authorization = new PausingAuthorization(env.Authorization);
        var app = new IdentityRuntimeConfigurationApplication(context, env, authorization, env.Clock);
        IdentityConfigurationProposal? proposal = null;
        for (var revision = 0; revision < 2; revision++)
        {
            var original = "fixture-original-" + revision;
            var callerMap = new Dictionary<string, string> { ["pwd"] = original };
            var candidate = Totp(env, "fixture-snapshot-" + revision); candidate = candidate with { Values = candidate.Values with { MfaAssurances = callerMap } };
            var expected = new IdentityConfigurationDocument(Totp: candidate with { Values = candidate.Values with { MfaAssurances = new Dictionary<string, string>(callerMap) } });
            var expectedPayload = expected.Snapshot; var expectedDigest = expected.Digest; var pause = authorization.PauseNext();
            var write = proposal is null ? app.CreateTotpDraftAsync(candidate, "OP-snapshot-create", "fixture-correlation")
                : app.UpdateTotpDraftAsync(proposal.ProposalReference, proposal.Revision, candidate, "OP-snapshot-update", "fixture-correlation");
            var request = await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            callerMap["pwd"] = "fixture-caller-mutated"; callerMap["federation"] = "fixture-added-after-authorization";
            pause.Continue.TrySetResult(true); proposal = await write;
            Assert.Equal(expectedDigest, request.CandidateDigest); Assert.Equal(expectedDigest, proposal.CandidateDigest);
            await using var read = env.Context(); var stored = await read.Set<IdentityConfigurationProposalRow>().SingleAsync();
            Assert.Equal(expectedPayload, stored.Payload); Assert.Equal(request.CandidateDigest, stored.Digest);
            var persisted = IdentityConfigurationDocument.Read(stored.Payload).Totp!.Values.MfaAssurances;
            Assert.Single(persisted); Assert.Equal(original, persisted["pwd"]);
        }
        proposal = await app.ValidateAsync(env.Command(proposal!)); env.Authorization.Actor = "fixture-security-approver";
        proposal = await app.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
        _ = await app.PublishAsync(env.Publication(proposal));
        var runtime = ((ITotpPolicySource)env.Source).RequireEffective(env.Clock.GetUtcNow()).MfaAssurances;
        Assert.Single(runtime); Assert.Equal("fixture-original-1", runtime["pwd"]);
    }

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
        {
            var pause = next; next = null;
            if (pause is not null) { pause.Entered.TrySetResult(request); await pause.Continue.Task.WaitAsync(cancellationToken); }
            return await inner.AuthorizeAsync(request, cancellationToken);
        }
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Registration)]
    [InlineData(IdentityConfigurationFamily.Totp)]
    public async Task AdditionalPoliciesRequireActualApprovalAndExactEffectivePublication(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        AssertUnavailable(env, family); await using var scope = env.Open();
        var proposal = await Create(scope.Application, env, family, "fixture-additional-v1", "OP-additional-create");
        proposal = await scope.Application.ValidateAsync(env.Command(proposal)); AssertUnavailable(env, family);
        await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.ApproveAsync(env.Command(proposal)));
        env.Authorization.Actor = "fixture-security-approver"; proposal = await scope.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = "fixture-security-publisher";
        var from = env.Clock.GetUtcNow().AddSeconds(1); var until = from.AddSeconds(2);
        var publication = await scope.Application.PublishAsync(new(proposal.ProposalReference, proposal.Revision, new(from, true, until, false), null, "OP-additional-publish", "fixture-additional-correlation"));
        AssertUnavailable(env, family); env.Clock.Advance(TimeSpan.FromSeconds(1)); Assert.Equal("fixture-additional-v1", EffectiveVersion(env, family));
        var historical = await scope.Application.HistoryPreviewAsync(new(family), publication.PublicationReference, "OP-additional-history", "fixture-additional-correlation");
        Assert.Equal(publication.ApprovalEvidenceReference, historical.Publication.ApprovalEvidenceReference);
        Assert.Equal(family == IdentityConfigurationFamily.Registration, historical.Registration is not null);
        Assert.Equal(family == IdentityConfigurationFamily.Totp, historical.Totp is not null);
        await using var platform = env.Platform(); var events = await platform.OutboxMessages.ToArrayAsync(); var audits = await platform.AuditEvents.ToArrayAsync();
        Assert.All(audits, x => Assert.True(x.VerifyIntegrity()));
        Assert.All(events, x => Assert.DoesNotContain("SecretBindingVersion", x.Payload, StringComparison.Ordinal));
        Assert.All(events, x => Assert.DoesNotContain("VerificationTemplateVersion", x.Payload, StringComparison.Ordinal));
        Assert.All(audits, x => Assert.DoesNotContain("MfaAssurances", x.Envelope, StringComparison.Ordinal));
        env.Clock.Advance(TimeSpan.FromSeconds(2)); AssertUnavailable(env, family);
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Registration)]
    [InlineData(IdentityConfigurationFamily.Totp)]
    public async Task AdditionalTypedHttpsCommandsPreserveReplayUpdateHistoryAndProspectiveRollback(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db, additionalScopes: [new(family)]);
        var author = await h.Browser("fixture-security-author"); var approver = await h.Browser("fixture-security-approver"); var publisher = await h.Browser("fixture-security-publisher");
        var key = Guid.NewGuid(); var original = h.DraftBody(family, "fixture-proposal", key); JsonElement proposal;
        using (var response = await h.Post(Host.DraftPath(family), original, author))
        { Assert.Equal(HttpStatusCode.OK, response.StatusCode); proposal = (await Body(response)).GetProperty("result").Clone(); }
        using (var replay = await h.Post(Host.DraftPath(family), original, author))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        h.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await h.Post(Host.DraftPath(family), original, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        h.Owners.Denied = null;
        object changed = family == IdentityConfigurationFamily.Registration ? Registration(h.Environment, "fixture-approved") : Totp(h.Environment, "fixture-approved");
        using (var update = await h.Post(Host.DraftPath(family), new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(),
            expectedRevision = proposal.GetProperty("revision").GetInt64(), candidate = changed }, author))
        { Assert.Equal(HttpStatusCode.OK, update.StatusCode); proposal = (await Body(update)).GetProperty("result").Clone(); }
        proposal = await Step(h, author, "validate", proposal);
        using (var denied = await h.Post("/proposals/approve", Write(proposal), author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        proposal = await Step(h, approver, "approve", proposal); var first = await Publish(h, publisher, proposal);
        Assert.Equal("fixture-approved", EffectiveVersion(h.Environment, family));
        using (var baseline = await h.Post("/history/preview", new { scope = h.Scope(family), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        {
            Assert.Equal(HttpStatusCode.OK, baseline.StatusCode); var typed = (await Body(baseline)).GetProperty(family == IdentityConfigurationFamily.Registration ? "registration" : "totp");
            Assert.Equal("fixture-approved", typed.GetProperty("values").GetProperty("version").GetString());
        }
        h.Environment.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var next = await h.Post(Host.DraftPath(family), h.DraftBody(family, "fixture-replacement", Guid.NewGuid()), author))
        { Assert.Equal(HttpStatusCode.OK, next.StatusCode); proposal = (await Body(next)).GetProperty("result").Clone(); }
        proposal = await Step(h, author, "validate", proposal); proposal = await Step(h, approver, "approve", proposal);
        var replacement = await Publish(h, publisher, proposal, first.GetProperty("publicationReference").GetGuid());
        Assert.Equal("fixture-replacement", EffectiveVersion(h.Environment, family)); h.Environment.Clock.Advance(TimeSpan.FromSeconds(1));
        using var rollback = await h.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = replacement.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()) }, publisher);
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode); Assert.Equal("fixture-approved", EffectiveVersion(h.Environment, family));
        using var history = await h.Post("/history", new { scope = h.Scope(family), pageSize = 2, offset = 0 }, publisher);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal(2, (await Body(history)).GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Registration)]
    [InlineData(IdentityConfigurationFamily.Totp)]
    public async Task ExistingTypedValidatorsRejectUnsupportedCandidatesWithoutOwnerEffects(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var scope = env.Open();
        if (family == IdentityConfigurationFamily.Registration)
        {
            var invalid = Registration(env, "fixture-invalid"); invalid = invalid with { Values = invalid.Values with { PasswordMinimumLength = 1 } };
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Application.CreateRegistrationDraftAsync(invalid, "OP-invalid", "fixture-correlation"));
        }
        else
        {
            var invalid = Totp(env, "fixture-invalid"); invalid = invalid with { Values = invalid.Values with { Digits = 8 } };
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Application.CreateTotpDraftAsync(invalid, "OP-invalid", "fixture-correlation"));
        }
        await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Registration)]
    [InlineData(IdentityConfigurationFamily.Totp)]
    public async Task MetadataCannotMoveAdditionalPolicyIntoAnotherRuntimeScope(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var scope = env.Open();
        var metadata = new PolicyVersionCandidate<string>("fixture-scope", "ACTIVE", "AUTHENTICATION", env.Interval, null, ["client-proposed-evidence-is-not-authority"]);
        if (family == IdentityConfigurationFamily.Registration)
        {
            var wrong = Registration(env, "fixture-scope") with { Metadata = metadata };
            await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => scope.Application.CreateRegistrationDraftAsync(wrong, "OP-wrong-scope", "fixture-correlation"));
        }
        else
        {
            var wrong = Totp(env, "fixture-scope") with { Metadata = metadata };
            await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => scope.Application.CreateTotpDraftAsync(wrong, "OP-wrong-scope", "fixture-correlation"));
        }
        AssertUnavailable(env, family);
    }

    [Fact]
    public void ExistingThreeFamilyRegistrationDoesNotActivateRegistrationOrTotpPorts()
    {
        var services = new ServiceCollection(); services.AddTunnerPersistedIdentityRuntimeConfiguration();
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IRegistrationPolicySource) || x.ServiceType == typeof(ITotpPolicySource));
        services.AddTunnerPersistedRegistrationConfiguration(); Assert.Contains(services, x => x.ServiceType == typeof(IRegistrationPolicySource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(ITotpPolicySource));
        services.AddTunnerPersistedTotpConfiguration(); Assert.Contains(services, x => x.ServiceType == typeof(ITotpPolicySource));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedRegistrationConfiguration());
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedTotpConfiguration());
    }

    // RECONSTRUCTED format from 477cfe405ab088b1ba3de4e8a10f1eed166debf7, not a captured old execution/database.
    // That source serialized exactly three document properties and the six receipt properties below, with numeric scope families 0/1/2.
    [Theory]
    [InlineData(IdentityConfigurationFamily.Authentication)]
    [InlineData(IdentityConfigurationFamily.Assurance)]
    [InlineData(IdentityConfigurationFamily.AuthenticationContent)]
    public async Task Reconstructed477PayloadAndReceiptRetainGoldenBytesScopeAndDigestThroughCurrentPublicationAndReplay(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var authentication = family == IdentityConfigurationFamily.Authentication ? env.Authentication("fixture-legacy") : null;
        var assurance = family == IdentityConfigurationFamily.Assurance ? env.Assurance("fixture-legacy") : null;
        var binding = family == IdentityConfigurationFamily.AuthenticationContent ? env.Binding("fixture-legacy") : null;
        var golden = JsonSerializer.Serialize(new { Authentication = authentication, Assurance = assurance, ContentBinding = binding });
        var goldenScope = family switch
        {
            IdentityConfigurationFamily.Authentication => "{\"Family\":0,\"AssuranceScope\":null,\"AuthenticationPolicyVersion\":null}",
            IdentityConfigurationFamily.Assurance => "{\"Family\":1,\"AssuranceScope\":{\"Operation\":\"fixture-security-operation\",\"Context\":\"fixture-user-context\"},\"AuthenticationPolicyVersion\":null}",
            _ => "{\"Family\":2,\"AssuranceScope\":null,\"AuthenticationPolicyVersion\":\"fixture-authentication-v1\"}"
        };
        var seed = new Reconstructed477Seed(golden, goldenScope); env.Interceptors = [seed];
        var operation = "OP-reconstructed477-" + family; var intent = Sha("fixture-legacy-request"); IdentityConfigurationProposal original;
        await using (var scope = env.Open())
        {
            var result = await scope.Application.ExecuteCommandAsync(operation, intent, () => family switch
            {
                IdentityConfigurationFamily.Authentication => scope.Application.CreateAuthenticationDraftAsync(authentication!, operation, "fixture-legacy-correlation"),
                IdentityConfigurationFamily.Assurance => scope.Application.CreateAssuranceDraftAsync(assurance!, operation, "fixture-legacy-correlation"),
                _ => scope.Application.CreateContentBindingDraftAsync(binding!, operation, "fixture-legacy-correlation")
            }); original = result.Result;
        }
        Assert.Equal(1, seed.Seeded); env.Interceptors = [];
        await using (var context = env.Context())
        {
            var row = await context.Set<IdentityConfigurationProposalRow>().SingleAsync();
            Assert.Equal(Encoding.UTF8.GetBytes(golden), Encoding.UTF8.GetBytes(row.Payload)); Assert.Equal(Sha(golden), row.Digest);
            Assert.Equal(Encoding.UTF8.GetBytes(goldenScope), Encoding.UTF8.GetBytes(row.ScopeKey)); Assert.Equal(Sha(goldenScope), Sha(row.ScopeKey));
        }
        await using (var scope = env.Open())
        {
            var preview = await scope.Application.PreviewAsync(env.Command(original)); var transport = JsonSerializer.Serialize(preview);
            Assert.DoesNotContain("\"Registration\"", transport, StringComparison.Ordinal); Assert.DoesNotContain("\"Totp\"", transport, StringComparison.Ordinal);
            var proposal = await scope.Application.ValidateAsync(env.Command(original)); env.Authorization.Actor = "fixture-security-approver";
            proposal = await scope.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
            _ = await scope.Application.PublishAsync(env.Publication(proposal));
            if (family == IdentityConfigurationFamily.Authentication) Assert.Equal("fixture-legacy", env.Source.RequireEffective(env.Clock.GetUtcNow()).Version);
            else if (family == IdentityConfigurationFamily.Assurance) Assert.Equal("fixture-legacy", env.Source.RequireEffective(env.AssuranceScope, env.Clock.GetUtcNow()).Metadata.VersionId);
            else Assert.Equal("fixture-legacy", env.Source.RequireQualified(env.Authentication().Values, env.Clock.GetUtcNow()).ConfigurationVersionReference);
            env.Authorization.Actor = "fixture-security-author"; var executed = false;
            var replay = await scope.Application.ExecuteCommandAsync(operation, intent, () => { executed = true; return Task.FromResult(original); });
            Assert.False(executed); Assert.True(replay.Replayed); Assert.Equal(original, replay.Result);
        }
    }

    private sealed record Legacy477Receipt(string RequestDigest, IdentityConfigurationAuthorizationRequest Authorization,
        string ActorReference, IdentityConfigurationActorKind ActorKind, IdentityConfigurationProposal? Proposal, Guid? PublicationReference);
    private sealed class Reconstructed477Seed(string goldenPayload, string goldenScope) : SaveChangesInterceptor
    {
        internal int Seeded;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var database = eventData.Context!;
            foreach (var entry in database.ChangeTracker.Entries<IdentityConfigurationProposalRow>().Where(x => x.State == EntityState.Added))
            {
                Assert.Equal(goldenPayload, entry.Entity.Payload); Assert.Equal(goldenScope, entry.Entity.ScopeKey);
                entry.Entity.Payload = goldenPayload; entry.Entity.Digest = Sha(goldenPayload); Seeded++;
            }
            foreach (var entry in database.ChangeTracker.Entries<OutboxMessage>().Where(x => x.State == EntityState.Added))
            {
                var payload = JsonNode.Parse(entry.Entity.Payload)!.AsObject();
                if (payload["commandReceipt"] is not { } receiptNode) continue;
                var receipt = receiptNode.Deserialize<IdentityConfigurationCommandReceipt>()!;
                var reconstructed = new Legacy477Receipt(receipt.RequestDigest, receipt.Authorization, receipt.ActorReference, receipt.ActorKind, receipt.Proposal, receipt.PublicationReference);
                var goldenReceipt = JsonSerializer.Serialize(reconstructed); Assert.Equal(goldenReceipt, receiptNode.ToJsonString());
                payload["commandReceipt"] = JsonNode.Parse(goldenReceipt);
                entry.Property(x => x.Payload).CurrentValue = payload.ToJsonString();
            }
            return ValueTask.FromResult(result);
        }
    }
    internal static RegistrationPolicyVersion Registration(RuntimeConfigurationTestEnvironment env, string version)
    {
        // Same explicit synthetic policy shape as RegistrationTests/RegistrationBffTests; no production selection.
        var values = new RegistrationPolicy(version, "PDR-0011", "fixture-processing", "fixture-retention", "fixture-terms", "fixture-privacy", "fixture-verification-template", "fixture-existing-template",
            "PASSWORD_AND_VERIFIED_EMAIL", 15, 128, 10000, 6, TimeSpan.FromMinutes(5), 2, TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(1), 1000, 1000, 1000, 1000, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
        return new(new(version, "ACTIVE", "REGISTRATION", env.Interval, null, ["client-proposed-evidence-is-not-authority"]), values);
    }
    internal static TotpPolicyVersion Totp(RuntimeConfigurationTestEnvironment env, string version) => new(
        new(version, "ACTIVE", "TOTP", env.Interval, null, ["client-proposed-evidence-is-not-authority"]), new TotpTests.Profile().Policy with { Version = version });
    private static Task<IdentityConfigurationProposal> Create(IdentityRuntimeConfigurationApplication app, RuntimeConfigurationTestEnvironment env, IdentityConfigurationFamily family, string version, string operation) =>
        family == IdentityConfigurationFamily.Registration ? app.CreateRegistrationDraftAsync(Registration(env, version), operation, "fixture-correlation") : app.CreateTotpDraftAsync(Totp(env, version), operation, "fixture-correlation");
    private static string EffectiveVersion(RuntimeConfigurationTestEnvironment env, IdentityConfigurationFamily family) => family == IdentityConfigurationFamily.Registration
        ? ((IRegistrationPolicySource)env.Source).RequireEffective(env.Clock.GetUtcNow()).Version : ((ITotpPolicySource)env.Source).RequireEffective(env.Clock.GetUtcNow()).Version;
    private static void AssertUnavailable(RuntimeConfigurationTestEnvironment env, IdentityConfigurationFamily family) => Assert.Throws<IdentityConfigurationUnavailableException>(() => EffectiveVersion(env, family));
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
