using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tunner.Admin.Bff;
using Tunner.Database.Outbox;
using Tunner.Platform.Bff;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Actual HTTPS/Admin-cookie/CSRF operations with isolated test-only workforce/session authority.
/// No live workforce sign-in, production grant or provider activation is claimed.</summary>
public sealed class AdminContentHttpTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CapabilitiesBindExactResourcesAndCurrentActorStateAndSeparationWithoutAuthorizingCommands()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db);
        var author = await h.Browser("fixture-author"); var approver = await h.Browser("fixture-approver"); var publisher = await h.Browser("fixture-publisher");
        using (var workspace = await h.Post("/content/capabilities", new { scope = h.Environment.Scope, target = "Workspace" }, author))
        { Assert.Equal(HttpStatusCode.OK, workspace.StatusCode); Assert.Equal(["Browse"], Actions(await Body(workspace))); }
        using (var definition = await h.Post("/content/capabilities", new { scope = h.Environment.Scope, target = "Definition", key = "login.heading", variantReference = h.Environment.Variant }, author))
        { Assert.Equal(HttpStatusCode.OK, definition.StatusCode); Assert.Equal(["CreateDraft", "History"], Actions(await Body(definition))); }
        var draft = await Draft(h, author, "Fixture capability heading");
        Assert.Equal(["UpdateDraft", "Validate", "Preview"], await ProposalActions(h, author, draft));
        var validated = await Proposal(h, author, "validate", draft);
        Assert.DoesNotContain("Approve", await ProposalActions(h, author, validated));
        Assert.Contains("Approve", await ProposalActions(h, approver, validated));
        Assert.DoesNotContain("Publish", await ProposalActions(h, publisher, validated));
        var approved = await Proposal(h, approver, "approve", validated);
        Assert.DoesNotContain("Publish", await ProposalActions(h, author, approved));
        Assert.DoesNotContain("Publish", await ProposalActions(h, approver, approved));
        Assert.Contains("Publish", await ProposalActions(h, publisher, approved));
        var probes = h.Owners.Requests.Where(x => x.IsCapabilityProbe && x.Operation == ContentOperation.Publish).ToArray();
        Assert.Contains(probes, x => x.TargetReference == approved.GetProperty("proposalReference").GetGuid().ToString("N") && x.ExpectedRevision == approved.GetProperty("revision").GetInt64());
        h.Owners.Denied = ContentOperation.Publish;
        Assert.DoesNotContain("Publish", await ProposalActions(h, publisher, approved));
        using (var denied = await h.Post("/content/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = approved.GetProperty("proposalReference").GetGuid(),
            expectedRevision = approved.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Clock.GetUtcNow()) }, publisher)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains(h.Owners.Requests, x => !x.IsCapabilityProbe && x.Operation == ContentOperation.Publish);
        h.Owners.Denied = null; var publication = await Publish(h, publisher, approved);
        Assert.Equal(["Preview"], await ProposalActions(h, publisher, (await CurrentProposal(h, publisher))));
        using var history = await h.Post("/content/capabilities", new { scope = h.Environment.Scope, target = "History", publicationReference = publication.GetProperty("publicationReference").GetGuid() }, publisher);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal(["Rollback"], Actions(await Body(history)));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("mixed")]
    [InlineData("unavailable")]
    public async Task CapabilityProjectionRejectsStaleMixedOrUnavailableQualification(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        var draft = await Draft(h, author, "Fixture exact target");
        if (failure == "stale") _ = await Proposal(h, author, "validate", draft);
        h.Owners.ProbesAvailable = failure != "unavailable";
        using var response = await h.Post("/content/capabilities", new { scope = h.Environment.Scope, target = "Proposal",
            proposalReference = draft.GetProperty("proposalReference").GetGuid(), expectedRevision = draft.GetProperty("revision").GetInt64(),
            key = failure == "mixed" ? "login.heading" : null }, author);
        Assert.Equal(failure == "stale" ? HttpStatusCode.Conflict : failure == "mixed" ? HttpStatusCode.BadRequest : HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task WorkspaceUsesOnlyExplicitPublishedPresentationBindingAndPreservesAbsentBinding()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        using (var absent = await h.Get("/content/workspace", author.Cookies)) Assert.Equal(JsonValueKind.Null, (await Body(absent)).GetProperty("presentation").ValueKind);
        var approver = await h.Browser("fixture-approver"); var publisher = await h.Browser("fixture-publisher");
        var proposal = await Proposal(h, author, "validate", await Draft(h, author, "Synthetic published Admin test bundle"));
        proposal = await Proposal(h, approver, "approve", proposal); var publication = await Publish(h, publisher, proposal);
        h.Owners.Presentation = new(h.Environment.Scope, h.Environment.Variant, h.Environment.Interval);
        using var response = await h.Get("/content/workspace", author.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bundle = (await Body(response)).GetProperty("presentation"); Assert.Equal(h.Environment.Scope, bundle.GetProperty("scope").GetString());
        Assert.Equal(h.Environment.Variant, bundle.GetProperty("requestedVariantReference").GetString());
        var value = bundle.GetProperty("content").GetProperty("login.heading");
        Assert.Equal(publication.GetProperty("publicationReference").GetGuid().ToString("N"), value.GetProperty("publicationReference").GetString());
        Assert.Equal("Synthetic published Admin test bundle", value.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("unpublished")]
    public async Task WorkspacePresentationFailsClosedForUnqualifiedOrUnpublishedBinding(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        h.Owners.Presentation = new(h.Environment.Scope, h.Environment.Variant, failure == "expired" ? new(h.Clock.GetUtcNow().AddDays(-2), true, h.Clock.GetUtcNow().AddDays(-1), false) : h.Environment.Interval);
        using var response = await h.Get("/content/workspace", author.Cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Npgsql", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static string[] Actions(JsonElement result) => result.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray();
    private static async Task<string[]> ProposalActions(TestHost h, Browser actor, JsonElement proposal)
    {
        using var response = await h.Post("/content/capabilities", new { scope = h.Environment.Scope, target = "Proposal",
            proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var result = await Body(response);
        Assert.Equal(proposal.GetProperty("revision").GetInt64(), result.GetProperty("expectedRevision").GetInt64()); return Actions(result);
    }
    private static async Task<JsonElement> CurrentProposal(TestHost h, Browser actor)
    {
        using var response = await h.Post("/content/proposals/search", new { scope = h.Environment.Scope, pageSize = 5, offset = 0 }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("items")[0].Clone();
    }
    [Fact]
    public async Task GuidedAdminContentWorkflowPreservesPurposeDiffApprovalPublicationHistoryAndRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db);
        var author = await h.Browser("fixture-author"); var approver = await h.Browser("fixture-approver"); var publisher = await h.Browser("fixture-publisher");
        using (var definitions = await h.Post("/content/definitions", new { scope = h.Environment.Scope, pageSize = 1, offset = 0 }, author))
        {
            Assert.Equal(HttpStatusCode.OK, definitions.StatusCode); var page = await Body(definitions);
            Assert.Equal("Synthetic purpose for login.heading", page.GetProperty("items")[0].GetProperty("purpose").GetString());
        }
        var created = await Draft(h, author, "Fixture initial heading"); var first = await Proposal(h, author, "validate", created);
        using (var edit = await h.Post("/content/proposals/update", new { idempotencyKey = Guid.NewGuid(), proposalReference = first.GetProperty("proposalReference").GetGuid(),
            expectedRevision = first.GetProperty("revision").GetInt64(), text = "Fixture reviewed heading" }, author))
        { Assert.Equal(HttpStatusCode.OK, edit.StatusCode); created = (await Body(edit)).GetProperty("result").Clone(); }
        using (var preview = await h.Post("/content/proposals/preview", Command(created), author))
        { Assert.Equal(HttpStatusCode.OK, preview.StatusCode); Assert.Equal("Fixture reviewed heading", (await Body(preview)).GetProperty("text").GetString()); }
        using (var diff = await h.Post("/content/proposals/diff", new { proposalReference = created.GetProperty("proposalReference").GetGuid(),
            expectedRevision = created.GetProperty("revision").GetInt64(), comparedVersionReference = first.GetProperty("versionReference").GetGuid() }, author))
        { Assert.Equal(HttpStatusCode.OK, diff.StatusCode); var comparison = await Body(diff); Assert.True(comparison.GetProperty("changed").GetBoolean()); Assert.Equal("Fixture initial heading", comparison.GetProperty("beforeText").GetString()); }
        var validated = await Proposal(h, author, "validate", created);
        using (var forbidden = await h.Post("/content/proposals/approve", WriteCommand(validated), author)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var approved = await Proposal(h, approver, "approve", validated);
        var original = await Publish(h, publisher, approved);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = await Draft(h, author, "Fixture replacement heading"); replacement = await Proposal(h, author, "validate", replacement);
        replacement = await Proposal(h, approver, "approve", replacement); var next = await Publish(h, publisher, replacement, original.GetProperty("publicationReference").GetGuid());
        using (var history = await h.Post("/content/history", new { scope = h.Environment.Scope, key = "login.heading", variantReference = h.Environment.Variant, pageSize = 5, offset = 0 }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal(2, (await Body(history)).GetProperty("items").GetArrayLength()); }
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await h.Post("/content/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = original.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = next.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(h.Clock.GetUtcNow()) }, publisher))
        { Assert.Equal(HttpStatusCode.OK, rollback.StatusCode); Assert.Equal(original.GetProperty("versionReference").GetGuid(), (await Body(rollback)).GetProperty("result").GetProperty("versionReference").GetGuid()); }
        await using var read = h.Environment.Open(); var resolved = read.Application.Resolve(new(h.Environment.Scope, h.Environment.Variant, h.Clock.GetUtcNow()));
        Assert.Equal("Fixture reviewed heading", resolved.Content["login.heading"].Text);
    }

    [Fact]
    public async Task ConcurrentDuplicateDraftUsesOneAtomicReceiptAndChangedBodyConflicts()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        var key = Guid.NewGuid(); var command = new { idempotencyKey = key, scope = h.Environment.Scope, key = "login.heading", variantReference = h.Environment.Variant, text = "Fixture duplicate" };
        var responses = await Task.WhenAll(h.Post("/content/proposals", command, author), h.Post("/content/proposals", command, author));
        try
        {
            Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode)); var bodies = await Task.WhenAll(responses.Select(Body));
            Assert.Single(bodies, x => x.GetProperty("replayed").GetBoolean()); Assert.Single(bodies, x => !x.GetProperty("replayed").GetBoolean());
            Assert.Equal(bodies[0].GetProperty("result").GetProperty("proposalReference").GetGuid(), bodies[1].GetProperty("result").GetProperty("proposalReference").GetGuid());
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using (var changed = await h.Post("/content/proposals", new { idempotencyKey = key, scope = h.Environment.Scope, key = "login.heading", variantReference = h.Environment.Variant, text = "Different intent" }, author))
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        await using var context = h.Environment.Context(); Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.proposals").SingleAsync());
    }

    [Fact]
    public async Task CompletedCommandReplayReauthorizesAndDoesNotTreatGovernanceChangeAsAbsent()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        var command = new { idempotencyKey = Guid.NewGuid(), scope = h.Environment.Scope, key = "login.heading", variantReference = h.Environment.Variant, text = "Fixture applied" };
        using var first = await h.Post("/content/proposals", command, author); Assert.Equal(HttpStatusCode.OK, first.StatusCode); var original = await Body(first);
        h.Owners.Denied = ContentOperation.CreateDraft;
        using (var denied = await h.Post("/content/proposals", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        h.Owners.Denied = null;
        h.Environment.Profile = h.Environment.Profile with { Metadata = new("fixture-governance-v2", "ACTIVE", h.Environment.Scope, h.Environment.Interval, null, ["fixture-v2-approval"]) };
        using var retry = await h.Post("/content/proposals", command, author); Assert.Equal(HttpStatusCode.OK, retry.StatusCode); var replay = await Body(retry);
        Assert.True(replay.GetProperty("replayed").GetBoolean()); Assert.Equal(original.GetProperty("result").GetRawText(), replay.GetProperty("result").GetRawText());
        await using var context = h.Environment.Context(); Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.proposals").SingleAsync());
    }

    [Fact]
    public async Task SameApplicationReplayReturnsOriginalReceiptAndReleasesConnectionLockAfterFailure()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var environment = new PresentationContentTestEnvironment(db, new TestClock()); await environment.InitializeAsync();
        await using var scope = environment.Open(); var app = scope.Application; const string op = "OP-fixture-owner-key", digest = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var first = await app.ExecuteCommandAsync(op, digest, () => app.CreateDraftAsync(environment.Draft("original") with { OperationId = op }));
        var changed = await app.UpdateDraftAsync(new(first.Result.ProposalReference, first.Result.Revision, "changed", "OP-fixture-edit", "fixture-correlation"));
        var executed = false; var replay = await app.ExecuteCommandAsync(op, digest, () => { executed = true; return Task.FromResult(changed); });
        Assert.False(executed); Assert.True(replay.Replayed); Assert.Equal(first.Result, replay.Result); Assert.NotEqual(changed.Revision, replay.Result.Revision);
        await Assert.ThrowsAsync<ContentConflictException>(() => app.ExecuteCommandAsync(op, new string('B', 64), () => Task.FromResult(changed)));
        environment.Authorization.Available = false;
        await Assert.ThrowsAsync<ContentForbiddenException>(() => app.ExecuteCommandAsync("OP-fixture-retryable", digest, () => app.CreateDraftAsync(environment.Draft("retry") with { OperationId = "OP-fixture-retryable" })));
        environment.Authorization.Available = true;
        var success = await app.ExecuteCommandAsync("OP-fixture-retryable", digest, () => app.CreateDraftAsync(environment.Draft("retry") with { OperationId = "OP-fixture-retryable" }));
        Assert.False(success.Replayed);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.ExecuteCommandAsync("OP-fixture-canceled", digest, () => Task.FromResult(changed), canceled.Token));
        await using var connection = environment.Context();
        await connection.Database.OpenConnectionAsync();
        try { Assert.True(await connection.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({"presentation-content-command:" + op},0)) AS \"Value\"").SingleAsync()); }
        finally
        {
            _ = await connection.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock(hashtextextended({"presentation-content-command:" + op},0)) AS \"Value\"").SingleAsync();
            await connection.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task AnonymousAndUserNamedCookieCannotEnterAdminContentWorkspace()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db);
        using (var anonymous = await h.Get("/content/workspace", "")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var owner = await h.Cookie("fixture-author"); var userNamed = owner.Replace("__Host-Tunner.Admin.Session=", "__Host-Tunner.User.Session=", StringComparison.Ordinal);
        using var user = await h.Get("/content/workspace", userNamed); Assert.Equal(HttpStatusCode.Unauthorized, user.StatusCode);
        Assert.Equal(0, h.Owners.Authorizations);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("origin")]
    [InlineData("actor-body")]
    [InlineData("duplicate-body")]
    [InlineData("oversized")]
    public async Task BrowserOrCommandBoundaryFailureCreatesNoContent(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        var body = new Dictionary<string, object> { ["idempotencyKey"] = Guid.NewGuid(), ["scope"] = h.Environment.Scope, ["key"] = "login.heading", ["variantReference"] = h.Environment.Variant, ["text"] = "Fixture draft" };
        if (failure == "actor-body") body["actorReference"] = "fixture-publisher";
        if (failure == "oversized") body["text"] = new string('X', 20000);
        var json = JsonSerializer.Serialize(body);
        if (failure == "duplicate-body") json = json[..^1] + ",\"text\":\"second\"}";
        using var response = await h.PostRaw("/content/proposals", json, author, failure == "csrf" ? "invalid-fixture-csrf" : author.Csrf,
            failure == "origin" ? "https://foreign.example.test" : TestHost.Origin);
        Assert.Equal(failure == "origin" ? HttpStatusCode.Forbidden : failure == "oversized" ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = h.Environment.Context(); Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.proposals").SingleAsync());
    }

    [Theory]
    [InlineData("session")]
    [InlineData("permission")]
    [InlineData("forged-actor")]
    public async Task CurrentWorkforceAndDomainAuthorityRemainIndependentRequiredOwners(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        if (failure == "session") h.Owners.SessionsAvailable = false;
        if (failure == "permission") h.Owners.Denied = ContentOperation.CreateDraft;
        if (failure == "forged-actor") h.Owners.WrongActor = true;
        using var response = await h.Post("/content/proposals", new { idempotencyKey = Guid.NewGuid(), scope = h.Environment.Scope, key = "login.heading", variantReference = h.Environment.Variant, text = "Fixture draft" }, author);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = h.Environment.Context(); Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.proposals").SingleAsync());
    }

    [Theory]
    [InlineData("page")]
    [InlineData("offset")]
    [InlineData("search")]
    [InlineData("scope")]
    public async Task QueriesStayWithinQualifiedScopeAndExplicitCapacity(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await TestHost.Start(db); var author = await h.Browser("fixture-author");
        using var response = await h.Post("/content/proposals/search", new { scope = failure == "scope" ? "fixture-unqualified-scope" : h.Environment.Scope,
            search = failure == "search" ? new string('X', 65) : null, pageSize = failure == "page" ? 6 : 1, offset = failure == "offset" ? 21 : 0 }, author);
        Assert.Equal(failure == "scope" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static object Command(JsonElement proposal) => new { proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() };
    private static object WriteCommand(JsonElement proposal) => new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() };
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static async Task<JsonElement> Draft(TestHost h, Browser actor, string text)
    {
        using var response = await h.Post("/content/proposals", new { idempotencyKey = Guid.NewGuid(), scope = h.Environment.Scope, key = "login.heading", variantReference = h.Environment.Variant, text }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Proposal(TestHost h, Browser actor, string operation, JsonElement proposal)
    {
        using var response = await h.Post("/content/proposals/" + operation, WriteCommand(proposal), actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Publish(TestHost h, Browser actor, JsonElement proposal, Guid? supersedes = null)
    {
        using var response = await h.Post("/content/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(),
            expectedRevision = proposal.GetProperty("revision").GetInt64(), supersedesPublicationReference = supersedes, effectiveInterval = Interval(h.Clock.GetUtcNow()) }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private sealed record Browser(string Cookies, string Csrf);

    private sealed class Owners(PresentationContentTestEnvironment environment) : IAdminContentSessionSource, IAdminContentAuthorizationSource,
        IAdminContentHttpProfileSource, IContentBrowseBudgetSource
    {
        internal readonly ConcurrentDictionary<string, AdminContentSession> Sessions = new();
        internal bool SessionsAvailable = true, WrongActor;
        internal ContentOperation? Denied;
        internal bool ProbesAvailable = true;
        internal AdminContentPresentationBinding? Presentation;
        internal readonly ConcurrentQueue<ContentAuthorizationRequest> Requests = new();
        internal int Authorizations;
        public ValueTask<AdminContentSession?> RequireCurrentAsync(ClaimsPrincipal principal, DateTimeOffset at, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); var id = principal.FindFirstValue("fixture-admin-session");
            return ValueTask.FromResult<AdminContentSession?>(SessionsAvailable && id is not null && Sessions.TryGetValue(id, out var session) &&
                principal.FindFirstValue(ClaimTypes.NameIdentifier) == session.ActorReference ? session : null);
        }
        public ValueTask<ContentAuthorizationDecision> AuthorizeAsync(AdminContentSession session, ContentAuthorizationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref Authorizations);
            Requests.Enqueue(request);
            if (request.IsCapabilityProbe && !ProbesAvailable) throw new ContentUnavailableException();
            return ValueTask.FromResult(new ContentAuthorizationDecision(request.Digest, request.Scope == environment.Scope && request.Operation != Denied,
                WrongActor ? "fixture-forged-actor" : session.ActorReference, session.ActorKind, "fixture-admin-workforce", "fixture-admin-policy", "fixture-admin-approval", environment.Interval));
        }
        public AdminContentHttpProfile RequireEffective(DateTimeOffset at) => new(new("fixture-admin-http", "ACTIVE", AdminContentHttpProfile.Scope, environment.Interval, null, ["SYNTHETIC-ONLY"]), [environment.Scope], 16384) { Presentation = Presentation };
        public ContentBrowseBudget RequireEffective(string scope, DateTimeOffset at) => new(new("fixture-browse", "ACTIVE", scope, environment.Interval, null, ["SYNTHETIC-ONLY"]), 5, 20, 64);
    }
    private sealed class Tickets : ITicketStore
    {
        private readonly ConcurrentDictionary<string, byte[]> tickets = new();
        public Task<string> StoreAsync(AuthenticationTicket ticket) { var id = Guid.NewGuid().ToString("N"); tickets[id] = TicketSerializer.Default.Serialize(ticket); return Task.FromResult(id); }
        public Task RenewAsync(string key, AuthenticationTicket ticket) { tickets[key] = TicketSerializer.Default.Serialize(ticket); return Task.CompletedTask; }
        public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(tickets.TryGetValue(key, out var ticket) ? TicketSerializer.Default.Deserialize(ticket) : null);
        public Task RemoveAsync(string key) { tickets.TryRemove(key, out _); return Task.CompletedTask; }
    }
    private sealed class TestHost : IAsyncDisposable
    {
        internal const string Origin = "https://admin-content.example.test";
        private const string Scheme = "Tunner.Admin.Bff.Session";
        private readonly Microsoft.AspNetCore.Builder.WebApplication application;
        private readonly X509Certificate2 certificate;
        private readonly HttpClient client;
        private readonly Tickets tickets;
        internal PresentationContentTestEnvironment Environment { get; }
        internal TestClock Clock { get; }
        internal Owners Owners { get; }
        private TestHost(Microsoft.AspNetCore.Builder.WebApplication application, X509Certificate2 certificate, HttpClient client, Tickets tickets,
            PresentationContentTestEnvironment environment, TestClock clock, Owners owners)
        { this.application = application; this.certificate = certificate; this.client = client; this.tickets = tickets; Environment = environment; Clock = clock; Owners = owners; }
        internal static async Task<TestHost> Start(IdentityTestDatabase database)
        {
            var clock = new TestClock(); var environment = new PresentationContentTestEnvironment(database, clock); await environment.InitializeAsync();
            var owners = new Owners(environment); var tickets = new Tickets();
            using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var bytes = generated.Export(X509ContentType.Pkcs12); X509Certificate2 certificate;
            try { certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.DefaultKeySet); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            Microsoft.AspNetCore.Builder.WebApplication? app = null;
            try
            {
                app = TunnerAdminBffHost.CreateApplication([], builder =>
                {
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Bff:BrowserOrigin"] = Origin });
                    builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
                    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider(); builder.Services.AddSingleton<ITicketStore>(tickets);
                    builder.Services.AddSingleton<TimeProvider>(clock); builder.Services.AddSingleton<IPresentationContentProfileSource>(environment);
                    builder.Services.AddTunnerPresentationContent(database.ConnectionString);
                    builder.Services.AddSingleton<IAdminContentSessionSource>(owners); builder.Services.AddSingleton<IAdminContentAuthorizationSource>(owners);
                    builder.Services.AddSingleton<IAdminContentHttpProfileSource>(owners); builder.Services.AddSingleton<IContentBrowseBudgetSource>(owners);
                });
                await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                var handler = new HttpClientHandler { UseCookies = false, UseProxy = false, AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented is not null && presented.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256) };
                return new(app, certificate, new HttpClient(handler) { BaseAddress = new Uri(address) }, tickets, environment, clock, owners);
            }
            catch { if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); } certificate.Dispose(); throw; }
        }
        internal async Task<string> Cookie(string actor)
        {
            var id = Guid.NewGuid().ToString("N"); Owners.Sessions[id] = new(actor, id, ContentActorKind.Human, "fixture-owner-session-approval",
                new(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false));
            var options = application.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme);
            var cookieNow = (options.TimeProvider ?? TimeProvider.System).GetUtcNow();
            var properties = new AuthenticationProperties { IssuedUtc = cookieNow.AddMinutes(-1), ExpiresUtc = cookieNow.AddMinutes(10), AllowRefresh = false };
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor), new("fixture-admin-session", id)], Scheme)), properties, Scheme);
            var stored = await tickets.StoreAsync(ticket);
            var reference = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new("Microsoft.AspNetCore.Authentication.Cookies-SessionId", stored)], Scheme)), new AuthenticationProperties(), Scheme);
            return options.Cookie.Name + "=" + options.TicketDataFormat.Protect(reference);
        }
        internal async Task<Browser> Browser(string actor)
        {
            var cookies = await Cookie(actor); using var response = await Get("/content/workspace", cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return new(Merge(cookies, response), (await Body(response)).GetProperty("csrfToken").GetString()!);
        }
        internal Task<HttpResponseMessage> Get(string path, string cookies) => Send(HttpMethod.Get, path, cookies, null, null);
        internal Task<HttpResponseMessage> Post(string path, object body, Browser browser) => PostRaw(path, JsonSerializer.Serialize(body, Json), browser, browser.Csrf, Origin);
        internal Task<HttpResponseMessage> PostRaw(string path, string json, Browser browser, string? csrf, string origin) => Send(HttpMethod.Post, path, browser.Cookies, json, csrf, origin);
        private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string cookies, string? json, string? csrf, string? origin = null)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Host = new Uri(Origin).Authority;
            if (cookies.Length != 0) request.Headers.TryAddWithoutValidation("Cookie", cookies);
            if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
            if (csrf is not null) request.Headers.TryAddWithoutValidation(TunnerBffFoundation.AntiforgeryHeader, csrf);
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await application.StopAsync(); await application.DisposeAsync(); certificate.Dispose(); }
    }
}
