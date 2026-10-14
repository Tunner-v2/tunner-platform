using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tunner.Admin.Bff;
using Tunner.Platform.Bff;
using Tunner.User.Bff;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class BffFoundationTests(BffFoundationFixture fixture) : IClassFixture<BffFoundationFixture>
{
    [Theory]
    [InlineData(BffSurface.User)]
    [InlineData(BffSurface.Admin)]
    public void ShippedHostsHaveOnlyHealthAndSeparateFrameworkBoundaries(BffSurface surface)
    {
        using var app = BffTestHost.Create(surface, _ => { });
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Single(routes);
        Assert.Equal("/health", routes[0].RoutePattern.RawText);
        Assert.NotNull(routes[0].Metadata.GetMetadata<IAllowAnonymous>());
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(BffTestHost.Scheme(surface));
        Assert.Equal($"__Host-Tunner.{surface}.Session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
        Assert.False(options.SlidingExpiration);
        Assert.Equal(TimeSpan.Zero, options.ExpireTimeSpan);
        Assert.IsType<UnavailableBffTicketStore>(options.SessionStore);
        Assert.Equal($"Tunner.{surface}.Bff", app.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        var references = (surface == BffSurface.User ? typeof(TunnerUserBffHost).Assembly : typeof(TunnerAdminBffHost).Assembly).GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name == "Tunner.Database" || reference.Name == "Tunner.Governance");
        Assert.Contains(references, reference => reference.Name == "Tunner.Platform");
        Assert.Contains(references, reference => reference.Name == "Tunner.Observability");
    }

    [Theory]
    [InlineData(BffSurface.User)]
    [InlineData(BffSurface.Admin)]
    public async Task HttpsHealthWorksWithoutActivatingAnyIdentity(BffSurface surface)
    {
        var host = await fixture.GetHost(surface);
        using var response = await host.Send(HttpMethod.Get, "/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(BffSurface.User)]
    [InlineData(BffSurface.Admin)]
    public async Task HttpAndSpoofedForwardedHttpsAreRejected(BffSurface surface)
    {
        var host = await fixture.GetHost(surface);
        using var request = host.Request(HttpMethod.Get, "/health", http: true);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("Forwarded", "proto=https;host=trusted.example.test");
        using var response = await host.Client.SendAsync(request);
        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(BffSurface.User)]
    [InlineData(BffSurface.Admin)]
    public async Task FallbackAuthorizationReturnsSafe401WithoutLoginRedirect(BffSurface surface)
    {
        var host = await fixture.GetHost(surface);
        using var request = host.Request(HttpMethod.Get, "/_fixture/read?returnUrl=https://outside.example.test/");
        request.Headers.Authorization = new("Bearer", "dummy");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "submitted-value");
        using var response = await host.Client.SendAsync(request);
        var body = await AssertProblem(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        Assert.DoesNotContain("outside.example.test", body, StringComparison.Ordinal);
        Assert.DoesNotContain("submitted-value", body, StringComparison.Ordinal);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(BffSurface.User)]
    [InlineData(BffSurface.Admin)]
    public async Task FrameworkCookiesKeepPrincipalAndTokensOnlyOnServer(BffSurface surface)
    {
        var host = await fixture.GetHost(surface);
        var session = await host.SignIn();
        Assert.Contains("secure", session.WireCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", session.WireCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session.WireCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", session.WireCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", session.WireCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", session.WireCookie, StringComparison.OrdinalIgnoreCase);
        var clientTicket = host.CookieOptions.TicketDataFormat.Unprotect(session.Cookie[(session.Cookie.IndexOf('=') + 1)..]);
        Assert.NotNull(clientTicket);
        Assert.Single(clientTicket.Principal.Claims);
        Assert.DoesNotContain(clientTicket.Principal.Claims, claim => claim.Type == ClaimTypes.NameIdentifier);
        Assert.Empty(clientTicket.Properties.GetTokens());
        var serverKey = clientTicket.Principal.Claims.Single().Value;
        var serverTicket = await host.Store.RetrieveAsync(serverKey);
        Assert.NotNull(serverTicket);
        Assert.Equal("a", serverTicket.Properties.GetTokenValue("access_token"));
        Assert.Equal("b", serverTicket.Properties.GetTokenValue("refresh_token"));
        using var response = await host.Send(HttpMethod.Get, "/_fixture/read", session.Cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(BffSurface.User)]
    [InlineData(BffSurface.Admin)]
    public async Task RevokedSessionIsDeniedOnTheVeryNextRequest(BffSurface surface)
    {
        var host = await fixture.GetHost(surface);
        var cookie = await host.ServerCookie();
        var before = host.Store.ReadCount;
        using var first = await host.Send(HttpMethod.Get, "/_fixture/read", cookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var key = host.CookieOptions.TicketDataFormat.Unprotect(cookie[(cookie.IndexOf('=') + 1)..])!.Principal.Claims.Single().Value;
        await host.Store.RemoveAsync(key);
        using var second = await host.Send(HttpMethod.Get, "/_fixture/read", cookie);
        await AssertProblem(second, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        Assert.True(host.Store.ReadCount >= before + 2);
    }

    [Theory]
    [InlineData(BffSurface.User, "expired")]
    [InlineData(BffSurface.Admin, "expired")]
    [InlineData(BffSurface.User, "missing-expiry")]
    [InlineData(BffSurface.Admin, "missing-expiry")]
    [InlineData(BffSurface.User, "missing-issued")]
    [InlineData(BffSurface.Admin, "missing-issued")]
    [InlineData(BffSurface.User, "future-issued")]
    [InlineData(BffSurface.Admin, "future-issued")]
    [InlineData(BffSurface.User, "foreign-identity")]
    [InlineData(BffSurface.Admin, "foreign-identity")]
    public async Task InvalidServerTicketMetadataNeverAuthenticates(BffSurface surface, string kind)
    {
        var host = await fixture.GetHost(surface);
        var cookie = await host.ServerCookie(kind);
        using var response = await host.Send(HttpMethod.Get, "/_fixture/read", cookie);
        await AssertProblem(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    [Theory]
    [InlineData(BffSurface.User)]
    [InlineData(BffSurface.Admin)]
    public async Task InvalidClientCookieDoesNotLeakOrActivateBearerAuthentication(BffSurface surface)
    {
        var host = await fixture.GetHost(surface);
        using var response = await host.Send(HttpMethod.Get, "/_fixture/read", $"__Host-Tunner.{surface}.Session=not-a-ticket");
        var body = await AssertProblem(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        Assert.DoesNotContain("not-a-ticket", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserCookieCannotBeRenamedIntoAnAdminSession()
    {
        var user = await fixture.GetHost(BffSurface.User);
        var admin = await fixture.GetHost(BffSurface.Admin);
        var cookie = await user.ServerCookie();
        using var response = await admin.Send(HttpMethod.Get, "/_fixture/read", cookie.Replace("__Host-Tunner.User.Session", "__Host-Tunner.Admin.Session", StringComparison.Ordinal));
        await AssertProblem(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task EveryUnsafeVerbRequiresAValidBoundCsrfToken(string verb)
    {
        foreach (var surface in new[] { BffSurface.User, BffSurface.Admin })
        {
            var host = await fixture.GetHost(surface);
            var session = await host.SignIn();
            using var missing = await host.Send(new(verb), "/_fixture/write", session.Cookie, origin: host.Origin);
            await AssertProblem(missing, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            var csrf = await host.Csrf(session.Cookie);
            using var invalid = await host.Send(new(verb), "/_fixture/write", session.Cookie + "; " + csrf.Cookie, host.Origin, "bad");
            await AssertProblem(invalid, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            using var valid = await host.Send(new(verb), "/_fixture/write", session.Cookie + "; " + csrf.Cookie, host.Origin, csrf.Token);
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        }
    }

    [Fact]
    public async Task AnonymousEndpointsDoNotBypassCsrfAndForeignSurfaceTokensFail()
    {
        var user = await fixture.GetHost(BffSurface.User);
        var admin = await fixture.GetHost(BffSurface.Admin);
        using var noToken = await user.Send(HttpMethod.Post, "/_fixture/anonymous", origin: user.Origin);
        await AssertProblem(noToken, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var csrf = await user.Csrf();
        using var valid = await user.Send(HttpMethod.Post, "/_fixture/anonymous", csrf.Cookie, user.Origin, csrf.Token);
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        Assert.Contains("httponly", csrf.WireCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", csrf.WireCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", csrf.WireCookie, StringComparison.OrdinalIgnoreCase);
        using var foreign = await admin.Send(HttpMethod.Post, "/_fixture/anonymous", csrf.Cookie.Replace("Tunner.User.Csrf", "Tunner.Admin.Csrf", StringComparison.Ordinal), admin.Origin, csrf.Token);
        await AssertProblem(foreign, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("https://outside.example.test")]
    [InlineData("https://user-bff.example.test.outside.example.test")]
    public async Task UnsafeRequestsRejectMissingOpaqueAndForeignOrigins(string? origin)
    {
        var host = await fixture.GetHost(BffSurface.User);
        var csrf = await host.Csrf();
        using var response = await host.Send(HttpMethod.Post, "/_fixture/anonymous", csrf.Cookie, origin, csrf.Token);
        await AssertProblem(response, HttpStatusCode.Forbidden, "FORBIDDEN_SCOPE");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task WrongRequestHostIsRejectedEvenWithValidOriginAndCsrf()
    {
        var host = await fixture.GetHost(BffSurface.User);
        var csrf = await host.Csrf();
        using var request = host.Request(HttpMethod.Post, "/_fixture/anonymous", cookie: csrf.Cookie, origin: host.Origin, csrf: csrf.Token);
        request.Headers.Host = "outside.example.test";
        using var response = await host.Client.SendAsync(request);
        await AssertProblem(response, HttpStatusCode.Forbidden, "FORBIDDEN_SCOPE");
    }

    [Fact]
    public async Task MissingOriginConfigurationLeavesOnlyHttpsHealthUsable()
    {
        await using var host = await BffTestHost.Start(BffSurface.User, fixture.Certificate, configureOrigin: false);
        using var health = await host.Send(HttpMethod.Get, "/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var browser = await host.Send(HttpMethod.Get, "/_fixture/read");
        await AssertProblem(browser, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE");
    }

    [Fact]
    public async Task DefaultProductionStoreAndSignInStayDenied()
    {
        await using var host = await BffTestHost.Start(BffSurface.User, fixture.Certificate, testIdentity: false);
        var protectedCookie = await host.ServerCookie();
        using var read = await host.Send(HttpMethod.Get, "/_fixture/read", protectedCookie);
        await AssertProblem(read, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        var csrf = await host.Csrf();
        using var signIn = await host.Send(HttpMethod.Post, "/_fixture/sign-in", csrf.Cookie, host.Origin, csrf.Token);
        var body = await AssertProblem(signIn, HttpStatusCode.InternalServerError, "DEPENDENCY_UNAVAILABLE");
        Assert.DoesNotContain("Identity-owned", body, StringComparison.Ordinal);
        Assert.False(signIn.Headers.Contains("Set-Cookie"));
        var unavailable = new UnavailableBffTicketStore();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.StoreAsync(BffTestHost.Ticket(BffSurface.User)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.RenewAsync("fixture", BffTestHost.Ticket(BffSurface.User)));
        await unavailable.RemoveAsync("fixture"); await unavailable.RemoveAsync("fixture");
    }

    [Fact]
    public async Task ForbidAndExceptionsReturnSafeProblemsWithoutDetailsOrRedirect()
    {
        var host = await fixture.GetHost(BffSurface.Admin);
        var cookie = await host.ServerCookie();
        using var forbidden = await host.Send(HttpMethod.Get, "/_fixture/forbid", cookie);
        await AssertProblem(forbidden, HttpStatusCode.Forbidden, "FORBIDDEN_SCOPE");
        Assert.Null(forbidden.Headers.Location);
        using var fault = await host.Send(HttpMethod.Get, "/_fixture/fault");
        var body = await AssertProblem(fault, HttpStatusCode.InternalServerError, "DEPENDENCY_UNAVAILABLE");
        Assert.DoesNotContain("fixture-detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://user-bff.example.test")]
    [InlineData("https://user:pass@user-bff.example.test")]
    [InlineData("https://user-bff.example.test/path")]
    [InlineData("https://user-bff.example.test/?q=x")]
    [InlineData("https://user-bff.example.test/#x")]
    [InlineData(" https://user-bff.example.test")]
    public void InvalidConfiguredOriginFailsWithoutEchoingInput(string origin)
    {
        var error = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddTunnerBffFoundation(BffSurface.User, origin));
        Assert.DoesNotContain(origin, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateSurfaceRegistrationAndWeakenedCookieConfigurationFail()
    {
        var services = new ServiceCollection(); services.AddTunnerBffFoundation(BffSurface.User);
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerBffFoundation(BffSurface.Admin));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddTunnerBffFoundation((BffSurface)99));
        using var app = BffTestHost.Create(BffSurface.User, builder => builder.Services.PostConfigure<CookieAuthenticationOptions>(BffTestHost.Scheme(BffSurface.User), options => options.Cookie.HttpOnly = false));
        Assert.Throws<OptionsValidationException>(() => app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(BffTestHost.Scheme(BffSurface.User)));
        using var csrfApp = BffTestHost.Create(BffSurface.User, builder => builder.Services.PostConfigure<AntiforgeryOptions>(options => options.Cookie.SecurePolicy = CookieSecurePolicy.None));
        Assert.Throws<OptionsValidationException>(() => csrfApp.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value);
    }

    [Fact]
    public async Task StoreFailureNeverFallsBackToClientIdentityAndDoesNotLeakDetails()
    {
        var host = await fixture.GetHost(BffSurface.User);
        var cookie = await host.ServerCookie();
        host.Store.FailNextRead();
        using var response = await host.Send(HttpMethod.Get, "/_fixture/read", cookie);
        var body = await AssertProblem(response, HttpStatusCode.InternalServerError, "DEPENDENCY_UNAVAILABLE");
        Assert.DoesNotContain("fixture-store-detail", body, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task NearlyExpiredTicketDoesNotAutomaticallyRenew()
    {
        var host = await fixture.GetHost(BffSurface.User);
        var cookie = await host.ServerCookie("renewal");
        using var response = await host.Send(HttpMethod.Get, "/_fixture/read", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("Origin")]
    [InlineData("X-Tunner-CSRF")]
    public async Task DuplicateSecurityHeadersAreRejected(string header)
    {
        var host = await fixture.GetHost(BffSurface.User);
        var csrf = await host.Csrf();
        using var request = host.Request(HttpMethod.Post, "/_fixture/anonymous", csrf.Cookie, host.Origin, csrf.Token);
        request.Headers.TryAddWithoutValidation(header, header == "Origin" ? host.Origin : csrf.Token);
        using var response = await host.Client.SendAsync(request);
        await AssertProblem(response, header == "Origin" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, header == "Origin" ? "FORBIDDEN_SCOPE" : "VALIDATION_ERROR");
    }

    private static async Task<string> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        Assert.Equal((int)status, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, document.RootElement.GetProperty("error_code").GetString());
        Assert.True(Guid.TryParseExact(document.RootElement.GetProperty("correlation_id").GetString(), "N", out _));
        return text;
    }
}

public sealed class BffFoundationFixture : IAsyncLifetime
{
    private readonly Dictionary<BffSurface, BffTestHost> hosts = [];
    public X509Certificate2 Certificate { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Schannel needs a non-ephemeral key load. DefaultKeySet (without PersistKeySet) cleans its temporary key on Dispose.
        var pkcs12 = generated.Export(X509ContentType.Pkcs12);
        try { Certificate = X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.DefaultKeySet); }
        finally { CryptographicOperations.ZeroMemory(pkcs12); }
        foreach (var surface in new[] { BffSurface.User, BffSurface.Admin }) hosts[surface] = await BffTestHost.Start(surface, Certificate);
    }
    internal Task<BffTestHost> GetHost(BffSurface surface) => Task.FromResult(hosts[surface]);
    public async Task DisposeAsync() { foreach (var host in hosts.Values) await host.DisposeAsync(); Certificate.Dispose(); }
}

internal sealed class BffTestHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly BffSurface surface;
    private readonly Uri httpAddress;
    internal HttpClient Client { get; }
    internal string Origin => $"https://{surface.ToString().ToLowerInvariant()}-bff.example.test";
    internal TestBffTicketStore Store { get; }
    internal CookieAuthenticationOptions CookieOptions => application.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme(surface));
    internal static string Scheme(BffSurface value) => $"Tunner.{value}.Bff.Session";
    private BffTestHost(WebApplication app, BffSurface value, HttpClient client, Uri http, TestBffTicketStore store) { application=app;surface=value;Client=client;httpAddress=http;Store=store; }
    internal static WebApplication Create(BffSurface surface, Action<WebApplicationBuilder> configure) => surface == BffSurface.User ? TunnerUserBffHost.CreateApplication([], configure) : TunnerAdminBffHost.CreateApplication([], configure);

    internal static async Task<BffTestHost> Start(BffSurface surface, X509Certificate2 certificate, bool configureOrigin = true, bool testIdentity = true)
    {
        var store = new TestBffTicketStore();
        var app = Create(surface, builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["Bff:BrowserOrigin"] = configureOrigin ? $"https://{surface.ToString().ToLowerInvariant()}-bff.example.test" : null });
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.WebHost.ConfigureKestrel(server => { server.Listen(IPAddress.Loopback,0); server.Listen(IPAddress.Loopback,0,listen => listen.UseHttps(certificate)); });
            builder.Services.AddAuthorization(options => options.AddPolicy("fixture.denied", policy => policy.RequireAssertion(_ => false)));
            if (testIdentity)
            {
                builder.Services.AddSingleton<ITicketStore>(store);
                // Only this isolated test host activates a synthetic identity. No production sign-in endpoint/event is shipped.
                builder.Services.PostConfigure<CookieAuthenticationOptions>(Scheme(surface), options => options.Events.OnSigningIn = _ => Task.CompletedTask);
            }
        });
        app.MapGet("/_fixture/csrf", (HttpContext context, IAntiforgery antiforgery) => Results.Json(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken })).AllowAnonymous();
        app.MapPost("/_fixture/sign-in", async (HttpContext context) =>
        {
            var ticket = Ticket(surface);
            await context.SignInAsync(Scheme(surface),ticket.Principal,ticket.Properties);
            return Results.NoContent();
        }).AllowAnonymous();
        app.MapGet("/_fixture/read", () => Results.Text("fixture-read")); // Deliberately relies on fallback authorization.
        app.MapMethods("/_fixture/write", ["POST","PUT","PATCH","DELETE"], () => Results.Text("fixture-write")).RequireAuthorization();
        app.MapPost("/_fixture/anonymous", () => Results.NoContent()).AllowAnonymous();
        app.MapGet("/_fixture/forbid", () => Results.NoContent()).RequireAuthorization("fixture.denied");
        app.MapGet("/_fixture/fault", (Func<IResult>)(() => throw new InvalidOperationException("fixture-detail"))).AllowAnonymous();
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var handler = new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented is not null && presented.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256) };
        var client = new HttpClient(handler) { BaseAddress=new Uri(addresses.Single(address=>address.StartsWith("https://",StringComparison.Ordinal))) };
        return new(app,surface,client,new Uri(addresses.Single(address=>address.StartsWith("http://",StringComparison.Ordinal))),store);
    }

    internal HttpRequestMessage Request(HttpMethod method, string path, string? cookie=null, string? origin=null, string? csrf=null, bool http=false)
    {
        var request = new HttpRequestMessage(method,new Uri(http ? httpAddress : Client.BaseAddress!,path));
        request.Headers.Host = new Uri(Origin).Authority;
        if(cookie is not null) request.Headers.TryAddWithoutValidation("Cookie",cookie);
        if(origin is not null) request.Headers.TryAddWithoutValidation("Origin",origin);
        if(csrf is not null) request.Headers.TryAddWithoutValidation(TunnerBffFoundation.AntiforgeryHeader,csrf);
        return request;
    }
    internal async Task<HttpResponseMessage> Send(HttpMethod method,string path,string? cookie=null,string? origin=null,string? csrf=null)
    { using var request=Request(method,path,cookie,origin,csrf); return await Client.SendAsync(request); }
    internal async Task<(string Cookie,string Token,string WireCookie)> Csrf(string? session=null)
    {
        using var response=await Send(HttpMethod.Get,"/_fixture/csrf",session);response.EnsureSuccessStatusCode();
        var payload=await response.Content.ReadFromJsonAsync<JsonElement>();
        var wire=response.Headers.GetValues("Set-Cookie").Single(value=>value.StartsWith($"__Host-Tunner.{surface}.Csrf=",StringComparison.Ordinal));
        return (wire.Split(';')[0],payload.GetProperty("requestToken").GetString()!,wire);
    }
    internal async Task<(string Cookie,string WireCookie)> SignIn()
    {
        var csrf=await Csrf();using var response=await Send(HttpMethod.Post,"/_fixture/sign-in",csrf.Cookie,Origin,csrf.Token);response.EnsureSuccessStatusCode();
        var wire=response.Headers.GetValues("Set-Cookie").Single(value=>value.StartsWith($"__Host-Tunner.{surface}.Session=",StringComparison.Ordinal));return (wire.Split(';')[0],wire);
    }
    internal static AuthenticationTicket Ticket(BffSurface surface)
    {
        var properties=new AuthenticationProperties {IssuedUtc=DateTimeOffset.UtcNow.AddMinutes(-1),ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(10),AllowRefresh=false};
        properties.StoreTokens([new AuthenticationToken {Name="access_token",Value="a"},new AuthenticationToken {Name="refresh_token",Value="b"}]);
        return new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"fixture-principal")],Scheme(surface))),properties,Scheme(surface));
    }
    internal async Task<string> ServerCookie(string kind="valid")
    {
        var ticket=Ticket(surface);
        switch(kind)
        {
            case "expired":ticket.Properties.ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(-1);break;
            case "renewal":ticket.Properties.IssuedUtc=DateTimeOffset.UtcNow.AddMinutes(-9);ticket.Properties.ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(1);ticket.Properties.AllowRefresh=true;break;
            case "missing-expiry":ticket.Properties.ExpiresUtc=null;break;
            case "missing-issued":ticket.Properties.IssuedUtc=null;break;
            case "future-issued":ticket.Properties.IssuedUtc=DateTimeOffset.UtcNow.AddMinutes(2);break;
            case "foreign-identity":ticket=new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"fixture-principal")],"foreign")),ticket.Properties,Scheme(surface));break;
        }
        var key=await Store.StoreAsync(ticket);
        var clientTicket=new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId",key)],Scheme(surface))),new AuthenticationProperties(),Scheme(surface));
        return CookieOptions.Cookie.Name+"="+CookieOptions.TicketDataFormat.Protect(clientTicket);
    }
    public async ValueTask DisposeAsync(){Client.Dispose();await application.StopAsync();await application.DisposeAsync();}
}

internal sealed class TestBffTicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<string,byte[]> tickets=new(StringComparer.Ordinal);
    private int reads;
    private int failRead;
    internal void FailNextRead() => Interlocked.Exchange(ref failRead,1);
    internal int ReadCount => Volatile.Read(ref reads);
    public Task<string> StoreAsync(AuthenticationTicket ticket){var key=Guid.NewGuid().ToString("N");tickets[key]=TicketSerializer.Default.Serialize(ticket);return Task.FromResult(key);}
    public Task RenewAsync(string key,AuthenticationTicket ticket){tickets[key]=TicketSerializer.Default.Serialize(ticket);return Task.CompletedTask;}
    public Task<AuthenticationTicket?> RetrieveAsync(string key){Interlocked.Increment(ref reads);if(Interlocked.Exchange(ref failRead,0)==1)throw new IOException("fixture-store-detail");return Task.FromResult(tickets.TryGetValue(key,out var bytes)?TicketSerializer.Default.Deserialize(bytes):null);}
    public Task RemoveAsync(string key){tickets.TryRemove(key,out _);return Task.CompletedTask;}
}