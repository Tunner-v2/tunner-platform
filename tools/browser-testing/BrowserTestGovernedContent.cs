using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Tunner.Admin.Bff;
using Tunner.Identity.Persistence;
using Tunner.Platform.Bff;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

// Explicit persistent engineering fixture. Optional synthetic Admin is separate from governed User-only Content; no workforce authentication or production policy.
internal sealed class BrowserTestGovernedContent : IContentTransactionLocalProfileSource, IAuthenticationContentBindingSource,
    IAdminContentSessionSource, IAdminContentAuthorizationSource, IAdminContentHttpProfileSource, IContentBrowseBudgetSource
{
    private const string Scope = "synthetic.local.identity.login", Variant = "synthetic-local-en", Evidence = "SYNTHETIC-LOCAL-TEST-ONLY";
    private static readonly EffectivePolicyInterval Interval = new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, null, false);
    private readonly BrowserTestHostOptions options;
    private readonly IReadOnlyDictionary<string, string> wording;
    private readonly ContentGovernanceProfile profile;
    private string? authenticationVersion;
    private const string Scheme = "Tunner.Admin.Bff.Session", SessionClaim = "synthetic-local-admin-session";
    private const string ChromeScope = "synthetic.local.admin.content";
    private const string Author = "synthetic-local-author", Approver = "synthetic-local-approver", Publisher = "synthetic-local-publisher";
    private readonly int adminPort;
    private readonly ContentGovernanceProfile? chromeProfile;
    private readonly Dictionary<string, AdminContentSession> sessions = new(StringComparer.Ordinal);
    private readonly object sessionGate = new();
    private bool initialized;

    internal BrowserTestGovernedContent(BrowserTestHostOptions options, int? selectedAdminPort = null)
    {
        this.options = options; RequireIsolated();
        using var proposal = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-authentication-content.json")));
        wording = proposal.RootElement.GetProperty("content").EnumerateObject().ToDictionary(x => x.Name,
            x => x.Value.GetString() ?? throw new InvalidOperationException("Synthetic Login wording is incomplete."), StringComparer.Ordinal);
        profile = new(new("synthetic-local-content-v1", "ACTIVE", Scope, Interval, null, [Evidence]), "Synthetic local Login", Evidence,
            "synthetic-local-processing", "synthetic-local-retention", "synthetic-local-audit-retention", "RESTRICTED_SECURITY", 4096, true, true, true,
            wording.Select(x => new ContentDefinition(x.Key, "synthetic-sensitive", true, true, true, [], []) { Purpose = "Synthetic local wording: " + x.Key }).ToArray(),
            [new(Variant, "en", new Dictionary<string, string> { ["page"] = "login" })], [new(Variant, [Variant])],
            Enum.GetValues<ContentOperation>().ToDictionary(operation => operation, operation => new ContentEffectContract(
                "synthetic-local-content-" + operation, "fixture-completed", "fixture/presentation-content", "1.0",
                operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History ? null : "fixture.local.content." + operation,
                operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History ? null : "1")));
        adminPort = selectedAdminPort ?? 0;
        if (selectedAdminPort is not null && (adminPort is < 1024 or > 65535 || new[] { options.Port, options.SmtpPort }.Contains(adminPort)))
            throw new InvalidOperationException("Explicit distinct isolated Admin HTTPS port required.");
        chromeProfile = selectedAdminPort is null ? null : Profile(ChromeScope, Chrome);
    }

    private void RequireIsolated()
    {
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development" || options.Mode != "ISOLATED_LOCAL_BROWSER_TESTING" ||
            options.PersistentDirectory is null || options.Totp is not null || options.Recovery is not null || options.RecoveryCase is not null ||
            options.Origin != "https://localhost:" + options.Port)
            throw new InvalidOperationException("Governed Content requires the explicit persistent isolated browser mode.");
    }

    internal void ConfigureUser(IServiceCollection services, AuthenticationPolicy policy)
    {
        RequireIsolated(); policy.Validate(); authenticationVersion = policy.Version;
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IPresentationContentProfileSource>(this);
        services.AddTunnerPresentationContent(options.ConnectionString);
        services.AddSingleton<IPresentationContentAuthorizationSource>(_ => new FixtureAuthorization(this, false));
        services.AddSingleton<IAuthenticationContentBindingSource>(this);
        services.AddTunnerPublishedAuthenticationPresentation();
    }

    public ContentGovernanceProfile RequireEffective(string scope, DateTimeOffset at) => RequireCurrentLocal(scope, at);
    public ContentGovernanceProfile RequireCurrentLocal(string scope, DateTimeOffset at)
    {
        RequireIsolated();
        if (!Interval.Contains(at)) throw new ContentUnavailableException();
        return scope == Scope ? profile : scope == ChromeScope && chromeProfile is not null ? chromeProfile : throw new ContentUnavailableException();
    }

    public AuthenticationContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset at)
    {
        RequireIsolated();
        if (authenticationVersion is null || policy.Version != authenticationVersion || !Interval.Contains(at)) throw new ContentUnavailableException();
        return new(policy.Version, "synthetic-local-login-binding-v1", Evidence, Scope, Variant, Interval,
            new(options.Origin + "/browser-testing/terms"), new(options.Origin + "/browser-testing/privacy"), new(options.Origin + "/browser-testing/support"));
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        RequireIsolated();
        var databaseOptions = new DbContextOptionsBuilder<PresentationContentDbContext>().UseNpgsql(options.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "presentation_content")).Options;
        await using var database = new PresentationContentDbContext(databaseOptions);
        await database.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        var application = new PresentationContentApplication(database, this, new FixtureAuthorization(this, true), TimeProvider.System);
        await InitializeProfileAsync(database, application, profile, wording, cancellationToken).ConfigureAwait(false);
        if (chromeProfile is { } chrome)
            await InitializeProfileAsync(database, application, chrome, Chrome, cancellationToken).ConfigureAwait(false);
        initialized = true;
    }

    private static async Task InitializeProfileAsync(PresentationContentDbContext database, PresentationContentApplication application,
        ContentGovernanceProfile selected, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        var scope = selected.Metadata.Scope;
        var stored = await database.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM presentation_content.profiles WHERE \"Scope\"={scope} AND \"Version\"={selected.Metadata.VersionId}) AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? lastStart = null;
        foreach (var (key, text) in values)
        {
            if (stored)
            {
                var history = await application.HistoryAsync(scope, key, Variant, "fixture-content-history", "fixture-content-seed", cancellationToken).ConfigureAwait(false);
                if (history.Count != 0)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (!history.Any(entry => entry.EffectiveInterval.Contains(now)))
                    {
                        var pending = history.Where(entry => entry.EffectiveInterval.From > now && entry.EffectiveInterval.From <= now.AddSeconds(5))
                            .Select(entry => (DateTimeOffset?)entry.EffectiveInterval.From).OrderBy(from => from).FirstOrDefault();
                        if (pending is { } activation && (lastStart is null || activation > lastStart)) lastStart = activation;
                    }
                    continue; // Restart preserves history, including committed bootstrap publications still awaiting activation.
                }
            }
            var proposal = await application.CreateDraftAsync(new(scope, key, Variant, text, "fixture-content-draft", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            stored = true;
            proposal = await application.ValidateAsync(new(proposal.ProposalReference, proposal.Revision, "fixture-content-validate", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            proposal = await application.ApproveAsync(new(proposal.ProposalReference, proposal.Revision, "fixture-content-approve", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            var ticks = DateTimeOffset.UtcNow.AddSeconds(5).UtcTicks;
            var from = new DateTimeOffset(ticks + (10 - ticks % 10) % 10, TimeSpan.Zero);
            _ = await application.PublishAsync(new(proposal.ProposalReference, proposal.Revision, new(from, true, null, false), null,
                "fixture-content-publish", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            if (lastStart is null || from > lastStart) lastStart = from;
        }
        if (lastStart is { } start)
        {
            var delay = start - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        _ = await application.ResolveAsync(new(scope, Variant, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private sealed class FixtureAuthorization(BrowserTestGovernedContent owner, bool seed) : IPresentationContentAuthorizationSource
    {
        public ValueTask<ContentAuthorizationDecision> AuthorizeAsync(ContentAuthorizationRequest request, CancellationToken cancellationToken)
        {
            owner.RequireIsolated(); cancellationToken.ThrowIfCancellationRequested();
            var actor = request.Operation switch { ContentOperation.Approve => "synthetic-local-approver", ContentOperation.Publish or ContentOperation.Rollback => "synthetic-local-publisher", _ => "synthetic-local-author" };
            return ValueTask.FromResult(new ContentAuthorizationDecision(request.Digest, seed && (request.Scope == Scope || owner.chromeProfile is not null && request.Scope == ChromeScope), actor,
                ContentActorKind.Human, "synthetic-local-fixture", "synthetic-local-content-authorization-v1", Evidence, Interval));
        }
    }

    private string AdminOrigin => "https://localhost:" + adminPort;
    private void RequireAdmin()
    {
        RequireIsolated();
        if (adminPort == 0 || chromeProfile is null) throw new InvalidOperationException("Synthetic Admin was not explicitly selected.");
    }

    private static ContentGovernanceProfile Profile(string scope, IReadOnlyDictionary<string, string> values) => new(
        new("synthetic-local-content-v1", "ACTIVE", scope, Interval, null, [Evidence]), "Synthetic local " + scope, Evidence,
        "synthetic-local-processing", "synthetic-local-retention", "synthetic-local-audit-retention", "RESTRICTED_SECURITY",
        4096, true, true, true,
        values.Select(x => new ContentDefinition(x.Key, "synthetic-sensitive", true, true, true, [], []) { Purpose = "Synthetic local wording: " + x.Key }).ToArray(),
        [new(Variant, "en", new Dictionary<string, string> { ["page"] = scope == Scope ? "login" : "admin-content" })], [new(Variant, [Variant])],
        Enum.GetValues<ContentOperation>().ToDictionary(operation => operation, operation => new ContentEffectContract(
            "synthetic-local-content-" + operation, "fixture-completed", "fixture/presentation-content", "1.0",
            operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History ? null : "fixture.local.content." + operation,
            operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History ? null : "1")));

    internal WebApplication CreateAdmin(IAuthenticationPolicySource authenticationPolicies)
    {
        RequireAdmin(); if (!initialized) throw new InvalidOperationException("Synthetic publications must be ready first.");
        var app = TunnerAdminBffHost.CreateApplication([], builder =>
        {
            if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Isolated Admin fixture requires Development.");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Bff:BrowserOrigin"] = AdminOrigin });
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, adminPort, endpoint => endpoint.UseHttps(options.Certificate)));
            var protection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.PersistentDirectory!, "admin-keys")));
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            // The existing Admin BFF foundation supplies its own application-name purpose, cookie and CSRF scheme.
            builder.Services.AddSingleton<ITicketStore>(_ => new SyntheticTickets());
            // Named fixture scheme only: preserve every other foundation event/flag and the User cookie owner.
            builder.Services.PostConfigure<CookieAuthenticationOptions>(Scheme, cookie =>
                cookie.Events.OnSigningIn = ValidateSyntheticSignInAsync);
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton<IPresentationContentProfileSource>(this); builder.Services.AddTunnerPresentationContent(options.ConnectionString);
            builder.Services.AddSingleton<IAdminContentSessionSource>(this); builder.Services.AddSingleton<IAdminContentAuthorizationSource>(this);
            builder.Services.AddSingleton<IAdminContentHttpProfileSource>(this); builder.Services.AddSingleton<IContentBrowseBudgetSource>(this);
            builder.Services.AddSingleton<IAuthenticationPolicySource>(authenticationPolicies); builder.Services.AddSingleton<IAuthenticationContentBindingSource>(this);
            builder.Services.AddTunnerAdminAuthenticationContentPreview();
        });
        app.MapGet("/browser-testing/status", (HttpContext http) =>
        { RequireLocalRequest(http); return new { mode = options.Mode, instanceId = options.RunId, ready = initialized, governedContent = true, syntheticAdmin = true }; }).AllowAnonymous();
        var themeLinks = MapFixtureTheme(app);
        app.MapGet("/browser-testing/admin", (HttpContext http, IAntiforgery antiforgery) =>
        {
            RequireLocalRequest(http); NoStore(http);
            // Same CSP as BffWebAssets.Render; the isolated custom HTML must retain frame and self-only protections.
            http.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            var token = antiforgery.GetAndStoreTokens(http).RequestToken ?? throw new InvalidOperationException("Synthetic Admin CSRF unavailable.");
            return Results.Content(BootstrapPage.Replace("CSRF_TOKEN", WebUtility.HtmlEncode(token), StringComparison.Ordinal)
                .Replace("ADMIN_THEME_LINKS", themeLinks, StringComparison.Ordinal), "text/html; charset=utf-8");
        }).AllowAnonymous();
        app.MapGet("/browser-testing/admin/bootstrap.js", (HttpContext http) =>
        { RequireLocalRequest(http); NoStore(http); return Results.Text(BootstrapScript, "text/javascript; charset=utf-8"); }).AllowAnonymous();
        // No GET creates a session. Unsafe requests still pass the unchanged BFF exact-Origin/header-CSRF middleware.
        app.MapPost("/browser-testing/admin/session", async Task<IResult> (HttpContext http) =>
        {
            RequireLocalRequest(http); NoStore(http);
            if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
            var limit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is null || limit.IsReadOnly) return Results.StatusCode(503);
            limit.MaxRequestBodySize = 4096; // Existing synthetic HTTP fixture budget, not a production admission.
            try
            {
                using var body = await JsonDocument.ParseAsync(http.Request.Body, new JsonDocumentOptions { MaxDepth = 4 }, http.RequestAborted).ConfigureAwait(false);
                if (body.RootElement.ValueKind != JsonValueKind.Object) return Results.BadRequest();
                var fields = body.RootElement.EnumerateObject().ToArray();
                if (fields.Length != 1 || fields[0].Name != "actor" || fields[0].Value.ValueKind != JsonValueKind.String) return Results.BadRequest();
                var actor = fields[0].Value.GetString() switch { "author" => Author, "approver" => Approver, "publisher" => Publisher, _ => null };
                if (actor is null) return Results.BadRequest();
                var previous = http.User.FindFirstValue(SessionClaim);
                if (previous is not null)
                {
                    // Complete sign-out in its own request: the cookie handler retains its store key
                    // within a request, so signing in here would renew the old actor's ticket.
                    await http.SignOutAsync(Scheme).ConfigureAwait(false);
                    lock (sessionGate) sessions.Remove(previous);
                    return Results.Json(new { status = "BOOTSTRAP_REQUIRED" }, statusCode: 202);
                }
                var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid().ToString("N"); var until = now.AddMinutes(10);
                lock (sessionGate)
                {
                    foreach (var expired in sessions.Where(x => !x.Value.EffectiveInterval.Contains(now)).Select(x => x.Key).ToArray()) sessions.Remove(expired);
                    if (sessions.Count >= 32) return Results.StatusCode(503);
                    sessions.Add(id, new(actor, id, ContentActorKind.Human, Evidence, new(now, true, until, false)));
                }
                var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor), new(SessionClaim, id)], Scheme));
                try { await http.SignInAsync(Scheme, principal, new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = until, AllowRefresh = false }).ConfigureAwait(false); }
                catch { lock (sessionGate) sessions.Remove(id); throw; }
                return Results.NoContent();
            }
            catch (JsonException) { return Results.BadRequest(); }
            catch (BadHttpRequestException error) { return Results.StatusCode(error.StatusCode == 413 ? 413 : 400); }
        }).AllowAnonymous();
        return app;
    }

    // Fixture-only static transport of the already-built Admin theme. The production /admin-assets guards stay unchanged.
    // Only entry-linked CSS and its exact packaged WOFF2 references are exposed; no script, map, HTML or request-derived path.
    private string MapFixtureTheme(WebApplication app)
    {
        RequireAdmin();
        const string prefix = "/browser-testing/admin/theme/";
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "admin", "assets");
        if ((File.GetAttributes(assetsRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Packaged Admin theme directory is required.");
        var entry = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "admin", "content.html"));
        var styles = System.Text.RegularExpressions.Regex.Matches(entry,
            """<link\b[^>]*\brel=["']stylesheet["'][^>]*\bhref=["']/admin-assets/assets/(?<name>[A-Za-z0-9_-]+\.css)["'][^>]*>""",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToArray();
        if (styles.Length == 0) throw new InvalidOperationException("Packaged Admin entry styles are required.");
        var allowed = new Dictionary<string, (string File, string Type, string? Text)>(StringComparer.Ordinal);
        string Packaged(string name)
        {
            var file = Path.Combine(assetsRoot, name);
            if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Exact packaged Admin theme file is required.");
            return file;
        }
        foreach (var name in styles)
        {
            var file = Packaged(name); var css = File.ReadAllText(file);
            if (System.Text.RegularExpressions.Regex.IsMatch(css, @"@import\b",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new InvalidOperationException("The fixture requires bundled Admin styles.");
            var fonts = System.Text.RegularExpressions.Regex.Matches(css,
                """url\(\s*["']?(?<url>/admin-assets/assets/(?<name>[A-Za-z0-9_-]+\.woff2))["']?\s*\)""",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            foreach (System.Text.RegularExpressions.Match font in fonts)
            {
                var fontName = font.Groups["name"].Value;
                allowed.TryAdd(prefix + fontName, (Packaged(fontName), "font/woff2", null));
                // Preserve the compiled declarations/tokens exactly; relocate only the already-allowlisted font URL.
                css = css.Replace(font.Groups["url"].Value, prefix + fontName, StringComparison.Ordinal);
            }
            allowed.Add(prefix + name, (file, "text/css; charset=utf-8", css));
        }
        foreach (var (route, asset) in allowed)
            app.MapGet(route, (HttpContext http) =>
            {
                RequireLocalRequest(http); NoStore(http);
                return asset.Text is { } css ? (IResult)Results.Text(css, asset.Type) : Results.File(asset.File, asset.Type);
            }).AllowAnonymous();
        return string.Join("", styles.Select(name => "<link rel=\"stylesheet\" href=\"" + WebUtility.HtmlEncode(prefix + name) + "\">"));
    }

    private async Task ValidateSyntheticSignInAsync(CookieSigningInContext context)
    {
        var http = context.HttpContext; RequireLocalRequest(http);
        var origins = http.Request.Headers.Origin;
        if (context.Scheme.Name != Scheme || !HttpMethods.IsPost(http.Request.Method) || http.Request.Path != "/browser-testing/admin/session" ||
            origins.Count != 1 || !StringComparer.Ordinal.Equals(origins[0], AdminOrigin) || !http.Request.HasJsonContentType() ||
            context.Principal is null || context.Principal.Identities.Count(x => x.IsAuthenticated) != 1 ||
            context.Principal.Identities.Any(x => x.IsAuthenticated && x.AuthenticationType != Scheme) ||
            context.Principal.Claims.Count() != 2 || context.Principal.FindAll(ClaimTypes.NameIdentifier).Count() != 1 ||
            context.Principal.FindAll(SessionClaim).Count() != 1)
            throw new InvalidOperationException("Only the fixed isolated Admin bootstrap may issue this cookie.");
        var now = (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow();
        var session = await RequireCurrentAsync(context.Principal, now, http.RequestAborted).ConfigureAwait(false);
        if (session is null || session.ActorReference is not (Author or Approver or Publisher) || session.ActorKind != ContentActorKind.Human ||
            session.QualificationReference != Evidence || session.SessionReference != context.Principal.FindFirstValue(SessionClaim) ||
            context.Properties.IssuedUtc is not { } issued || context.Properties.ExpiresUtc is not { } expires ||
            issued > now || expires <= now || expires <= issued || session.EffectiveInterval.Until is not { } until ||
            // AuthenticationProperties encodes issue/expiry instants at whole-second precision.
            issued != DateTimeOffset.FromUnixTimeSeconds(session.EffectiveInterval.From.ToUnixTimeSeconds()) ||
            expires != DateTimeOffset.FromUnixTimeSeconds(until.ToUnixTimeSeconds()) ||
            context.Properties.IsPersistent || context.Properties.AllowRefresh != false)
            throw new InvalidOperationException("A current exact synthetic server session is required.");
    }

    private void RequireLocalRequest(HttpContext http)
    {
        RequireAdmin();
        if (!http.Request.IsHttps || http.Request.Host.Value != "localhost:" + adminPort || http.Request.QueryString.HasValue ||
            http.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)) throw new BadHttpRequestException("Isolated local fixture only.", 400);
    }
    private static void NoStore(HttpContext http)
    { http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer"; }
    public ValueTask<AdminContentSession?> RequireCurrentAsync(ClaimsPrincipal principal, DateTimeOffset at, CancellationToken cancellationToken)
    {
        RequireAdmin(); cancellationToken.ThrowIfCancellationRequested(); var id = principal.FindFirstValue(SessionClaim);
        lock (sessionGate)
            return ValueTask.FromResult<AdminContentSession?>(id is not null && sessions.TryGetValue(id, out var session) &&
                session.EffectiveInterval.Contains(at) && principal.Identities.Any(x => x.IsAuthenticated) && principal.FindFirstValue(ClaimTypes.NameIdentifier) == session.ActorReference &&
                principal.Identities.Where(x => x.IsAuthenticated).All(x => x.AuthenticationType == Scheme) ? session : null);
    }
    public ValueTask<ContentAuthorizationDecision> AuthorizeAsync(AdminContentSession session, ContentAuthorizationRequest request, CancellationToken cancellationToken)
    {
        RequireAdmin(); cancellationToken.ThrowIfCancellationRequested();
        var common = request.Operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History;
        var allowed = (session.ActorReference is Author or Approver or Publisher) && request.Scope == Scope && (common || (session.ActorReference switch
        {
            Author => request.Operation is ContentOperation.CreateDraft or ContentOperation.UpdateDraft or ContentOperation.Validate,
            Approver => request.Operation == ContentOperation.Approve,
            Publisher => request.Operation is ContentOperation.Publish or ContentOperation.Rollback,
            _ => false
        }));
        return ValueTask.FromResult(Decision(request, session.ActorReference, allowed));
    }
    public AdminContentHttpProfile RequireEffective(DateTimeOffset at)
    {
        RequireAdmin(); if (!Interval.Contains(at)) throw new ContentUnavailableException();
        return new(new("synthetic-local-admin-http-v1", "ACTIVE", AdminContentHttpProfile.Scope, Interval, null, [Evidence]), [Scope], 16384)
        { Presentation = new(ChromeScope, Variant, Interval) };
    }
    ContentBrowseBudget IContentBrowseBudgetSource.RequireEffective(string scope, DateTimeOffset at)
    {
        RequireAdmin(); if (scope != Scope || !Interval.Contains(at)) throw new ContentUnavailableException();
        return new(new("synthetic-local-content-browse-v1", "ACTIVE", scope, Interval, null, [Evidence]), 5, 20, 64);
    }
    private static ContentAuthorizationDecision Decision(ContentAuthorizationRequest request, string actor, bool allowed) =>
        new(request.Digest, allowed, actor, ContentActorKind.Human, "synthetic-local-fixture", "synthetic-local-content-authorization-v1", Evidence, Interval);
    // Native test ticket pattern, scoped to this separate Admin host. Expired tickets are removed; no credentials are accepted.
    private sealed class SyntheticTickets : ITicketStore
    {
        private readonly Dictionary<string, (byte[] Value, DateTimeOffset Until)> tickets = new(StringComparer.Ordinal);
        private readonly object gate = new();
        public Task<string> StoreAsync(AuthenticationTicket ticket)
        {
            lock (gate)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var expired in tickets.Where(x => x.Value.Until <= now).Select(x => x.Key).ToArray()) tickets.Remove(expired);
                if (tickets.Count >= 32 || ticket.Properties.ExpiresUtc is not { } until || until <= now) throw new InvalidOperationException("Synthetic Admin ticket capacity unavailable.");
                var id = Guid.NewGuid().ToString("N"); tickets.Add(id, (TicketSerializer.Default.Serialize(ticket), until)); return Task.FromResult(id);
            }
        }
        public Task RenewAsync(string key, AuthenticationTicket ticket) => throw new InvalidOperationException("Synthetic Admin tickets never slide.");
        public Task<AuthenticationTicket?> RetrieveAsync(string key)
        {
            lock (gate)
            {
                if (tickets.TryGetValue(key, out var entry) && entry.Until > DateTimeOffset.UtcNow) return Task.FromResult<AuthenticationTicket?>(TicketSerializer.Default.Deserialize(entry.Value));
                tickets.Remove(key); return Task.FromResult<AuthenticationTicket?>(null);
            }
        }
        public Task RemoveAsync(string key) { lock (gate) tickets.Remove(key); return Task.CompletedTask; }
    }

    // Diagnostic fixture controls only, outside the production Admin screens. No URL credential, arbitrary claim or return target.
    // Approved Admin ThemeProvider attributes, typography/card/actions and native compiled PrimaryButton classes.
    // This remains the same isolated actor control; no new application workflow, custom stylesheet or auth action.
    private const string BootstrapPage = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Isolated Admin fixture</title><meta name="tunner-csrf" content="CSRF_TOKEN">ADMIN_THEME_LINKS<script src="/browser-testing/admin/bootstrap.js" defer></script></head>
<body><div class="tn-theme" data-tunner-surface="admin" data-tunner-theme="light"><div class="admin-content">
<a class="content-skip" href="#fixture-main">Skip to content</a><header class="content-header"><div class="content-brand"><span class="tn-brand">Tunner</span></div></header>
<main id="fixture-main" class="content-main"><section class="content-panel" aria-labelledby="fixture-title"><div class="content-panel-heading"><h1 id="fixture-title">Isolated Admin fixture</h1></div>
<p>Synthetic local test data only. This is not workforce authentication or a production approval.</p><p>Select a fixed synthetic actor. Author edits and validates; approver approves; publisher publishes. The Content owner enforces separate actors.</p>
<div class="content-editor-footer"><div class="content-actions"><button type="button" class="tn-primary-button" data-presentation="standard" data-appearance="filled" data-actor="author">Use synthetic author</button> <button type="button" class="tn-primary-button" data-presentation="standard" data-appearance="filled" data-actor="approver">Use synthetic approver</button> <button type="button" class="tn-primary-button" data-presentation="standard" data-appearance="filled" data-actor="publisher">Use synthetic publisher</button></div></div>
<p id="message" role="status" aria-live="polite"></p></section></main></div></div></body></html>
""";
    private const string BootstrapScript = """
'use strict';
for (const button of document.querySelectorAll('[data-actor]')) button.addEventListener('click', async () => {
  const buttons = [...document.querySelectorAll('[data-actor]')]; buttons.forEach(item => item.disabled = true);
  try {
    let csrf = document.querySelector('meta[name="tunner-csrf"]').content;
    const signIn = () => fetch('/browser-testing/admin/session', {method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json','X-Tunner-CSRF':csrf},body:JSON.stringify({actor:button.dataset.actor})});
    let response = await signIn();
    if (response.status === 202) {
      const bootstrap = await fetch('/browser-testing/admin', {credentials:'same-origin',cache:'no-store'});
      if (!bootstrap.ok) throw new Error('unavailable');
      csrf = new DOMParser().parseFromString(await bootstrap.text(), 'text/html').querySelector('meta[name="tunner-csrf"]')?.content;
      if (!csrf) throw new Error('unavailable');
      response = await signIn();
    }
    if (response.status !== 204) throw new Error('unavailable');
    window.location.assign('/admin/content');
  } catch { document.getElementById('message').textContent = 'The isolated session could not start. Reload this page and try again.'; buttons.forEach(item => item.disabled = false); }
});
""";
    // Exact existing synthetic Admin Content fixture labels; real publication receipts are produced during isolated seeding.
    private static readonly IReadOnlyDictionary<string, string> Chrome = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["admin.content.brand"] = "Tunner",
        ["admin.content.operations"] = "Operations workspace",
        ["admin.content.title"] = "Content management",
        ["admin.content.subtitle"] = "Review and publish approved wording for each context.",
        ["admin.content.content"] = "Content",
        ["admin.content.workspace"] = "Content workspace",
        ["admin.content.review"] = "Review & publish",
        ["admin.content.history"] = "Version history",
        ["admin.content.menu"] = "Menu",
        ["admin.content.skip"] = "Skip to content",
        ["admin.content.boundary"] = "Admin workspace · User sign-in remains separate",
        ["admin.content.scope"] = "Surface",
        ["admin.content.selectScope"] = "Choose a surface",
        ["admin.content.selectVariant"] = "Choose a context",
        ["admin.content.search"] = "Search",
        ["admin.content.searchHelp"] = "Find a purpose or semantic key.",
        ["admin.content.noMatches"] = "No matching definitions.",
        ["admin.content.noSelection"] = "Choose a content definition to begin.",
        ["admin.content.wording"] = "Wording",
        ["admin.content.reason"] = "Change reason",
        ["admin.content.wordingHelp"] = "Keep required variables intact. Changes remain drafts until published.",
        ["admin.content.reasonHelp"] = "Explain why this wording needs to change.",
        ["admin.content.save"] = "Save draft",
        ["admin.content.compare"] = "Preview & compare",
        ["admin.content.validate"] = "Validate",
        ["admin.content.approve"] = "Approve",
        ["admin.content.publish"] = "Publish",
        ["admin.content.restore"] = "Restore this version",
        ["admin.content.cancel"] = "Cancel",
        ["admin.content.confirm"] = "Confirm",
        ["admin.content.loading"] = "Loading…",
        ["admin.content.unavailable"] = "This content is unavailable. Try again.",
        ["admin.content.noAccess"] = "This action is unavailable for the current Admin session.",
        ["admin.content.conflict"] = "This proposal has changed. Review the current version before continuing.",
        ["admin.content.unknown"] = "The response was interrupted. Check the result by retrying the same request.",
        ["admin.content.retry"] = "Retry same request",
        ["admin.content.refresh"] = "Refresh",
        ["admin.content.empty"] = "No published version.",
        ["admin.content.current"] = "Published wording",
        ["admin.content.candidate"] = "Candidate",
        ["admin.content.approval"] = "Approval",
        ["admin.content.pending"] = "Pending",
        ["admin.content.preview"] = "User context preview",
        ["admin.content.previewUnavailable"] = "The approved User preview is not available in this workspace.",
        ["admin.content.historyHelp"] = "Published wording is immutable. Restore creates a new prospective publication.",
        ["admin.content.version"] = "Version",
        ["admin.content.publication"] = "Publication reference",
        ["admin.content.effective"] = "Effective (UTC)",
        ["admin.content.needsApproval"] = "Approval required",
        ["admin.content.sod"] = "Configured separation of duties applies. Each action is checked by the server.",
        ["admin.content.scheduledFrom"] = "Effective from",
        ["admin.content.scheduledUntil"] = "Effective until",
        ["admin.content.validationSummary"] = "Check the highlighted fields.",
        ["admin.content.required"] = "Enter a value.",
        ["admin.content.maxLength"] = "The value exceeds the allowed length.",
        ["admin.content.invalidTime"] = "Choose an end after the start.",
        ["admin.content.prospectiveOnly"] = "Choose a future effective time. Published history is preserved.",
        ["admin.content.saved"] = "Draft saved.",
        ["admin.content.replayed"] = "The earlier request was already applied. Current state has been reloaded.",
        ["admin.content.success"] = "The request was applied. Current state has been reloaded.",
        ["admin.content.needsContext"] = "Choose a definition, context and saved candidate in the workspace first.",
        ["admin.content.more"] = "Next page",
        ["admin.content.definition"] = "Content library",
        ["admin.content.semanticKey"] = "Semantic key",
        ["admin.content.slots"] = "Variables",
        ["admin.content.context"] = "Context",
        ["admin.content.edit"] = "Edit",
        ["admin.content.startBoundary"] = "Start boundary",
        ["admin.content.endBoundary"] = "End boundary",
        ["admin.content.inclusive"] = "Inclusive",
        ["admin.content.exclusive"] = "Exclusive",
        ["admin.content.choose"] = "Choose",
        ["admin.content.endMode"] = "End condition",
        ["admin.content.endNone"] = "No scheduled end",
        ["admin.content.endAt"] = "At a specified time",
        ["admin.content.utc"] = "Enter UTC time.",
        ["admin.content.changed"] = "Changed",
        ["admin.content.unchanged"] = "Unchanged",
        ["admin.content.newDraft"] = "New draft",
        ["admin.content.selectCandidate"] = "Choose a saved candidate",
        ["admin.content.draftState"] = "Draft",
        ["admin.content.validatedState"] = "Validated",
        ["admin.content.approvedState"] = "Approved",
        ["admin.content.publishedState"] = "Published",
        ["admin.content.baseline"] = "Compare with published version",
        ["admin.content.unsaved"] = "Unsaved wording",
        ["admin.content.useReviewedVersion"] = "Continue with reviewed version",
    };
}
