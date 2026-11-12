using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Tunner.User.Bff;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class FederationOidcTransportTests
{
    private const string Scheme = "synthetic-oidc";
    private const string Issuer = "https://issuer.example.test";
    private const string Client = "synthetic-client";
    private const string Callback = "https://user.example.test/federation/callback";
    private static FederationOidcTransport Transport(string issuer = Issuer, string callback = Callback,
        string metadata = "https://issuer.example.test/.well-known/openid-configuration", string[]? returns = null,
        TimeSpan? timeout = null, string correlationCookie = "__Host-synthetic-correlation.") =>
        new(Scheme, issuer, metadata, Client, callback, returns ?? ["/synthetic/complete", "/synthetic/failure"],
            timeout ?? TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5), 8192,
            correlationCookie, "__Host-synthetic-nonce.");

    private static OpenIdConnectOptions Options(Func<FederationOidcIdentityFacts, CancellationToken, Task<string?>>? completed = null,
        Func<FederationOidcFailure, CancellationToken, Task<string?>>? failed = null)
    {
        var options = new OpenIdConnectOptions();
        Transport().Configure(options, "synthetic-confidential-secret", TimeProvider.System,
            completed ?? ((_, _) => Task.FromResult<string?>("/synthetic/complete")), failed ?? ((_, _) => Task.FromResult<string?>("/synthetic/failure")));
        return options;
    }

    private static DefaultHttpContext Http()
    {
        var http = new DefaultHttpContext(); http.Request.Scheme = "https"; http.Request.Host = new("user.example.test");
        http.Request.Path = "/federation/callback"; http.Request.Method = "POST"; http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Response.Body = new MemoryStream(); return http;
    }

    private static TicketReceivedContext Ticket(OpenIdConnectOptions options, string? attempt = "synthetic-attempt", Claim[]? claims = null)
    {
        var properties = new AuthenticationProperties { RedirectUri = "https://untrusted.example.test/raw" };
        if (attempt is not null) properties.Items[FederationOidcTransport.AttemptProperty] = attempt;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims ?? [new("iss", Issuer), new("sub", "opaque-subject"), new("aud", Client)], "synthetic-native"));
        return new(Http(), new(Scheme, Scheme, typeof(OpenIdConnectHandler)), options, new(principal, properties, Scheme));
    }

    [Fact]
    public void NativeOptionsAreConfidentialCodePkceFormPostAndDoNotComposeOrSaveAuthentication()
    {
        var options = Options();
        Assert.Equal("code", options.ResponseType); Assert.Equal("form_post", options.ResponseMode); Assert.True(options.UsePkce);
        Assert.False(options.MapInboundClaims); Assert.False(options.SaveTokens); Assert.False(options.GetClaimsFromUserInfoEndpoint);
        Assert.False(options.UseTokenLifetime); Assert.False(options.SkipUnrecognizedRequests); Assert.True(options.RequireHttpsMetadata);
        Assert.Equal(FederationOidcTransport.NoSignInScheme, options.SignInScheme);
        Assert.Equal(new[] { "openid" }, options.Scope); Assert.False(options.RemoteSignOutPath.HasValue); Assert.False(options.SignedOutCallbackPath.HasValue);
        Assert.True(options.TokenValidationParameters.ValidateIssuer); Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime); Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens); Assert.True(options.TokenValidationParameters.RequireExpirationTime);
        Assert.False(options.TokenValidationParameters.IgnoreTrailingSlashWhenValidatingAudience);
        Assert.Null(options.TokenValidationParameters.LifetimeValidator); Assert.Null(options.TokenValidationParameters.SignatureValidator);
        Assert.Equal(TimeSpan.FromSeconds(5), options.TokenValidationParameters.ClockSkew);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RemoteAuthenticationTimeout); Assert.Equal(TimeSpan.FromMinutes(1), options.ProtocolValidator.NonceLifetime);
        Assert.Same(TimeProvider.System, options.TimeProvider); Assert.Equal(8192, options.TokenHandler.MaximumTokenSizeInBytes);
        foreach (var cookie in new[] { options.CorrelationCookie, options.NonceCookie })
        {
            Assert.True(cookie.HttpOnly); Assert.Equal(CookieSecurePolicy.Always, cookie.SecurePolicy);
            Assert.Equal(SameSiteMode.None, cookie.SameSite); Assert.Equal("/", cookie.Path); Assert.Null(cookie.Domain);
        }
    }

    [Fact]
    public async Task TerminalTicketOnlyProjectsClosedFactsAndIgnoresNativeReturnUriAndOtherClaims()
    {
        FederationOidcIdentityFacts? observed = null;
        var options = Options((facts, _) => { observed = facts; return Task.FromResult<string?>("/synthetic/complete"); });
        var context = Ticket(options, claims: [new("iss", Issuer), new("sub", " exact-subject "), new("aud", Client), new("email", "untrusted@example.test"), new("auth_time", "unqualified")]);
        await options.Events.TicketReceived(context);
        Assert.True(context.Result!.Handled); Assert.NotNull(observed); Assert.Equal(" exact-subject ", observed.Subject);
        Assert.Equal(Issuer, observed.Issuer); Assert.Equal(Client, observed.ClientId); Assert.Equal("synthetic-attempt", observed.AttemptId);
        Assert.Equal("{}", JsonSerializer.Serialize(observed)); Assert.Equal(nameof(FederationOidcIdentityFacts), observed.ToString());
        Assert.Null(context.ReturnUri); Assert.Null(context.Properties.RedirectUri);
        Assert.Equal("/synthetic/complete", context.Response.Headers.Location); Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Theory]
    [InlineData("missing-attempt")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("missing-subject")]
    [InlineData("duplicate-subject")]
    [InlineData("duplicate-issuer")]
    [InlineData("duplicate-audience")]
    public async Task MissingOrAmbiguousTerminalEvidenceNeverReachesSuccessOwner(string change)
    {
        var successCalls = 0; FederationOidcFailure? failure = null;
        var options = Options((_, _) => { successCalls++; return Task.FromResult<string?>("/synthetic/complete"); },
            (value, _) => { failure = value; return Task.FromResult<string?>("/synthetic/failure"); });
        var claims = new List<Claim> { new("iss", change == "issuer" ? "https://other.example.test" : Issuer), new("aud", change == "audience" ? "other-client" : Client) };
        if (change != "missing-subject") claims.Add(new("sub", "subject"));
        if (change.StartsWith("duplicate-", StringComparison.Ordinal)) claims.Add(new(change == "duplicate-subject" ? "sub" : change == "duplicate-issuer" ? "iss" : "aud", "ambiguous"));
        var context = Ticket(options, change == "missing-attempt" ? null : "synthetic-attempt", claims.ToArray());
        await options.Events.TicketReceived(context);
        Assert.True(context.Result!.Handled); Assert.Equal(0, successCalls); Assert.NotNull(failure);
        Assert.Equal(FederationOidcFailureReason.InvalidEvidence, failure.Reason); Assert.Equal("{}", JsonSerializer.Serialize(failure));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("query")]
    [InlineData("id-token")]
    [InlineData("access-token")]
    [InlineData("missing-code")]
    [InlineData("wrong-path")]
    [InlineData("wrong-case")]
    public async Task NativeMessageGateAcceptsOnlyExactCodeFormPost(string change)
    {
        var options = Options(); var http = Http();
        if (change == "GET") http.Request.Method = "GET";
        if (change == "query") http.Request.QueryString = new("?code=untrusted");
        if (change == "wrong-path") http.Request.Path = "/other";
        if (change == "wrong-case") http.Request.Path = "/Federation/callback";
        var context = new MessageReceivedContext(http, new(Scheme, Scheme, typeof(OpenIdConnectHandler)), options, new())
        {
            ProtocolMessage = new() { Code = change == "missing-code" ? null : "synthetic-code", IdToken = change == "id-token" ? "synthetic-id-token" : null,
                AccessToken = change == "access-token" ? "synthetic-access-token" : null }
        };
        await options.Events.MessageReceived(context);
        Assert.True(context.Result!.Handled); Assert.Equal("/synthetic/failure", http.Response.Headers.Location);
    }

    [Fact]
    public async Task CodeFormPostProceedsToNativeProtocolValidationWithoutIssuingFactsEarly()
    {
        var options = Options(); var context = new MessageReceivedContext(Http(), new(Scheme, Scheme, typeof(OpenIdConnectHandler)), options, new())
        { ProtocolMessage = new() { Code = "synthetic-code" } };
        await options.Events.MessageReceived(context); Assert.Null(context.Result);
        var early = new TokenValidatedContext(Http(), new(Scheme, Scheme, typeof(OpenIdConnectHandler)), options,
            new(new ClaimsIdentity([new("sub", "subject")], "synthetic")), new());
        await options.Events.TokenValidated(early); Assert.Null(early.Result);
    }

    [Fact]
    public async Task NativeFailuresAndThrowingOwnersRemainHandledWithoutRawErrorOrSigninFallback()
    {
        var options = Options((_, _) => throw new InvalidOperationException("sensitive-owner-detail"), (_, _) => throw new InvalidOperationException("sensitive-failure-detail"));
        var ticket = Ticket(options); await options.Events.TicketReceived(ticket);
        Assert.True(ticket.Result!.Handled); Assert.Equal(400, ticket.Response.StatusCode); Assert.False(ticket.Response.Headers.ContainsKey("Location"));
        var remote = new RemoteFailureContext(Http(), new(Scheme, Scheme, typeof(OpenIdConnectHandler)), options, new InvalidOperationException("sensitive-native-detail"));
        await options.Events.RemoteFailure(remote); Assert.True(remote.Result!.Handled); Assert.Equal(400, remote.Response.StatusCode);
        var failed = new AuthenticationFailedContext(Http(), new(Scheme, Scheme, typeof(OpenIdConnectHandler)), options)
        { Exception = new InvalidOperationException("sensitive-native-detail") };
        await options.Events.AuthenticationFailed(failed); Assert.True(failed.Result!.Handled); Assert.Equal(400, failed.Response.StatusCode);
        Assert.Equal(0, ticket.Response.Body.Length); Assert.Equal(0, remote.Response.Body.Length); Assert.Equal(0, failed.Response.Body.Length);
    }

    [Theory]
    [InlineData("https://external.example.test")]
    [InlineData("/synthetic/unapproved")]
    [InlineData("/synthetic/complete?token=value")]
    public async Task EvenTrustedOwnerReturnMustMatchTheCleanAllowlist(string destination)
    {
        var options = Options((_, _) => Task.FromResult<string?>(destination)); var context = Ticket(options);
        await options.Events.TicketReceived(context); Assert.True(context.Result!.Handled);
        Assert.Equal(400, context.Response.StatusCode); Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public void InvalidTrustedProfilesFailBeforeNativeConfiguration()
    {
        Assert.Throws<ArgumentException>(() => Transport(issuer: "http://issuer.example.test"));
        Assert.Throws<ArgumentException>(() => Transport(issuer: "https://*.example.test"));
        Assert.Throws<ArgumentException>(() => Transport(callback: Callback + "?token=value"));
        Assert.Throws<ArgumentException>(() => Transport(metadata: "http://issuer.example.test/config"));
        Assert.Throws<ArgumentException>(() => Transport(returns: ["//external.example.test"]));
        Assert.Throws<ArgumentException>(() => Transport(timeout: TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => Transport(correlationCookie: "ordinary-cookie"));
        Assert.Equal("{}", JsonSerializer.Serialize(Transport())); Assert.Equal(nameof(FederationOidcTransport), Transport().ToString());
    }
}
