using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

// Public configuration/publication reads only. Private Recovery wire/effects retain the fixed native cohort.
public sealed class IdentityRuntimeRecoveryPasswordContentBindingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private const string DraftPath = "/recovery-password-content/draft";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ActualAdminPublicationFeedsPublishedRecoveryMappingWithReplayReplacementHistoryAndForwardRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        await using var admin = await Host.Start(db, additionalScopes: [Scope(f)], recoveryPasswordPolicies: policies); var env = admin.Environment;
        var author = await admin.Browser("fixture-security-author"); var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        using (var workspace = await admin.Get("/workspace", author.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
            var facts = (await Body(workspace)).GetProperty("workspaces").EnumerateArray().Single(x => x.GetProperty("scope").GetProperty("family").GetString() == "RecoveryPasswordContent")
                .GetProperty("recoveryPasswordContentOwnerFacts");
            Assert.Equal(f.Policy.Version, facts.GetProperty("recoveryPasswordPolicyVersion").GetString());
            Assert.Equal(f.Policy.UnresolvedDownstreamEffects, facts.GetProperty("followUpReferences").EnumerateArray().Select(x => x.GetString()!));
        }
        var content = new PresentationContentTestEnvironment(db, env.Clock, f.Resolver.Result.Content.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.Ordinal));
        await content.InitializeAsync(); ContentHistoryEntry? originalGoal = null;
        foreach (var text in f.Resolver.Result.Content.Values)
        { var seededPublication = await content.PublishTextAsync(text.Key, text.Text); if (text.Key == f.Binding.GoalLabelKey) originalGoal = seededPublication; }
        var candidate = Values(f, env, "fixture-recovery-map-v1") with { ContentScope = content.Scope, VariantReference = content.Variant };
        var key = Guid.NewGuid(); var request = new { idempotencyKey = key, candidate }; JsonElement proposal;
        using (var created = await admin.Post(DraftPath, request, author))
        { Assert.Equal(HttpStatusCode.OK, created.StatusCode); proposal = (await Body(created)).GetProperty("result").Clone(); }
        using (var replay = await admin.Post(DraftPath, request, author))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        using (var update = await admin.Post(DraftPath, new { idempotencyKey = Guid.NewGuid(), candidate = candidate with { ConfigurationVersionReference = "fixture-recovery-map-v2" },
            proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, update.StatusCode); proposal = (await Body(update)).GetProperty("result").Clone(); }
        using (var preview = await admin.Post("/proposals/preview", Target(proposal), author))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode); var body = await Body(preview);
            Assert.Equal("fixture-recovery-map-v2", body.GetProperty("recoveryPasswordContent").GetProperty("configurationVersionReference").GetString());
            Assert.False(body.GetProperty("recoveryPasswordContent").TryGetProperty("policy", out _));
        }
        proposal = await Step(admin, author, "validate", proposal);
        using (var forbidden = await admin.Post("/proposals/approve", new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, author))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        proposal = await Step(admin, approver, "approve", proposal); var first = await Publish(admin, publisher, proposal);
        await using var services = Services(env, policies); var source = services.GetRequiredService<IRecoveryPasswordContentBindingSource>();
        var selected = await source.RequireQualifiedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None);
        await using (var read = env.Context()) Assert.Equal((await read.Set<IdentityConfigurationPublicationRow>().SingleAsync(x => x.Id == first.GetProperty("publicationReference").GetGuid())).ApprovalEventReference.ToString("N"), selected.QualificationReference);
        Assert.Equal(f.Binding.Policy.Snapshot, selected.Policy.Snapshot); Assert.Equal(f.Binding.Policy.Metadata.EffectiveInterval.Until, selected.EffectiveInterval.Until);
        await using var reader = content.Open(); var adapter = new PublishedRecoveryPasswordPresentationSource(reader.Application, source, env.Clock);
        var publication = await adapter.RequirePublishedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(originalGoal!.PublicationReference.ToString("N"), publication.Content[f.Binding.GoalLabelKey].PublicationRef);
        Assert.Equal(candidate.FollowUps, publication.FollowUps); Assert.Equal(candidate.EffectValueKey, publication.EffectValueKey);
        env.Clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = await content.PublishTextAsync(f.Binding.GoalLabelKey, "Synthetic reviewed Recovery mapping wording", supersedes: originalGoal.PublicationReference);
        var current = await adapter.RequirePublishedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(replacement.PublicationReference.ToString("N"), current.Content[f.Binding.GoalLabelKey].PublicationRef);
        Assert.Equal("Synthetic reviewed Recovery mapping wording", current.Content[f.Binding.GoalLabelKey].Text); Assert.Equal(publication.FollowUps, current.FollowUps);
        using (var create = await admin.Post(DraftPath, new { idempotencyKey = Guid.NewGuid(), candidate = candidate with { ConfigurationVersionReference = "fixture-recovery-map-v3" } }, author))
        { Assert.Equal(HttpStatusCode.OK, create.StatusCode); proposal = (await Body(create)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal);
        var next = await Publish(admin, publisher, proposal, first.GetProperty("publicationReference").GetGuid()); env.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = next.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow()) }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("fixture-recovery-map-v2", (await source.RequireQualifiedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None)).ConfigurationVersionReference);
        using (var history = await admin.Post("/history/preview", new { scope = Scope(f), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal("fixture-recovery-map-v2", (await Body(history)).GetProperty("recoveryPasswordContent").GetProperty("configurationVersionReference").GetString()); }
        var retained = await reader.Application.HistoryAsync(content.Scope, f.Binding.GoalLabelKey, content.Variant, "OP-recovery-map-history", "fixture-correlation");
        Assert.Contains(retained, x => x.PublicationReference == originalGoal.PublicationReference); Assert.Contains(retained, x => x.PublicationReference == replacement.PublicationReference);
        await using var user = await AuthenticationTestHost.Start(db, s =>
        {
            s.AddSingleton<TimeProvider>(env.Clock);
            s.AddSingleton<IPresentationContentProfileSource>(content); s.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization);
            s.AddTunnerPresentationContent(db.ConnectionString); s.AddTunnerPersistedRecoveryPasswordContentBinding(); s.AddTunnerPublishedRecoveryPasswordPresentation();
        });
        using (var bootstrap = await user.Get("/authentication/options")) Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        Assert.Null(user.Services.GetService<RecoveryBrowserService>());
        var routes = user.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(x => x.RoutePattern.RawText);
        Assert.DoesNotContain(routes, route => route is not null && route.StartsWith("/recovery", StringComparison.Ordinal));
        await AssertNoRecoveryEffects(env);
    }

    [Fact]
    public async Task AppliedReplayAndHistoryRemainFactualAfterOwnerWithdrawalButFreshWritesAndRuntimeFailClosed()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        await using var admin = await Host.Start(db, additionalScopes: [Scope(f)], recoveryPasswordPolicies: policies); var env = admin.Environment;
        var author = await admin.Browser("fixture-security-author"); var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        var request = new { idempotencyKey = Guid.NewGuid(), candidate = Values(f, env, "fixture-replay-owner") }; JsonElement proposal;
        using (var create = await admin.Post(DraftPath, request, author)) { Assert.Equal(HttpStatusCode.OK, create.StatusCode); proposal = (await Body(create)).GetProperty("result").Clone(); }
        proposal = await Step(admin, author, "validate", proposal); proposal = await Step(admin, approver, "approve", proposal); var publication = await Publish(admin, publisher, proposal);
        await using var before = env.Platform(); var effects = (await before.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()).Select(x => JsonSerializer.Serialize(x)).ToArray();
        policies.Current = null;
        using (var replay = await admin.Post(DraftPath, request, author)) { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        await using (var after = env.Platform()) Assert.Equal(effects, (await after.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()).Select(x => JsonSerializer.Serialize(x)).ToArray());
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post(DraftPath, request, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        admin.Owners.Denied = null;
        using (var history = await admin.Post("/history/preview", new { scope = Scope(f), publicationReference = publication.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.True((await Body(history)).TryGetProperty("recoveryPasswordContent", out _)); }
        using (var fresh = await admin.Post(DraftPath, new { idempotencyKey = Guid.NewGuid(), request.candidate }, author)) Assert.Equal(HttpStatusCode.ServiceUnavailable, fresh.StatusCode);
        using (var workspace = await admin.Get("/workspace", author.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, workspace.StatusCode); var rows = (await Body(workspace)).GetProperty("workspaces").EnumerateArray().ToArray();
            Assert.Equal(4, rows.Length); Assert.All(rows, x => Assert.False(x.TryGetProperty("recoveryPasswordContentOwnerFacts", out _)));
        }
        await using var services = Services(env, policies);
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => services.GetRequiredService<IRecoveryPasswordContentBindingSource>().RequireQualifiedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None).AsTask());
        await AssertNoRecoveryEffects(env);
    }

    [Fact]
    public async Task FollowUpCollectionIsDetachedBeforeTheFirstAuthorizationAwaitAndAuthorizedDigestMatchesPersistedOrder()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        var rows = f.Binding.FollowUps.Reverse().ToList(); var original = rows.ToArray(); var candidate = Values(f, env, "fixture-detached") with { FollowUps = rows };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? authorizedDigest = null;
        var authorization = new CallbackAuthorization(env.Authorization, async request => { authorizedDigest = request.CandidateDigest; entered.SetResult(); await release.Task; });
        await using var context = env.Context(); var app = new IdentityRuntimeConfigurationApplication(context, env, authorization, env.Clock, policies);
        var pending = app.CreateRecoveryPasswordContentDraftAsync(candidate, "OP-detach", "fixture-correlation");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); rows[0] = rows[0] with { LabelKey = "changed-after-authorization" }; rows.Reverse(); release.SetResult();
        var proposal = await pending; var row = await context.Set<IdentityConfigurationProposalRow>().AsNoTracking().SingleAsync(); var persisted = IdentityConfigurationDocument.Read(row.Payload);
        Assert.Equal(original, persisted.RecoveryPasswordContent!.FollowUps); Assert.Equal(authorizedDigest, persisted.Digest); Assert.Equal(proposal.CandidateDigest, persisted.Digest);
        await using var services = Services(env, policies);
        var approved = await Approve(env, policies, proposal); await PublishOwner(env, policies, approved);
        Assert.Equal(original, (await services.GetRequiredService<IRecoveryPasswordContentBindingSource>().RequireQualifiedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None)).FollowUps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshMutationRollsBackIfRecoveryOwnerChangesDuringAuthorizationOrAfterSave(bool afterSave)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        if (afterSave) env.Interceptors = [new AfterSave(() => policies.Current = null)];
        IIdentityConfigurationAuthorizationSource authorization = afterSave ? env.Authorization : new CallbackAuthorization(env.Authorization, _ =>
        { policies.Current = Changed(f.Binding.Policy); return Task.CompletedTask; });
        await using (var context = env.Context())
        {
            var app = new IdentityRuntimeConfigurationApplication(context, env, authorization, env.Clock, policies);
            await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => app.CreateRecoveryPasswordContentDraftAsync(Values(f, env, "fixture-withdrawal"), "OP-withdraw", "fixture-correlation"));
        }
        await AssertNoConfiguration(env);
    }

    [Theory]
    [InlineData("missing-owner")]
    [InlineData("wrong-version")]
    [InlineData("missing-followup")]
    public async Task FreshDraftRequiresActualOwnerAndItsExactFollowUpSet(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        var candidate = Values(f, env, "fixture-invalid-owner");
        if (failure == "missing-owner") policies.Current = null;
        if (failure == "wrong-version") candidate = candidate with { RecoveryPasswordPolicyVersion = "foreign-owner" };
        if (failure == "missing-followup") candidate = candidate with { FollowUps = candidate.FollowUps.Take(1).ToArray() };
        await using (var context = env.Context()) await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() =>
            new IdentityRuntimeConfigurationApplication(context, env, env.Authorization, env.Clock, policies).CreateRecoveryPasswordContentDraftAsync(candidate, "OP-invalid-owner", "fixture-correlation"));
        await AssertNoConfiguration(env);
    }

    [Theory]
    [InlineData("raw-values")]
    [InlineData("owner-snapshot")]
    [InlineData("missing-owner")]
    public async Task AsyncRuntimeReadRejectsDifferentRawValuesOrOwnerChangedDuringTheActualDatabaseRead(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        await PublishOwner(env, policies, await Approve(env, policies, await Draft(env, policies, Values(f, env, "fixture-runtime"))));
        var requested = failure == "raw-values" ? f.Policy with { GoalReference = "foreign-goal" } : f.Policy;
        if (failure != "raw-values") env.Interceptors = [new AfterAsyncRead(() => policies.Current = failure == "missing-owner" ? null : Changed(f.Binding.Policy))];
        await using var services = Services(env, policies);
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => services.GetRequiredService<IRecoveryPasswordContentBindingSource>().RequireQualifiedAsync(requested, env.Clock.GetUtcNow(), CancellationToken.None).AsTask());
        await AssertNoRecoveryEffects(env);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentPublicationBoundsAreCheckedAtTheFreshClockAfterTheFinalPolicyRead(bool includesUntil)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        var end = env.Clock.GetUtcNow().AddSeconds(1);
        await PublishOwner(env, policies, await Approve(env, policies, await Draft(env, policies, Values(f, env, "fixture-expiring"))), new(env.Clock.GetUtcNow(), true, end, includesUntil));
        policies.Reads = 0; policies.OnRead = count => { if (count == 2) env.Clock.Advance(TimeSpan.FromSeconds(1) + (includesUntil ? TimeSpan.FromTicks(10) : TimeSpan.Zero)); };
        await using var services = Services(env, policies);
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => services.GetRequiredService<IRecoveryPasswordContentBindingSource>().RequireQualifiedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None).AsTask());
        Assert.Equal(2, policies.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservedFutureMappingClipsTheActualSelectionWithExactBoundary(bool includesFrom)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        var first = await PublishOwner(env, policies, await Approve(env, policies, await Draft(env, policies, Values(f, env, "fixture-current"))));
        var at = env.Clock.GetUtcNow().AddSeconds(2);
        await PublishOwner(env, policies, await Approve(env, policies, await Draft(env, policies, Values(f, env, "fixture-future"))), new(at, includesFrom, null, false), first.PublicationReference);
        await using var services = Services(env, policies); var source = services.GetRequiredService<IRecoveryPasswordContentBindingSource>();
        var selected = await source.RequireQualifiedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(at, selected.EffectiveInterval.Until); Assert.Equal(!includesFrom, selected.EffectiveInterval.IncludesUntil);
        env.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(includesFrom ? "fixture-future" : "fixture-current", (await source.RequireQualifiedAsync(f.Policy, env.Clock.GetUtcNow(), CancellationToken.None)).ConfigurationVersionReference);
    }

    [Fact]
    public async Task ExplicitEmptyOwnerFollowUpsAreQualifiedFactsAndCanBePublishedWithoutInventedRows()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var f = new PublishedRecoveryPasswordPresentationTests.Fixture();
        var owner = new RecoveryPasswordPolicyVersion(f.Binding.Policy.Metadata, f.Policy with { UnresolvedDownstreamEffects = Array.Empty<string>() }); var policies = new Owner(owner);
        await using var admin = await Host.Start(db, additionalScopes: [Scope(f)], recoveryPasswordPolicies: policies); var env = admin.Environment;
        var actor = await admin.Browser("fixture-security-author");
        using (var workspace = await admin.Get("/workspace", actor.Cookies))
        {
            var facts = (await Body(workspace)).GetProperty("workspaces").EnumerateArray().Single(x => x.TryGetProperty("recoveryPasswordContentOwnerFacts", out _)).GetProperty("recoveryPasswordContentOwnerFacts");
            Assert.Equal(0, facts.GetProperty("followUpReferences").GetArrayLength());
        }
        var candidate = Values(f, env, "fixture-empty-followups") with { FollowUps = Array.Empty<RecoveryPasswordFollowUp>() };
        await PublishOwner(env, policies, await Approve(env, policies, await Draft(env, policies, candidate)));
        await using var services = Services(env, policies);
        Assert.Empty((await services.GetRequiredService<IRecoveryPasswordContentBindingSource>().RequireQualifiedAsync(owner.Values, env.Clock.GetUtcNow(), CancellationToken.None)).FollowUps);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("duplicate")]
    [InlineData("authority")]
    public async Task MalformedTypedHttpMappingIs400AndDoesNotChangeAnyOwnerOrPlatformRecord(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var policies = new Owner(f.Binding.Policy);
        await using var admin = await Host.Start(db, additionalScopes: [Scope(f)], recoveryPasswordPolicies: policies); var actor = await admin.Browser("fixture-security-author"); var env = admin.Environment;
        var body = JsonSerializer.SerializeToNode(new { idempotencyKey = Guid.NewGuid(), candidate = Values(f, env, "fixture-http-invalid") }, WebJson)!;
        if (failure == "key") body["candidate"]!["effectValueKey"] = " ";
        if (failure == "duplicate") body["candidate"]!["followUps"]![1]!["reference"] = f.Binding.FollowUps[0].Reference;
        if (failure == "authority") body["candidate"]!["policy"] = JsonSerializer.SerializeToNode(f.Policy, WebJson);
        await using var platform = env.Platform();
        var beforeEvents = (await platform.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()).Select(x => JsonSerializer.Serialize(x)).ToArray();
        var beforeAudits = (await platform.AuditEvents.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()).Select(x => JsonSerializer.Serialize(x)).ToArray();
        using (var response = await admin.Post(DraftPath, body, actor)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(beforeEvents, (await platform.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()).Select(x => JsonSerializer.Serialize(x)).ToArray());
        Assert.Equal(beforeAudits, (await platform.AuditEvents.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()).Select(x => JsonSerializer.Serialize(x)).ToArray());
        await using var context = env.Context(); Assert.Empty(await context.Set<IdentityConfigurationProposalRow>().ToArrayAsync()); Assert.Empty(await context.Set<IdentityConfigurationVersionRow>().ToArrayAsync()); Assert.Empty(await context.Set<IdentityConfigurationPublicationRow>().ToArrayAsync());
    }

    [Fact]
    public void PreviousBulkPayloadAndScopeBytesAreUnchangedAndNewDimensionsCannotLeakIntoOldFamilies()
    {
        // RECONSTRUCTED 307f5032 family14 format, not a retained runtime payload.
        var binding = new SessionActionContentBinding("fixture-auth", "fixture-bulk", "fixture-qualified", "fixture-content", "fixture-variant", new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, null, false));
        var prior = JsonSerializer.Serialize(new LegacyBulkDocument(null, null, null, binding)); var document = IdentityConfigurationDocument.Read(prior);
        Assert.Equal(prior, document.Snapshot); Assert.Equal(Sha(prior), document.Digest); Assert.Null(document.RecoveryPasswordContent);
        Assert.Equal("{\"Family\":14,\"AssuranceScope\":null,\"AuthenticationPolicyVersion\":\"fixture-auth\"}", document.Scope.Key);
        foreach (var family in Enum.GetValues<IdentityConfigurationFamily>().Where(x => x != IdentityConfigurationFamily.RecoveryPasswordContent))
            Assert.Throws<IdentityConfigurationUnavailableException>(() => new IdentityConfigurationScope(family, RecoveryPasswordPolicyVersion: "foreign-dimension").Require());
        Assert.Equal(15, (int)IdentityConfigurationFamily.RecoveryPasswordContent);
    }

    [Fact]
    public void ExplicitRegistrationSelectsOnlyTheMappingPortAndOldHelpersDoNotActivateRecovery()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration(); Assert.DoesNotContain(old, x => x.ServiceType == typeof(IRecoveryPasswordContentBindingSource));
        var services = new ServiceCollection(); services.AddTunnerPersistedRecoveryPasswordContentBinding();
        Assert.Contains(services, x => x.ServiceType == typeof(IRecoveryPasswordContentBindingSource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IRecoveryPasswordPolicySource) || x.ServiceType == typeof(IRecoveryPasswordPresentationSource) || x.ServiceType == typeof(RecoveryBrowserService) || x.ServiceType == typeof(RecoveryCaseApplication) || x.ServiceType == typeof(IAuthenticationPolicySource));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedRecoveryPasswordContentBinding());
    }

    private sealed class Owner(RecoveryPasswordPolicyVersion policy) : IRecoveryPasswordPolicySource
    {
        internal RecoveryPasswordPolicyVersion? Current = policy; internal int Reads; internal Action<int>? OnRead;
        public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at) { OnRead?.Invoke(++Reads); return Current ?? throw new InvalidOperationException("Synthetic owner unavailable."); }
    }
    private sealed class CallbackAuthorization(IIdentityConfigurationAuthorizationSource inner, Func<IdentityConfigurationAuthorizationRequest, Task> callback) : IIdentityConfigurationAuthorizationSource
    {
        private bool invoked;
        public async ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken)
        { if (!invoked) { invoked = true; await callback(request); } return await inner.AuthorizeAsync(request, cancellationToken); }
    }
    private sealed class AfterSave(Action callback) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { callback(); return ValueTask.FromResult(result); }
    }
    private sealed class AfterAsyncRead(Action callback) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        { callback(); return ValueTask.FromResult(result); }
    }
    private sealed record LegacyBulkDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionActionContentBinding? SessionBulkRevocationContent);
    private static RecoveryPasswordPolicyVersion Changed(RecoveryPasswordPolicyVersion original) => new(original.Metadata, original.Values with { GoalReference = "different-qualified-goal" });
    private static IdentityConfigurationScope Scope(PublishedRecoveryPasswordPresentationTests.Fixture f) => new(IdentityConfigurationFamily.RecoveryPasswordContent, RecoveryPasswordPolicyVersion: f.Policy.Version);
    private static RecoveryPasswordContentBindingValues Values(PublishedRecoveryPasswordPresentationTests.Fixture f, RuntimeConfigurationTestEnvironment env, string version) =>
        new(f.Policy.Version, version, "candidate-evidence-not-authority", f.Binding.ContentScope, f.Binding.VariantReference, env.Interval,
            f.Binding.GoalLabelKey, f.Binding.GoalDescriptionKey, f.Binding.EffectLabelKey, f.Binding.EffectValueKey, f.Binding.FollowUps.ToArray());
    private static ServiceProvider Services(RuntimeConfigurationTestEnvironment env, Owner owner)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(env.Clock); services.AddSingleton<IRecoveryPasswordPolicySource>(owner); services.AddScoped(_ => env.Context());
        services.AddTunnerPersistedRecoveryPasswordContentBinding(); return services.BuildServiceProvider();
    }
    private static async Task<IdentityConfigurationProposal> Draft(RuntimeConfigurationTestEnvironment env, Owner owner, RecoveryPasswordContentBindingValues value)
    {
        env.Authorization.Actor = "fixture-security-author"; await using var context = env.Context();
        return await new IdentityRuntimeConfigurationApplication(context, env, env.Authorization, env.Clock, owner).CreateRecoveryPasswordContentDraftAsync(value, "OP-mapping-create", "fixture-correlation");
    }
    private static async Task<IdentityConfigurationProposal> Approve(RuntimeConfigurationTestEnvironment env, Owner owner, IdentityConfigurationProposal proposal)
    {
        await using var context = env.Context(); var app = new IdentityRuntimeConfigurationApplication(context, env, env.Authorization, env.Clock, owner);
        env.Authorization.Actor = "fixture-security-author"; proposal = await app.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        return await app.ApproveAsync(env.Command(proposal));
    }
    private static async Task<IdentityConfigurationHistory> PublishOwner(RuntimeConfigurationTestEnvironment env, Owner owner, IdentityConfigurationProposal proposal, EffectivePolicyInterval? interval = null, Guid? supersedes = null)
    {
        await using var context = env.Context(); env.Authorization.Actor = "fixture-security-publisher";
        return await new IdentityRuntimeConfigurationApplication(context, env, env.Authorization, env.Clock, owner).PublishAsync(env.Publication(proposal, supersedes) with { EffectiveInterval = interval ?? new(env.Clock.GetUtcNow(), true, null, false) });
    }
    private static async Task AssertNoConfiguration(RuntimeConfigurationTestEnvironment env)
    {
        await using var context = env.Context(); Assert.Empty(await context.Set<IdentityConfigurationProposalRow>().ToArrayAsync()); Assert.Empty(await context.Set<IdentityConfigurationVersionRow>().ToArrayAsync()); Assert.Empty(await context.Set<IdentityConfigurationPublicationRow>().ToArrayAsync());
        await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync());
    }
    private static async Task AssertNoRecoveryEffects(RuntimeConfigurationTestEnvironment env)
    {
        await using var context = env.Context(); Assert.Empty(await context.Accounts.ToArrayAsync()); Assert.Empty(await context.Set<AuthenticationSessionRow>().ToArrayAsync()); Assert.Empty(await context.Set<StepUpProofRow>().ToArrayAsync());
        Assert.Empty(await context.Set<RecoveryEntryRow>().ToArrayAsync()); Assert.Empty(await context.Set<RecoveryCaseRow>().ToArrayAsync()); Assert.Empty(await context.Set<RecoveryResultRow>().ToArrayAsync());
    }
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static object Target(JsonElement proposal) => new { proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() };
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Step(Host host, Browser actor, string operation, JsonElement proposal)
    {
        using var response = await host.Post("/proposals/" + operation, new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Publish(Host host, Browser actor, JsonElement proposal, Guid? supersedes = null)
    {
        using var response = await host.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64(), effectiveInterval = Interval(host.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
}
