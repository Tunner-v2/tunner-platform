using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Platform.Policies;
using Tunner.Database.Outbox;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// Reuse the existing private Scenario/Create/Profile/Login/NoEffects contract; no second host or proof setup.
public sealed partial class SessionBulkRevocationHttpTests
{
    [Fact]
    public async Task PublishedContentReachesBothExistingRevocationOptionsWithReplacementHistoryAndNoNativeEffects()
    {
        var configured = await CreatePublishedActionScenario(); await using var test = configured.Scenario; var content = configured.Content;
        var published = await PublishActionBundle(content); await AssertPublishedActionOptions(test, content, published);
        using (var listing = await test.Host.Get("/account/sessions/options", test.Actor.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, listing.StatusCode); var body = await listing.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SessionListingPresentation.RequiredKeys.Count, body.GetProperty("content").EnumerateObject().Count());
            Assert.Equal("Fixture account.sessions.title", body.GetProperty("content").GetProperty("account.sessions.title").GetProperty("text").GetString());
        }
        test.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "account.sessions.revoke.confirm.title", "account.sessions.bulk.review.title" })
        {
            var old = published[key]; var replacement = await content.PublishTextAsync(key, "Synthetic revised action title: " + key, supersedes: old.PublicationReference);
            published[key] = replacement; await using var reader = content.Open();
            var history = await reader.Application.HistoryAsync(content.Scope, key, content.Variant, "OP-fixture-action-history", "fixture-action-history");
            Assert.Contains(history, row => row.PublicationReference == old.PublicationReference); Assert.Contains(history, row => row.PublicationReference == replacement.PublicationReference);
        }
        await AssertPublishedActionOptions(test, content, published); await NoEffects(test);
        await using var read = test.Database.Context(); Assert.Equal(0, await read.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
    }

    [Fact]
    public async Task PublishedContentSupportsExistingSinglePrepareAndBulkPreviewActivationWithoutProofOrRevocation()
    {
        var configured = await CreatePublishedActionScenario(); await using var test = configured.Scenario;
        _ = await PublishActionBundle(configured.Content);
        await using var read = test.Database.Context(); var target = await read.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == test.Targets[0].SessionId);
        string selectedCookies;
        using (var single = await test.Host.Post("/account/sessions/revocation/prepare", new
        { requestId = Guid.NewGuid(), targetReference = target.SessionId, expectedVersion = target.Version.ToString(CultureInfo.InvariantCulture) }, test.Actor.Cookies, test.Actor.Csrf))
        {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode); Headers(single);
            var body = await single.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("PENDING", body.GetProperty("status").GetString());
            selectedCookies = AuthenticationTestHost.Merge(test.Actor.Cookies, single);
        }
        var draft = await Preview(test, selectedCookies); var activated = await Activate(test, draft);
        Assert.Equal("PENDING", activated.View.GetProperty("status").GetString());
        await NoEffects(test);
    }

    [Fact]
    public async Task PublishedContentSingleSignOutCompletesFreshOriginalPasswordProofAndRevokesOnlySelectedSession()
    {
        var configured = await CreatePublishedActionScenario(); await using var test = configured.Scenario; var content = configured.Content;
        var published = await PublishActionBundle(content); await AssertPublishedActionOptions(test, content, published);
        AuthenticationSessionRow actorBefore; AuthenticationSessionRow targetBefore; AuthenticationSessionRow untouchedBefore;
        await using (var read = test.Database.Context())
        {
            actorBefore = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(row => row.SessionId == test.Actor.SessionId);
            targetBefore = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(row => row.SessionId == test.Targets[0].SessionId);
            untouchedBefore = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(row => row.SessionId == test.Targets[1].SessionId);
        }
        var request = Guid.NewGuid(); Original original;
        using (var prepared = await test.Host.Post("/account/sessions/revocation/prepare", new
        { requestId = request, targetReference = targetBefore.SessionId, expectedVersion = targetBefore.Version.ToString(CultureInfo.InvariantCulture) }, test.Actor.Cookies, test.Actor.Csrf))
        {
            Assert.Equal(HttpStatusCode.OK, prepared.StatusCode); var body = await prepared.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PENDING", body.GetProperty("status").GetString());
            original = new(request, body.GetProperty("operationReference").GetString()!, AuthenticationTestHost.Merge(test.Actor.Cookies, prepared), test.Actor.Csrf, body);
        }
        using (var premature = await test.Host.Post("/account/sessions/revocation/result", new { requestId = request }, original.Cookies, original.Csrf))
            Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
        await NoEffects(test); test.Host.Clock.Advance(TimeSpan.FromSeconds(1)); original = await Complete(test, original);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await test.Host.Post("/account/sessions/revocation/result", new { requestId = request }, original.Cookies, original.Csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("REVOKED", body.GetProperty("status").GetString()); Assert.Equal(original.OperationReference, body.GetProperty("operationReference").GetString());
            Assert.Equal(targetBefore.SessionId, body.GetProperty("target").GetProperty("reference").GetGuid());
            Assert.Equal((targetBefore.Version + 1).ToString(CultureInfo.InvariantCulture), body.GetProperty("target").GetProperty("version").GetString());
        }
        await using (var check = test.Database.Context())
        {
            var target = await check.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == targetBefore.SessionId);
            var actor = await check.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == actorBefore.SessionId);
            var untouched = await check.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == untouchedBefore.SessionId);
            Assert.Equal("REVOKED", target.State); Assert.Equal(targetBefore.Version + 1, target.Version);
            Assert.Equal(1, await check.Set<AuthenticationSessionRow>().CountAsync(row => row.State == "REVOKED"));
            Assert.Equal("ACTIVE", untouched.State); Assert.Equal(untouchedBefore.Version, untouched.Version); Assert.Equal("ACTIVE", actor.State);
            Assert.Equal(actorBefore.AuthenticatedAt, actor.AuthenticatedAt); Assert.Equal(actorBefore.AuthenticationMethods, actor.AuthenticationMethods); Assert.Equal(actorBefore.Assurance, actor.Assurance);
            var proof = await check.Set<StepUpProofRow>().SingleAsync();
            Assert.Equal("CONSUMED", proof.State); Assert.Equal("pwd", proof.AuthenticationMethod); Assert.Equal(test.Actor.SessionId, proof.SessionId);
            Assert.Equal(SessionRevocationBinding.Scope.Operation, proof.Operation); Assert.Equal(SessionRevocationBinding.Scope.Context, proof.Context);
            Assert.Equal("OP-SESSION-REVOKE-" + request.ToString("N"), proof.OperationId); Assert.Equal(original.OperationReference, proof.CorrelationId);
            Assert.Equal(targetBefore.SessionId.ToString("N"), proof.ResourceId); Assert.Equal(targetBefore.Version, proof.ExpectedVersion);
            Assert.True(proof.AuthenticatedAt > actorBefore.AuthenticatedAt && proof.EndedAt >= proof.AuthenticatedAt && proof.EndedAt < proof.ExpiresAt);
            var effect = await check.Set<OutboxMessage>().SingleAsync(message => message.EventType == "TunnerSessionRevoked");
            Assert.Equal(targetBefore.SessionId.ToString("N"), effect.Subject); Assert.Equal(proof.OperationId, effect.OperationId); Assert.Equal(original.OperationReference, effect.CorrelationId);
            var consumed = await check.Set<OutboxMessage>().SingleAsync(message => message.EventType == "StepUpProofConsumed");
            Assert.Equal(proof.ProofId.ToString("N"), consumed.Subject); Assert.Equal(proof.OperationId, consumed.OperationId);
        }
        test.Host.Clock.Advance(TimeSpan.FromSeconds(1)); const string completedKey = "account.sessions.revoke.success.title";
        published[completedKey] = await content.PublishTextAsync(completedKey, "Synthetic published completed single sign-out", supersedes: published[completedKey].PublicationReference);
        await AssertPublishedActionOptions(test, content, published);
        using (var current = await test.Host.Get("/authentication/session", original.Cookies)) Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using (var untouched = await test.Host.Get("/authentication/session", test.Targets[1].Cookies)) Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
    }

    [Fact]
    public async Task PublishedContentBulkSignOutCompletesFreshOriginalPasswordProofAndPreservesActorAndLaterSession()
    {
        var configured = await CreatePublishedActionScenario(); await using var test = configured.Scenario; var content = configured.Content;
        var published = await PublishActionBundle(content); await AssertPublishedActionOptions(test, content, published);
        AuthenticationSessionRow actorBefore;
        await using (var read = test.Database.Context()) actorBefore = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(row => row.SessionId == test.Actor.SessionId);
        var draft = await Preview(test);
        var exact = draft.View.GetProperty("targets").EnumerateArray().Select(target => new
        { Reference = target.GetProperty("reference").GetGuid(), Version = long.Parse(target.GetProperty("version").GetString()!, CultureInfo.InvariantCulture) }).ToArray();
        Assert.Equal(test.Targets.Select(target => target.SessionId).Order(), exact.Select(target => target.Reference).Order());
        var later = await Login(test.Host, test.Database); var original = await Activate(test, draft);
        await NoEffects(test); test.Host.Clock.Advance(TimeSpan.FromSeconds(1)); original = await Complete(test, original);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, original.Cookies, original.Csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("REVOKED", body.GetProperty("status").GetString()); Assert.Equal(original.OperationReference, body.GetProperty("operationReference").GetString());
            Assert.Equal(exact.Select(target => target.Reference).Order(), body.GetProperty("targets").EnumerateArray().Select(target => target.GetProperty("reference").GetGuid()).Order());
        }
        await using (var check = test.Database.Context())
        {
            var actor = await check.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == test.Actor.SessionId);
            var late = await check.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == later.SessionId);
            Assert.Equal("ACTIVE", actor.State); Assert.Equal("ACTIVE", late.State);
            Assert.Equal(actorBefore.AuthenticatedAt, actor.AuthenticatedAt); Assert.Equal(actorBefore.AuthenticationMethods, actor.AuthenticationMethods); Assert.Equal(actorBefore.Assurance, actor.Assurance);
            foreach (var target in exact)
            { var ended = await check.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == target.Reference); Assert.Equal("REVOKED", ended.State); Assert.Equal(target.Version + 1, ended.Version); }
            Assert.Equal(exact.Length, await check.Set<AuthenticationSessionRow>().CountAsync(row => row.State == "REVOKED"));
            var proof = await check.Set<StepUpProofRow>().SingleAsync();
            Assert.Equal("CONSUMED", proof.State); Assert.Equal("pwd", proof.AuthenticationMethod); Assert.Equal(test.Actor.SessionId, proof.SessionId);
            Assert.Equal(SessionBulkRevocationApplication.Scope.Operation, proof.Operation); Assert.Equal(SessionBulkRevocationApplication.Scope.Context, proof.Context);
            Assert.Equal("OP-SESSION-REVOKE-OTHERS-" + original.RequestId.ToString("N"), proof.OperationId); Assert.Equal(original.OperationReference, proof.CorrelationId);
            Assert.Equal(original.RequestId.ToString("N"), proof.ResourceId); Assert.Equal(0, proof.ExpectedVersion);
            Assert.True(proof.AuthenticatedAt > actorBefore.AuthenticatedAt && proof.EndedAt >= proof.AuthenticatedAt && proof.EndedAt < proof.ExpiresAt);
            var effects = await check.Set<OutboxMessage>().Where(message => message.EventType == "TunnerSessionRevoked").ToArrayAsync();
            Assert.Equal(exact.Select(target => target.Reference.ToString("N")).Order(StringComparer.Ordinal), effects.Select(message => message.Subject).Order(StringComparer.Ordinal));
            Assert.All(effects, effect => { Assert.Equal(proof.OperationId, effect.OperationId); Assert.Equal(original.OperationReference, effect.CorrelationId); });
            var consumed = await check.Set<OutboxMessage>().SingleAsync(message => message.EventType == "StepUpProofConsumed");
            Assert.Equal(proof.ProofId.ToString("N"), consumed.Subject); Assert.Equal(proof.OperationId, consumed.OperationId);
        }
        test.Host.Clock.Advance(TimeSpan.FromSeconds(1)); const string completedKey = "account.sessions.bulk.completed";
        published[completedKey] = await content.PublishTextAsync(completedKey, "Synthetic published completed bulk sign-out", supersedes: published[completedKey].PublicationReference);
        await AssertPublishedActionOptions(test, content, published);
        using (var current = await test.Host.Get("/authentication/session", original.Cookies)) Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using (var untouched = await test.Host.Get("/authentication/session", later.Cookies)) Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
    }

    [Theory]
    [InlineData("missing-publication")]
    [InlineData("wrong-binding")]
    [InlineData("expired-binding")]
    [InlineData("publication-expiry")]
    [InlineData("storage-outage")]
    [InlineData("policy-change-during-publication")]
    public async Task PublishedContentUnavailableFailsBothOptionsAndPreparationWithoutProofOrRevocation(string failure)
    {
        var configured = await CreatePublishedActionScenario(failure == "storage-outage"); await using var test = configured.Scenario; var content = configured.Content;
        _ = await PublishActionBundle(content, failure == "missing-publication", failure == "publication-expiry");
        if (failure == "wrong-binding") configured.Binding.Value = configured.Binding.Value with { AuthenticationPolicyVersion = "fixture-other-authentication" };
        if (failure == "expired-binding") configured.Binding.Value = configured.Binding.Value with { EffectiveInterval = new(test.Host.Clock.GetUtcNow().AddMinutes(-1), true, test.Host.Clock.GetUtcNow(), false) };
        if (failure == "publication-expiry") test.Host.Clock.Advance(TimeSpan.FromSeconds(2));
        var policyChanged = false;
        if (failure == "policy-change-during-publication") configured.Binding.AfterFinalRead = () =>
        { policyChanged = true; test.Host.Policies.Policy = test.Host.Policies.Policy with { Version = "fixture-action-authentication-v2" }; };
        foreach (var path in new[] { "/account/sessions/revocation/options", Root + "/options" })
        {
            using var response = await test.Host.Get(path, test.Actor.Cookies); await AssertActionUnavailable(response);
        }
        await using var read = test.Database.Context(); var target = await read.Set<AuthenticationSessionRow>().SingleAsync(row => row.SessionId == test.Targets[0].SessionId);
        using (var response = await test.Host.Post("/account/sessions/revocation/prepare", new
        { requestId = Guid.NewGuid(), targetReference = target.SessionId, expectedVersion = target.Version.ToString(CultureInfo.InvariantCulture) }, test.Actor.Cookies, test.Actor.Csrf))
            await AssertActionUnavailable(response);
        using (var response = await test.Host.Post(Root + "/preview", new { }, test.Actor.Cookies, test.Actor.Csrf)) await AssertActionUnavailable(response);
        if (failure == "policy-change-during-publication") Assert.True(policyChanged);
        await NoEffects(test);
    }

    private async Task<(Scenario Scenario, PresentationContentTestEnvironment Content, PublishedActionBindings Binding)> CreatePublishedActionScenario(bool storageOutage = false)
    {
        PresentationContentTestEnvironment? content = null; PublishedActionBindings? binding = null; IdentityTestDatabase? database = null;
        var scenario = await Create(synchronizeClock: true, start: (actual, configure) => { database = actual; return AuthenticationTestHost.Start(actual, configure); }, configure: (services, profile) =>
        {
            profile.Interval = NativeInterval;
            var clock = Assert.IsType<AuthenticationTestHost.TestClock>(services.Last(service => service.ServiceType == typeof(TimeProvider)).ImplementationInstance);
            var policy = Assert.IsType<AuthenticationTestHost.TestPolicies>(services.Last(service => service.ServiceType == typeof(IAuthenticationPolicySource)).ImplementationInstance).Policy;
            var texts = SessionRevocationPresentation.RequiredKeys.Concat(SessionBulkRevocationPresentation.RequiredKeys).ToDictionary(key => key, key => "Synthetic published action: " + key, StringComparer.Ordinal);
            content = new(database ?? throw new InvalidOperationException("The existing scenario database must be captured before composition."), clock, texts);
            binding = new(policy.Version, content);
            services.AddSingleton<IPresentationContentProfileSource>(content); services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization);
            var connection = storageOutage ? new NpgsqlConnectionStringBuilder(database!.ConnectionString) { Database = "fixture_absent_action_content_" + Guid.NewGuid().ToString("N") }.ConnectionString : database!.ConnectionString;
            services.AddTunnerPresentationContent(connection);
            services.Remove(services.Single(service => service.ServiceType == typeof(ISessionRevocationPresentationSource)));
            services.Remove(services.Single(service => service.ServiceType == typeof(ISessionBulkRevocationPresentationSource)));
            services.AddSingleton<ISessionRevocationContentBindingSource>(binding); services.AddSingleton<ISessionBulkRevocationContentBindingSource>(binding);
            services.AddTunnerPublishedSessionRevocationPresentation(); services.AddTunnerPublishedSessionBulkRevocationPresentation();
        });
        try
        {
            Assert.NotNull(content); Assert.NotNull(binding); Assert.Same(scenario.Host.Clock, content.Clock);
            // The existing scenario synchronized this shared clock to system time. Advance, never rewind, before Content seeding.
            var remainder = scenario.Host.Clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond;
            if (remainder != 0) scenario.Host.Clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond - remainder));
            Assert.Equal(0L, scenario.Host.Clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond);
            await content.InitializeAsync(); return (scenario, content, binding);
        }
        catch { await scenario.DisposeAsync(); throw; }
    }
    private static async Task<Dictionary<string, ContentHistoryEntry>> PublishActionBundle(PresentationContentTestEnvironment content, bool missing = false, bool expires = false)
    {
        var result = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
        foreach (var definition in content.Profile.Definitions)
        {
            if (missing && definition.Key == SessionRevocationPresentation.RequiredKeys[0]) continue;
            if (!expires) { result.Add(definition.Key, await content.PublishTextAsync(definition.Key, "Synthetic published action: " + definition.Key)); continue; }
            await using var writer = content.Open(); content.Authorization.Actor = "fixture-author"; var proposal = await writer.Application.CreateDraftAsync(content.Draft("Synthetic published action: " + definition.Key, definition.Key));
            proposal = await writer.Application.ValidateAsync(content.Command(proposal)); content.Authorization.Actor = "fixture-approver"; proposal = await writer.Application.ApproveAsync(content.Command(proposal)); content.Authorization.Actor = "fixture-publisher";
            result.Add(definition.Key, await writer.Application.PublishAsync(content.Publish(proposal) with { EffectiveInterval = new(content.Clock.GetUtcNow(), true, content.Clock.GetUtcNow().AddSeconds(1), false) }));
        }
        return result;
    }
    private static async Task AssertPublishedActionOptions(Scenario test, PresentationContentTestEnvironment content, Dictionary<string, ContentHistoryEntry> publications)
    {
        foreach (var path in new[] { "/account/sessions/revocation/options", Root + "/options" })
        {
            using var response = await test.Host.Get(path, test.Actor.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("en", body.GetProperty("locale").GetString());
            foreach (var (key, expected) in publications)
            {
                var text = body.GetProperty("content").GetProperty(key); Assert.Equal(expected.PublicationReference.ToString("N"), text.GetProperty("publicationRef").GetString());
                Assert.Equal(expected.VersionReference.ToString("N"), text.GetProperty("versionRef").GetString()); Assert.Equal(content.Variant, text.GetProperty("variantRef").GetString());
                Assert.Equal("PUBLISHED", text.GetProperty("state").GetString()); Assert.False(string.IsNullOrWhiteSpace(text.GetProperty("text").GetString()));
            }
            foreach (var absent in new[] { "requestId", "operationReference", "target", "targets", "csrfToken" }) Assert.False(body.TryGetProperty(absent, out _));
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }
    private static async Task AssertActionUnavailable(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("DEPENDENCY_UNAVAILABLE", body, StringComparison.Ordinal); Assert.False(response.Headers.Contains("Set-Cookie"));
        foreach (var forbidden in new[] { "fixture_absent_action_content_", "Npgsql", "Password=" }) Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body); foreach (var key in new[] { "content", "requestId", "operationReference", "target", "targets" }) Assert.False(document.RootElement.TryGetProperty(key, out _));
    }
    private sealed class PublishedActionBindings(string policyVersion, PresentationContentTestEnvironment content) : ISessionRevocationContentBindingSource, ISessionBulkRevocationContentBindingSource
    {
        internal int Reads; internal Action? AfterFinalRead;
        internal SessionActionContentBinding Value = new(policyVersion, "fixture-action-binding", "fixture-action-qualification", content.Scope, content.Variant, content.Interval);
        public ValueTask<SessionActionContentBinding> RequireQualifiedAsync(AuthenticationPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Reads++; if (Reads == 2) AfterFinalRead?.Invoke(); return ValueTask.FromResult(Value); }
    }
}
