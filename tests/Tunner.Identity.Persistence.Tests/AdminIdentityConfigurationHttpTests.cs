using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using Tunner.Database.Audit;
using Tunner.Database.Outbox;
using Tunner.Platform.Bff;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Actual isolated HTTPS/Admin cookie/CSRF and PostgreSQL owner operations. Synthetic workforce receipts activate no live sign-in.</summary>
public sealed class AdminIdentityConfigurationHttpTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private const string Base = "/identity/configuration";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter<IdentityConfigurationFamily>(), new JsonStringEnumConverter<IdentityConfigurationCapabilityTarget>() } };

    [Theory]
    [InlineData("authentication")]
    [InlineData("assurance")]
    [InlineData("authentication-content")]
    [InlineData("registration")]
    [InlineData("totp")]
    [InlineData("registration-content")]
    [InlineData("passkey")]
    [InlineData("totp-content")]
    [InlineData("passkey-content")]
    [InlineData("step-up-content")]
    public async Task CanonicalSettingsRoutesEnforceCurrentIdentityAdmission(string family)
    {
        var additional = family switch
        {
            "registration" => new[] { new IdentityConfigurationScope(IdentityConfigurationFamily.Registration) },
            "totp" => new[] { new IdentityConfigurationScope(IdentityConfigurationFamily.Totp) },
            "registration-content" => new[] { new IdentityConfigurationScope(IdentityConfigurationFamily.RegistrationContent, RegistrationPolicyVersion: "fixture-registration-route-v1") },
            "passkey" => new[] { new IdentityConfigurationScope(IdentityConfigurationFamily.Passkey) },
            "totp-content" => new[] { new IdentityConfigurationScope(IdentityConfigurationFamily.TotpContent, TotpPolicyVersion: "fixture-totp-route-v1") },
            "passkey-content" => new[] { new IdentityConfigurationScope(IdentityConfigurationFamily.PasskeyContent, PasskeyPolicyVersion: "fixture-passkey-route-v1") },
            "step-up-content" => new[] { new IdentityConfigurationScope(IdentityConfigurationFamily.StepUpContent, AssuranceScope: new("fixture-step-up-route", "fixture-user-context"), AssurancePolicyVersion: "fixture-assurance-route-v1") },
            _ => Array.Empty<IdentityConfigurationScope>()
        };
        await using var database = await fixture.CreateDatabaseAsync(); await using var host = await Host.Start(database, additionalScopes: additional);
        var actor = await host.Browser("fixture-security-author"); var path = "/identity/settings/" + family;
        foreach (var suffix in new[] { "", "/review", "/history" })
        {
            using var response = await host.Screen(path + suffix, actor.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("script-src 'self'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
            Assert.Contains("/admin-assets/", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var anonymous = await host.Screen(path, "")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var foreign = await host.Screen(path, actor.Cookies.Replace("__Host-Tunner.Admin.Session", "__Host-Tunner.User.Session", StringComparison.Ordinal)))
            Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
        host.Owners.Denied = IdentityConfigurationOperation.Browse;
        using var denied = await host.Screen(path, actor.Cookies); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Authentication)]
    [InlineData(IdentityConfigurationFamily.Assurance)]
    [InlineData(IdentityConfigurationFamily.AuthenticationContent)]
    public async Task ThreeTypedFamiliesPublishThroughExistingPortsAndRollbackProspectively(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db);
        var author = await h.Browser("fixture-security-author"); var approver = await h.Browser("fixture-security-approver"); var publisher = await h.Browser("fixture-security-publisher");
        var created = await Draft(h, author, family, "fixture-first");
        using (var preview = await h.Post("/proposals/preview", Query(created), author))
        { Assert.Equal(HttpStatusCode.OK, preview.StatusCode); Assert.Equal(created.GetProperty("candidateDigest").GetString(), (await Body(preview)).GetProperty("proposal").GetProperty("candidateDigest").GetString()); }
        h.Environment.AssertUnavailable(family);
        var validated = await Step(h, author, "validate", created);
        using (var denied = await h.Post("/proposals/approve", Write(validated), author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("Approve", await Actions(h, author, validated)); Assert.Contains("Approve", await Actions(h, approver, validated));
        var approved = await Step(h, approver, "approve", validated);
        Assert.DoesNotContain("Publish", await Actions(h, approver, approved)); Assert.Contains("Publish", await Actions(h, publisher, approved));
        var first = await Publish(h, publisher, approved);
        using (var baseline = await h.Post("/history/preview", new { scope = h.Scope(family), publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        {
            Assert.Equal(HttpStatusCode.OK, baseline.StatusCode); var value = await Body(baseline);
            Assert.Equal(first.GetProperty("candidateDigest").GetString(), value.GetProperty("publication").GetProperty("candidateDigest").GetString());
            Assert.Equal(JsonValueKind.Object, value.GetProperty(family == IdentityConfigurationFamily.Authentication ? "authentication" : family == IdentityConfigurationFamily.Assurance ? "assurance" : "contentBinding").ValueKind);
        }
        AssertEffective(h, family, "fixture-first");
        h.Environment.Clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = await Draft(h, author, family, "fixture-next"); replacement = await Step(h, author, "validate", replacement);
        replacement = await Step(h, approver, "approve", replacement); var next = await Publish(h, publisher, replacement, first.GetProperty("publicationReference").GetGuid());
        AssertEffective(h, family, "fixture-next"); h.Environment.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await h.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(),
            supersedesPublicationReference = next.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()) }, publisher))
        { Assert.Equal(HttpStatusCode.OK, rollback.StatusCode); Assert.Equal(first.GetProperty("versionReference").GetGuid(), (await Body(rollback)).GetProperty("result").GetProperty("versionReference").GetGuid()); }
        AssertEffective(h, family, "fixture-first");
        using var history = await h.Post("/history", new { scope = h.Scope(family), pageSize = 2, offset = 0 }, publisher);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode); var page = await Body(history); Assert.Equal(2, page.GetProperty("items").GetArrayLength()); Assert.Equal(2, page.GetProperty("nextOffset").GetInt32());
    }

    [Fact]
    public async Task ConcurrentDuplicateRetryPreservesOneNativeReceiptAndChangedIntentConflicts()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db); var author = await h.Browser("fixture-security-author");
        var key = Guid.NewGuid(); var command = h.DraftBody(IdentityConfigurationFamily.Authentication, "fixture-idempotent", key);
        var replies = await Task.WhenAll(h.Post("/authentication/draft", command, author), h.Post("/authentication/draft", command, author));
        try
        {
            foreach (var reply in replies) Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            var bodies = await Task.WhenAll(replies.Select(Body)); Assert.Single(bodies, x => x.GetProperty("replayed").GetBoolean());
            Assert.Equal(bodies[0].GetProperty("result").GetRawText(), bodies[1].GetProperty("result").GetRawText());
            var operation = bodies[0].GetProperty("operationId").GetString();
            await using var platform = h.Environment.Platform(); var events = await platform.Set<OutboxMessage>().Where(x => x.OperationId == operation).ToArrayAsync(); Assert.Single(events);
            var audit = await platform.Set<AuditEventRecord>().SingleAsync(x => x.OperationId == operation); Assert.True(audit.VerifyIntegrity());
            Assert.Equal(key.ToString("N"), audit.CorrelationId); using var envelope = JsonDocument.Parse(audit.Envelope);
            Assert.Equal("fixture-security-author", envelope.RootElement.GetProperty("actor").GetProperty("principal_id").GetString());
            Assert.DoesNotContain("PasswordHashIterations", events[0].Payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fixture-processing", events[0].Payload, StringComparison.Ordinal);
        }
        finally { foreach (var reply in replies) reply.Dispose(); }
        using var changed = await h.Post("/authentication/draft", h.DraftBody(IdentityConfigurationFamily.Authentication, "fixture-other", key), author);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task ReplayReauthorizesCurrentIdentityPermissionAndKeepsOriginalReceiptAcrossRevisionAndGovernanceChanges()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db); var author = await h.Browser("fixture-security-author");
        var body = h.DraftBody(IdentityConfigurationFamily.Authentication, "fixture-replay", Guid.NewGuid());
        using var created = await h.Post("/authentication/draft", body, author); Assert.Equal(HttpStatusCode.OK, created.StatusCode); var initial = await Body(created);
        _ = await Step(h, author, "validate", initial.GetProperty("result"));
        h.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await h.Post("/authentication/draft", body, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        h.Owners.Denied = null; h.Owners.GovernanceVersion = "fixture-governance-v2";
        using var retry = await h.Post("/authentication/draft", body, author); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var replay = await Body(retry); Assert.True(replay.GetProperty("replayed").GetBoolean()); Assert.Equal(initial.GetProperty("result").GetRawText(), replay.GetProperty("result").GetRawText());
        await using var context = h.Environment.Context(); Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM identity.runtime_configuration_proposals").SingleAsync());
    }

    [Fact]
    public async Task AnonymousUserCookieAndContentEditorReceiveNoIdentityConfigurationAuthority()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db);
        using (var anonymous = await h.Get("/workspace", "")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var cookie = await h.Cookie("fixture-security-author");
        using (var user = await h.Get("/workspace", cookie.Replace("__Host-Tunner.Admin.Session=", "__Host-Tunner.User.Session=", StringComparison.Ordinal))) Assert.Equal(HttpStatusCode.Unauthorized, user.StatusCode);
        using var editor = await h.Get("/workspace", await h.Cookie("fixture-content-editor")); Assert.Equal(HttpStatusCode.Forbidden, editor.StatusCode);
    }

    [Fact]
    public async Task ConfigChromeUsesExplicitRealPublishedBindingWithoutGrantingContentEditingAuthority()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db, publishChrome: true);
        var author = await h.Browser("fixture-security-author");
        using (var absent = await h.Get("/workspace", author.Cookies)) Assert.Equal(JsonValueKind.Null, (await Body(absent)).GetProperty("presentation").ValueKind);
        h.Owners.Presentation = new("fixture.identity.login", "fixture-login-en", h.Environment.Interval);
        using var response = await h.Get("/workspace", author.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bundle = (await Body(response)).GetProperty("presentation"); Assert.Equal("en", bundle.GetProperty("locale").GetString());
        var heading = bundle.GetProperty("content").GetProperty("admin.configuration.heading"); Assert.Equal("Synthetic configuration heading", heading.GetProperty("text").GetString());
        Assert.True(Guid.TryParse(heading.GetProperty("publicationReference").GetString(), out _));
    }

    [Fact]
    public async Task ExactHistoricalPreviewRechecksHistoryAuthorityAndRejectsWrongScope()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db);
        var author = await h.Browser("fixture-security-author"); var approver = await h.Browser("fixture-security-approver"); var publisher = await h.Browser("fixture-security-publisher");
        var proposal = await Step(h, author, "validate", await Draft(h, author, IdentityConfigurationFamily.Authentication, "fixture-baseline"));
        proposal = await Step(h, approver, "approve", proposal); var publication = await Publish(h, publisher, proposal);
        h.Owners.Denied = IdentityConfigurationOperation.History;
        using (var denied = await h.Post("/history/preview", new { scope = h.Scope(IdentityConfigurationFamily.Authentication), publicationReference = publication.GetProperty("publicationReference").GetGuid() }, publisher))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        h.Owners.Denied = null;
        using var mixed = await h.Post("/history/preview", new { scope = h.Scope(IdentityConfigurationFamily.Assurance), publicationReference = publication.GetProperty("publicationReference").GetGuid() }, publisher);
        Assert.Equal(HttpStatusCode.Conflict, mixed.StatusCode);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("origin")]
    [InlineData("actor")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    [InlineData("missing-family")]
    public async Task BrowserAndStrictTypedBoundaryFailuresDoNotCreateConfiguration(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db); var author = await h.Browser("fixture-security-author");
        var json = JsonSerializer.Serialize(h.DraftBody(IdentityConfigurationFamily.Authentication, "fixture-boundary", Guid.NewGuid()), Json);
        var path = "/authentication/draft";
        if (failure == "actor") json = json[..^1] + ",\"actorReference\":\"fixture-security-publisher\"}";
        if (failure == "duplicate") json = json[..^1] + ",\"idempotencyKey\":\"" + Guid.NewGuid() + "\"}";
        if (failure == "oversized") json = new string('X', 70000);
        if (failure == "missing-family") { path = "/proposals/search"; json = "{\"scope\":{},\"pageSize\":1,\"offset\":0}"; }
        using var response = await h.Raw(path, json, author, failure == "csrf" ? "invalid" : author.Csrf, failure == "origin" ? "https://foreign.example.test" : Host.Origin);
        Assert.Equal(failure == "origin" ? HttpStatusCode.Forbidden : failure == "oversized" ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = h.Environment.Context(); Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM identity.runtime_configuration_proposals").SingleAsync());
    }

    [Theory]
    [InlineData("page")]
    [InlineData("offset")]
    [InlineData("search")]
    [InlineData("scope")]
    public async Task BrowseUsesQualifiedExactScopesAndBounds(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db); var author = await h.Browser("fixture-security-author");
        var scope = failure == "scope" ? new { family = "AuthenticationContent", authenticationPolicyVersion = "unqualified" } as object : h.Scope(IdentityConfigurationFamily.Authentication);
        using var response = await h.Post("/proposals/search", new { scope, pageSize = failure == "page" ? 6 : 1, offset = failure == "offset" ? 21 : 0, search = failure == "search" ? new string('X', 65) : null }, author);
        Assert.Equal(failure == "scope" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("permission")]
    [InlineData("session")]
    [InlineData("actor")]
    [InlineData("probe")]
    public async Task ExactCurrentAuthorityAndProbeQualificationFailClosed(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var h = await Host.Start(db); var author = await h.Browser("fixture-security-author");
        var draft = await Draft(h, author, IdentityConfigurationFamily.Authentication, "fixture-current");
        if (failure == "stale") _ = await Step(h, author, "validate", draft);
        if (failure == "permission") h.Owners.Denied = IdentityConfigurationOperation.Validate;
        if (failure == "session") h.Owners.SessionsAvailable = false;
        if (failure == "actor") h.Owners.WrongActor = true;
        if (failure == "probe") h.Owners.ProbesAvailable = false;
        using var response = failure == "probe" ? await h.Post("/capabilities", new { scope = h.Scope(IdentityConfigurationFamily.Authentication), target = "Proposal",
            proposalReference = draft.GetProperty("proposalReference").GetGuid(), expectedRevision = draft.GetProperty("revision").GetInt64() }, author) : await h.Post("/proposals/validate", Write(draft), author);
        Assert.Equal(failure == "stale" ? HttpStatusCode.Conflict : failure == "probe" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static void AssertEffective(Host h, IdentityConfigurationFamily family, string version)
    {
        if (family == IdentityConfigurationFamily.Authentication) Assert.Equal(version, h.Environment.Source.RequireEffective(h.Environment.Clock.GetUtcNow()).Version);
        else if (family == IdentityConfigurationFamily.Assurance) Assert.Equal(version, h.Environment.Source.RequireEffective(h.Environment.AssuranceScope, h.Environment.Clock.GetUtcNow()).Metadata.VersionId);
        else Assert.Equal(version, h.Environment.Source.RequireQualified(h.Environment.Authentication().Values, h.Environment.Clock.GetUtcNow()).ConfigurationVersionReference);
    }
    private static object Query(JsonElement p) => new { proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() };
    private static object Write(JsonElement p) => new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() };
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static async Task<JsonElement> Draft(Host h, Browser actor, IdentityConfigurationFamily family, string version)
    {
        using var response = await h.Post(Host.DraftPath(family), h.DraftBody(family, version, Guid.NewGuid()), actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement proposal)
    {
        using var response = await h.Post("/proposals/" + operation, Write(proposal), actor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement proposal, Guid? supersedes = null)
    {
        using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = proposal.GetProperty("proposalReference").GetGuid(),
            expectedRevision = proposal.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<string[]> Actions(Host h, Browser actor, JsonElement proposal)
    {
        var scope = proposal.GetProperty("scope").Clone();
        using var response = await h.Post("/capabilities", new { scope, target = "Proposal", proposalReference = proposal.GetProperty("proposalReference").GetGuid(), expectedRevision = proposal.GetProperty("revision").GetInt64() }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray();
    }
    internal sealed record Browser(string Cookies, string Csrf);
    internal sealed class Owners(RuntimeConfigurationTestEnvironment env, IReadOnlyList<IdentityConfigurationScope>? additionalScopes = null) : IAdminIdentityConfigurationSessionSource,
        IAdminIdentityConfigurationAuthorizationSource, IAdminIdentityConfigurationHttpProfileSource, IIdentityConfigurationBrowseBudgetSource, IIdentityConfigurationGovernanceSource
    {
        internal readonly ConcurrentDictionary<string, AdminIdentityConfigurationSession> Sessions = new();
        internal bool SessionsAvailable = true, WrongActor, ProbesAvailable = true;
        internal IdentityConfigurationOperation? Denied;
        internal string GovernanceVersion = "fixture-identity-governance-v1";
        internal AdminIdentityConfigurationPresentationBinding? Presentation;
        internal IdentityConfigurationScope[] Scopes => [new(IdentityConfigurationFamily.Authentication), new(IdentityConfigurationFamily.Assurance, env.AssuranceScope), new(IdentityConfigurationFamily.AuthenticationContent, AuthenticationPolicyVersion: env.Authentication().Values.Version), .. (additionalScopes ?? [])];
        public ValueTask<AdminIdentityConfigurationSession?> RequireCurrentAsync(ClaimsPrincipal principal, DateTimeOffset at, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); var id = principal.FindFirstValue("fixture-identity-admin-session");
            return ValueTask.FromResult<AdminIdentityConfigurationSession?>(SessionsAvailable && id is not null && Sessions.TryGetValue(id, out var session) && principal.FindFirstValue(ClaimTypes.NameIdentifier) == session.ActorReference ? session : null);
        }
        public ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(AdminIdentityConfigurationSession session, IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (request.IsCapabilityProbe && !ProbesAvailable) throw new IdentityConfigurationUnavailableException();
            return ValueTask.FromResult(new IdentityConfigurationAuthorizationDecision(request.Digest, Scopes.Contains(request.Scope) && request.Operation != Denied && session.ActorReference.StartsWith("fixture-security-", StringComparison.Ordinal),
                WrongActor ? "forged" : session.ActorReference, session.ActorKind, "fixture-identity-admin", "fixture-identity-admin-policy", "fixture-identity-admin-evidence", env.Interval));
        }
        public AdminIdentityConfigurationHttpProfile RequireEffective(DateTimeOffset at) => new(new("fixture-config-http", "ACTIVE", AdminIdentityConfigurationHttpProfile.Scope, env.Interval, null, ["SYNTHETIC-ONLY"]), Scopes, 65536) { Presentation = Presentation };
        public IdentityConfigurationBrowseBudget RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt) => new(new("fixture-config-browse", "ACTIVE", scope, env.Interval, null, ["SYNTHETIC-ONLY"]), 5, 20, 64);
        IdentityConfigurationGovernance IIdentityConfigurationGovernanceSource.RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt)
        { var original = env.RequireEffective(scope, effectiveAt); return original with { Metadata = new(GovernanceVersion, "ACTIVE", scope, env.Interval, null, ["SYNTHETIC-ONLY"]) }; }
    }
    private sealed class Tickets : ITicketStore
    {
        private readonly ConcurrentDictionary<string, byte[]> tickets = new();
        public Task<string> StoreAsync(AuthenticationTicket ticket) { var id = Guid.NewGuid().ToString("N"); tickets[id] = TicketSerializer.Default.Serialize(ticket); return Task.FromResult(id); }
        public Task RenewAsync(string key, AuthenticationTicket ticket) { tickets[key] = TicketSerializer.Default.Serialize(ticket); return Task.CompletedTask; }
        public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(tickets.TryGetValue(key, out var ticket) ? TicketSerializer.Default.Deserialize(ticket) : null);
        public Task RemoveAsync(string key) { tickets.TryRemove(key, out _); return Task.CompletedTask; }
    }
    internal sealed class Host : IAsyncDisposable
    {
        internal const string Origin = "https://admin-identity-configuration.example.test";
        private const string Scheme = "Tunner.Admin.Bff.Session";
        private readonly Microsoft.AspNetCore.Builder.WebApplication application;
        private readonly X509Certificate2 certificate;
        private readonly HttpClient client;
        private readonly Tickets tickets;
        internal RuntimeConfigurationTestEnvironment Environment { get; }
        internal Owners Owners { get; }
        private Host(Microsoft.AspNetCore.Builder.WebApplication application, X509Certificate2 certificate, HttpClient client, Tickets tickets, RuntimeConfigurationTestEnvironment environment, Owners owners)
        { this.application = application; this.certificate = certificate; this.client = client; this.tickets = tickets; Environment = environment; Owners = owners; }
        internal static async Task<Host> Start(IdentityTestDatabase database, bool publishChrome = false, IReadOnlyList<IdentityConfigurationScope>? additionalScopes = null)
        {
            var env = new RuntimeConfigurationTestEnvironment(database); var owners = new Owners(env, additionalScopes); var tickets = new Tickets();
            PresentationContentTestEnvironment? chrome = null;
            if (publishChrome)
            {
                chrome = new(database, env.Clock, new Dictionary<string, string> { ["admin.configuration.heading"] = "Synthetic configuration heading" });
                await chrome.InitializeAsync(); _ = await chrome.PublishTextAsync("admin.configuration.heading", "Synthetic configuration heading");
            }
            using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)); var bytes = generated.Export(X509ContentType.Pkcs12); X509Certificate2 certificate;
            try { certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.DefaultKeySet); } finally { CryptographicOperations.ZeroMemory(bytes); }
            Microsoft.AspNetCore.Builder.WebApplication? app = null;
            try
            {
                app = TunnerAdminBffHost.CreateApplication([], builder =>
                {
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Bff:BrowserOrigin"] = Origin });
                    builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
                    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider(); builder.Services.AddSingleton<ITicketStore>(tickets);
                    builder.Services.AddSingleton<TimeProvider>(env.Clock); builder.Services.AddScoped(_ => env.Context()); builder.Services.AddScoped<IdentityRuntimeConfigurationApplication>();
                    builder.Services.AddSingleton<IIdentityConfigurationGovernanceSource>(owners); builder.Services.AddSingleton<IIdentityConfigurationBrowseBudgetSource>(owners);
                    builder.Services.AddSingleton<IAdminIdentityConfigurationSessionSource>(owners); builder.Services.AddSingleton<IAdminIdentityConfigurationAuthorizationSource>(owners);
                    builder.Services.AddSingleton<IAdminIdentityConfigurationHttpProfileSource>(owners);
                    if (chrome is not null)
                    {
                        builder.Services.AddTunnerPresentationContent(database.ConnectionString);
                        builder.Services.AddSingleton<IPresentationContentProfileSource>(chrome); builder.Services.AddSingleton<IPresentationContentAuthorizationSource>(chrome.Authorization);
                    }
                });
                await app.StartAsync(); var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                var handler = new HttpClientHandler { UseCookies = false, UseProxy = false, AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented is not null && presented.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256) };
                return new(app, certificate, new HttpClient(handler) { BaseAddress = new Uri(address) }, tickets, env, owners);
            }
            catch { if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); } certificate.Dispose(); throw; }
        }
        internal object Scope(IdentityConfigurationFamily family) => family switch
        {
            IdentityConfigurationFamily.Authentication => new { family = family.ToString() },
            IdentityConfigurationFamily.Registration or IdentityConfigurationFamily.Totp => new { family = family.ToString() },
            IdentityConfigurationFamily.Assurance => new { family = family.ToString(), assuranceScope = Environment.AssuranceScope },
            _ => new { family = family.ToString(), authenticationPolicyVersion = Environment.Authentication().Values.Version }
        };
        internal static string DraftPath(IdentityConfigurationFamily family) => family switch
        { IdentityConfigurationFamily.Authentication => "/authentication/draft", IdentityConfigurationFamily.Assurance => "/assurance/draft", IdentityConfigurationFamily.Registration => "/registration/draft", IdentityConfigurationFamily.Totp => "/totp/draft", _ => "/content-binding/draft" };
        internal object DraftBody(IdentityConfigurationFamily family, string version, Guid key)
        {
            object candidate = family switch
            { IdentityConfigurationFamily.Authentication => Environment.Authentication(version), IdentityConfigurationFamily.Assurance => Environment.Assurance(version),
                IdentityConfigurationFamily.Registration => IdentityRuntimeAdditionalConfigurationTests.Registration(Environment, version),
                IdentityConfigurationFamily.Totp => IdentityRuntimeAdditionalConfigurationTests.Totp(Environment, version), _ => Environment.Binding(version) };
            return new { idempotencyKey = key, candidate };
        }
        internal async Task<string> Cookie(string actor)
        {
            var id = Guid.NewGuid().ToString("N"); Owners.Sessions[id] = new(actor, id, IdentityConfigurationActorKind.Human, "fixture-workforce-qualification", Environment.Interval);
            var options = application.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme); var now = (options.TimeProvider ?? TimeProvider.System).GetUtcNow();
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor), new("fixture-identity-admin-session", id)], Scheme)),
                new AuthenticationProperties { IssuedUtc = now.AddMinutes(-1), ExpiresUtc = now.AddMinutes(10), AllowRefresh = false }, Scheme);
            var stored = await tickets.StoreAsync(ticket); var reference = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new("Microsoft.AspNetCore.Authentication.Cookies-SessionId", stored)], Scheme)), new AuthenticationProperties(), Scheme);
            return options.Cookie.Name + "=" + options.TicketDataFormat.Protect(reference);
        }
        internal async Task<Browser> Browser(string actor)
        {
            var cookies = await Cookie(actor); using var response = await Get("/workspace", cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var csrfCookie = response.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]);
            return new(cookies + "; " + string.Join("; ", csrfCookie), (await Body(response)).GetProperty("csrfToken").GetString()!);
        }
        internal Task<HttpResponseMessage> Get(string path, string cookies) => Send(HttpMethod.Get, path, cookies, null, null, null);
        internal Task<HttpResponseMessage> Screen(string path, string cookies) => Send(HttpMethod.Get, path, cookies, null, null, null, root: true);
        internal Task<HttpResponseMessage> Post(string path, object body, Browser actor) => Raw(path, JsonSerializer.Serialize(body, Json), actor, actor.Csrf, Origin);
        internal Task<HttpResponseMessage> Raw(string path, string json, Browser actor, string? csrf, string origin) => Send(HttpMethod.Post, path, actor.Cookies, json, csrf, origin);
        private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string cookies, string? json, string? csrf, string? origin, bool root = false)
        {
            using var request = new HttpRequestMessage(method, root ? path : Base + path); request.Headers.Host = new Uri(Origin).Authority;
            if (cookies.Length != 0) request.Headers.TryAddWithoutValidation("Cookie", cookies);
            if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
            if (csrf is not null) request.Headers.TryAddWithoutValidation(TunnerBffFoundation.AntiforgeryHeader, csrf);
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json"); return await client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await application.StopAsync(); await application.DisposeAsync(); certificate.Dispose(); }
    }
}
