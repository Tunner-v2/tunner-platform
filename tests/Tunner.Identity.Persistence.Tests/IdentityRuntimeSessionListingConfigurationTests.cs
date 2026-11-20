using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeSessionListingConfigurationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ExactBudgetRoundTripsThroughApprovalAndImmutableHistory()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var owner = env.Open(); var candidate = Budget(env, "fixture-listing-v1", 2);
        var p = await owner.Application.CreateSessionListingDraftAsync(candidate, "OP-create", "fixture-correlation"); Assert.Throws<IdentityConfigurationUnavailableException>(() => Runtime(env));
        var preview = await owner.Application.PreviewAsync(env.Command(p)); Assert.NotNull(preview.SessionListing); Assert.Equal(JsonSerializer.Serialize(candidate), JsonSerializer.Serialize(preview.SessionListing));
        p = await owner.Application.ValidateAsync(env.Command(p)); await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => owner.Application.ApproveAsync(env.Command(p)));
        env.Authorization.Actor = "fixture-security-approver"; p = await owner.Application.ApproveAsync(env.Command(p)); env.Authorization.Actor = "fixture-security-publisher";
        var publication = await owner.Application.PublishAsync(env.Publication(p)); Assert.Equal(2, Runtime(env).MaximumRows);
        var history = await owner.Application.HistoryPreviewAsync(new(IdentityConfigurationFamily.SessionListing), publication.PublicationReference, "OP-history", "fixture-correlation"); Assert.NotNull(history.SessionListing); Assert.Equal(JsonSerializer.Serialize(candidate), JsonSerializer.Serialize(history.SessionListing));
        var error = await Assert.ThrowsAsync<PostgresException>(() => owner.Database.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.runtime_configuration_versions SET policy_version_reference={"forged"} WHERE id={publication.VersionReference}")); Assert.Equal("55000", error.SqlState);
        Assert.Equal("fixture-listing-v1", Runtime(env).Metadata.VersionId);
    }

    [Theory]
    [InlineData(0, 4096)]
    [InlineData(int.MaxValue, 4096)]
    [InlineData(1, 0)]
    public async Task ExistingConstructorBoundsRejectCandidatesBeforeOwnerEffects(int rows, int bytes)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        Assert.ThrowsAny<ArgumentException>(() => new SessionListingHttpBudget(Budget(env, "fixture-invalid", 1).Metadata, rows, bytes));
        await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("status")]
    public async Task OnlyExactActiveBudgetMetadataCanEnterLifecycle(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var owner = env.Open();
        var c = new SessionListingHttpBudget(new("fixture-invalid", failure == "status" ? "DRAFT" : "ACTIVE", failure == "scope" ? "other" : SessionListingHttpBudget.Scope, env.Interval, null, ["SYNTHETIC-ONLY"]), 1, 4096);
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => owner.Application.CreateSessionListingDraftAsync(c, "OP-invalid", "fixture-correlation"));
    }

    [Fact]
    public async Task ActualAdminPublicationControlsGuardedListingAndCursorBudgetChangesWithoutRevokingSessions()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [new(IdentityConfigurationFamily.SessionListing)]); var env = admin.Environment;
        var author = await admin.Browser("fixture-security-author"); var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        var command = new { idempotencyKey = Guid.NewGuid(), candidate = Budget(env, "fixture-listing-v1", 1) }; JsonElement p;
        using (var create = await admin.Post("/session-listing/draft", command, author)) { Assert.Equal(HttpStatusCode.OK, create.StatusCode); p = (await Body(create)).GetProperty("result").Clone(); }
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post("/session-listing/draft", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); admin.Owners.Denied = null;
        using (var replay = await admin.Post("/session-listing/draft", command, author)) { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        using (var conflict = await admin.Post("/session-listing/draft", new { command.idempotencyKey, candidate = Budget(env, "fixture-conflict", 2) }, author)) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        p = await Step(admin, author, "validate", p); p = await Step(admin, approver, "approve", p); var first = await Publish(admin, publisher, p);
        await using var user = await AuthenticationTestHost.Start(db, services => { services.AddSingleton<TimeProvider>(env.Clock); services.AddTunnerPersistedSessionListingConfiguration(); });
        Assert.Equal(user.Clock.GetUtcNow(), env.Clock.GetUtcNow()); await user.SeedAsync(db); await SessionListingTests.SignIn(user); await SessionListingTests.SignIn(user);
        var boot = await user.Options(); using var signed = await user.Post("/authentication/sign-in", AuthenticationTestHost.Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        var cookies = AuthenticationTestHost.Merge(boot.Cookies, signed); using var session = await user.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        cookies = AuthenticationTestHost.Merge(cookies, session); var csrf = (await Body(session)).GetProperty("csrfToken").GetString()!; string continuation;
        using (var anonymous = await user.Post("/account/sessions/list", new { }, "", "")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var listed = await user.Post("/account/sessions/list", new { }, cookies, csrf)) { Assert.Equal(HttpStatusCode.OK, listed.StatusCode); var page = await Body(listed); Assert.Single(page.GetProperty("sessions").EnumerateArray()); continuation = page.GetProperty("continuation").GetString()!; Assert.False(string.IsNullOrWhiteSpace(continuation)); }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var create = await admin.Post("/session-listing/draft", new { idempotencyKey = Guid.NewGuid(), candidate = Budget(env, "fixture-listing-v2", 2) }, author)) { Assert.Equal(HttpStatusCode.OK, create.StatusCode); p = (await Body(create)).GetProperty("result").Clone(); }
        using (var update = await admin.Post("/session-listing/draft", new { idempotencyKey = Guid.NewGuid(), candidate = Budget(env, "fixture-listing-v3", 2), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, author)) { Assert.Equal(HttpStatusCode.OK, update.StatusCode); p = (await Body(update)).GetProperty("result").Clone(); }
        p = await Step(admin, author, "validate", p); p = await Step(admin, approver, "approve", p); var latest = await Publish(admin, publisher, p, first.GetProperty("publicationReference").GetGuid());
        using (var stale = await user.Post("/account/sessions/list", new { continuation }, cookies, csrf)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var current = await user.Post("/account/sessions/list", new { }, cookies, csrf)) { Assert.Equal(HttpStatusCode.OK, current.StatusCode); Assert.Equal(2, (await Body(current)).GetProperty("sessions").GetArrayLength()); }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(), supersedesPublicationReference = latest.GetProperty("publicationReference").GetGuid(), effectiveInterval = new { from = env.Clock.GetUtcNow(), includesFrom = true, until = env.Clock.GetUtcNow().AddSeconds(1), includesUntil = false } }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal(1, Runtime(env).MaximumRows); env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var expired = await user.Post("/account/sessions/list", new { }, cookies, csrf)) Assert.Equal(HttpStatusCode.ServiceUnavailable, expired.StatusCode);
        await using var unchanged = db.Context(); Assert.All(await unchanged.Set<AuthenticationSessionRow>().ToArrayAsync(), x => Assert.Equal("ACTIVE", x.State));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task InvalidHttpBudgetCannotCreateProposalOrCommandReceipt(int rows)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [new(IdentityConfigurationFamily.SessionListing)]); var actor = await admin.Browser("fixture-security-author");
        await using var before = admin.Environment.Platform(); var events = JsonSerializer.Serialize(await before.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()); var audit = JsonSerializer.Serialize(await before.AuditEvents.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync());
        using var bad = await admin.Post("/session-listing/draft", new { idempotencyKey = Guid.NewGuid(), candidate = new { metadata = Budget(admin.Environment, "fixture-invalid", 1).Metadata, maximumRows = rows, maximumRequestBytes = 4096 } }, actor); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        await using var after = admin.Environment.Platform(); Assert.Equal(events, JsonSerializer.Serialize(await after.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync())); Assert.Equal(audit, JsonSerializer.Serialize(await after.AuditEvents.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()));
        await using var config = db.Context(); Assert.Empty(await config.Set<IdentityConfigurationProposalRow>().ToArrayAsync()); Assert.Empty(await config.Set<IdentityConfigurationVersionRow>().ToArrayAsync()); Assert.Empty(await config.Set<IdentityConfigurationPublicationRow>().ToArrayAsync());
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
    public async Task ElevenLegacyScopesAndReconstructedOriginalPayloadPreserveBytes(int ordinal)
    {
        // RECONSTRUCTION from f793e93e, not captured historic runtime data. Existing family-specific payload cohorts remain retained.
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var scope = new IdentityConfigurationScope((IdentityConfigurationFamily)ordinal, AssuranceScope: ordinal == 1 ? env.AssuranceScope : ordinal == 9 ? StepUpBrowserTests.Scope : null,
            AuthenticationPolicyVersion: ordinal == 2 ? "fixture-auth" : null, RegistrationPolicyVersion: ordinal == 5 ? "fixture-registration" : null,
            TotpPolicyVersion: ordinal == 7 ? "fixture-totp" : null, PasskeyPolicyVersion: ordinal == 8 ? "fixture-passkey" : null, AssurancePolicyVersion: ordinal == 9 ? "fixture-step" : null);
        scope.Require(); var old = JsonSerializer.Serialize(new LegacyScope(ordinal, scope.AssuranceScope, scope.AuthenticationPolicyVersion, scope.RegistrationPolicyVersion, scope.TotpPolicyVersion, scope.PasskeyPolicyVersion, scope.AssurancePolicyVersion)); Assert.Equal(old, scope.Key); Assert.Equal(Sha(old), Sha(scope.Key));
        var payload = JsonSerializer.Serialize(new { Authentication = env.Authentication("fixture-old"), Assurance = (AssurancePolicyVersion?)null, ContentBinding = (AuthenticationContentBinding?)null });
        var document = IdentityConfigurationDocument.Read(payload); Assert.Equal(payload, document.Snapshot); Assert.Equal(Sha(payload), document.Digest); Assert.Null(document.SessionListing); Assert.Equal(11, (int)IdentityConfigurationFamily.SessionListing);
    }

    [Fact]
    public void SourceSelectionIsExplicitAndBaseHelperCannotActivateSessionListing()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration(); Assert.DoesNotContain(old, x => x.ServiceType == typeof(ISessionListingHttpBudgetSource));
        var services = new ServiceCollection(); services.AddTunnerPersistedSessionListingConfiguration(); Assert.Contains(services, x => x.ServiceType == typeof(ISessionListingHttpBudgetSource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(ISessionListingPresentationSource) || x.ServiceType == typeof(ISessionRevocationPresentationSource) || x.ServiceType == typeof(ISessionBulkRevocationDiscoverySource) || x.ServiceType == typeof(ISessionBulkRevocationHttpBudgetSource));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedSessionListingConfiguration());
    }

    private sealed record LegacyScope(int Family, AssurancePolicyScope? AssuranceScope, string? AuthenticationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TotpPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PasskeyPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssurancePolicyVersion);
    private static SessionListingHttpBudget Budget(RuntimeConfigurationTestEnvironment env, string version, int rows) => new(new(version, "ACTIVE", SessionListingHttpBudget.Scope, env.Interval, null, ["proposed-not-runtime-authority"]), rows, 4096);
    private static SessionListingHttpBudget Runtime(RuntimeConfigurationTestEnvironment env) => ((ISessionListingHttpBudgetSource)env.Source).RequireEffective(env.Clock.GetUtcNow());
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement p)
    { using var response = await h.Post("/proposals/" + operation, new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, actor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone(); }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement p, Guid? supersedes = null)
    { using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64(), effectiveInterval = new { from = h.Environment.Clock.GetUtcNow(), includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false }, supersedesPublicationReference = supersedes }, actor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone(); }
}
