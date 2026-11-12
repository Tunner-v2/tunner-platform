using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tunner.Platform.Bff;
using Tunner.Identity.Persistence;
using Tunner.User.Bff;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Real native handler + bounded BFF callback, local TLS and in-process synthetic HTTP only. No Identity/SQL/session acceptance.</summary>
public sealed class FederationOidcNativeProtocolTests
{
    [Theory]
    [InlineData(FederationProviderKind.Google)]
    [InlineData(FederationProviderKind.Microsoft)]
    public async Task QualifiedFreshBranchRequestsEssentialClaimsAndProjectsOnlyActualNativeEvidence(FederationProviderKind provider)
    {
        await using var host = await ProtocolHost.Start(provider);
        var challenge = await host.Challenge();
        Assert.True(host.EssentialFreshClaimsRequested && host.PromptLoginRequested == (provider == FederationProviderKind.Microsoft));
        using var response = await host.Callback(challenge);
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther && host.Successes == 1 && host.ExactFreshFactsObserved);
        Assert.True(host.Provider.TokenRequests == 1 && host.Provider.PkceAndConfidentialClientObserved);
    }

    [Theory]
    [InlineData("missing-auth-time")]
    [InlineData("duplicate-auth-time")]
    [InlineData("noninteger-auth-time")]
    [InlineData("missing-methods")]
    [InlineData("duplicate-methods")]
    public async Task FreshBranchRejectsMissingOrAmbiguousClaimsWithoutIatOrReceiptFallback(string defect)
    {
        await using var host = await ProtocolHost.Start(FederationProviderKind.Google);
        var challenge = await host.Challenge(); host.Provider.Defect = defect;
        using var response = await host.Callback(challenge);
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther && host.Provider.TokenRequests == 1 && host.Successes == 0);
        Assert.True(!host.ExactFreshFactsObserved && string.IsNullOrEmpty(await response.Content.ReadAsStringAsync()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unsigned")]
    [InlineData("empty-rs256")]
    [InlineData("unsupported")]
    public async Task NativeValidatedEventRejectsUnsupportedShapeWithoutProjectingIdentity(string shape)
    {
        var options = new OpenIdConnectOptions(); var successCalls = 0;
        var transport = new FederationOidcTransport("synthetic-native", SyntheticProvider.Issuer,
            SyntheticProvider.Issuer + "/.well-known/openid-configuration", SyntheticProvider.ClientId,
            "https://user.example.test/authentication/federation/google/callback", ["/login"], TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(1), TimeSpan.Zero, 16384, "__Host-native.Correlation.", "__Host-native.Nonce.");
        transport.Configure(options, SyntheticProvider.Secret, TimeProvider.System,
            (_, _) => { successCalls++; return Task.FromResult<string?>("/login"); }, (_, _) => Task.FromResult<string?>("/login"));
        var header = new JwtHeader { ["alg"] = shape == "unsigned" ? "none" : shape == "unsupported" ? "HS256" : "RS256" };
        // Synthetic event shapes are rejection inputs only; real verification remains covered by native TLS flows.
        var token = shape == "missing" ? null : new JwtSecurityToken(header, new JwtPayload(), "synthetic-header", "synthetic-payload",
            shape == "unsupported" ? "synthetic-signature" : string.Empty);
        var context = new TokenValidatedContext(new DefaultHttpContext(), new("synthetic-native", "synthetic-native", typeof(OpenIdConnectHandler)), options,
            new(new ClaimsIdentity()), new()) { SecurityToken = token! };
        await options.Events.TokenValidated(context);
        var failure = Assert.IsAssignableFrom<Exception>(context.Result?.Failure); Assert.Equal(0, successCalls);
        Assert.Equal("FEDERATION_PROTOCOL_REJECTED", failure.Message);
    }

    [Fact]
    public void NativeValidatorStateOwnershipAndOnlyRequiredClaimProjectionArePreserved()
    {
        var options = new OpenIdConnectOptions(); var nativeValidator = options.ProtocolValidator;
        var transport = new FederationOidcTransport("synthetic-native", SyntheticProvider.Issuer,
            SyntheticProvider.Issuer + "/.well-known/openid-configuration", SyntheticProvider.ClientId,
            "https://user.example.test/authentication/federation/google/callback", ["/login"], TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(1), TimeSpan.Zero, 16384, "__Host-native.Correlation.", "__Host-native.Nonce.");
        transport.Configure(options, SyntheticProvider.Secret, TimeProvider.System,
            (_, _) => Task.FromResult<string?>("/login"), (_, _) => Task.FromResult<string?>("/login"));
        Assert.Same(nativeValidator, options.ProtocolValidator);
        Assert.False(options.ProtocolValidator.RequireStateValidation); Assert.True(options.ProtocolValidator.RequireState);
        Assert.True(options.ProtocolValidator.RequireNonce); Assert.True(options.ProtocolValidator.RequireTimeStampInNonce);
        Assert.Equal(TimeSpan.FromMinutes(1), options.ProtocolValidator.NonceLifetime);
        Assert.DoesNotContain(options.ClaimActions, action => action.ClaimType is "iss" or "aud");
        Assert.Contains(options.ClaimActions, action => action.ClaimType == "nonce");
        Assert.False(options.MapInboundClaims); Assert.False(options.SaveTokens);
        Assert.True(options.TokenValidationParameters.ValidateIssuer); Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey); Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.Equal("__Host-native.Correlation.", options.CorrelationCookie.Name);
    }

    [Fact]
    public async Task NativeCodePkceSignatureNonceAndProtectedPropertiesReachOnlyTerminalClosedFacts()
    {
        await using var host = await ProtocolHost.Start();
        var challenge = await host.Challenge();
        using var response = await host.Callback(challenge);
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
        Assert.True(host.Successes == 1, host.Diagnostics()); Assert.Equal(1, host.SecretReads); Assert.True(host.Provider.PkceAndConfidentialClientObserved);
        Assert.Equal(1, host.Provider.TokenRequests); Assert.True(host.ExactFactsObserved);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("__Host-Tunner.User.Session", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("correlation")]
    [InlineData("nonce")]
    [InlineData("missing-nonce")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("unsigned")]
    [InlineData("expired")]
    [InlineData("provider-mixup")]
    [InlineData("withdrawn-profile")]
    [InlineData("secret-unavailable")]
    public async Task RealNativeValidationRejectsInvalidProtocolBeforeTheSuccessOwner(string defect)
    {
        await using var host = await ProtocolHost.Start();
        var challenge = await host.Challenge();
        host.Provider.Defect = defect;
        if (defect == "withdrawn-profile") host.Current = false;
        if (defect == "secret-unavailable") host.SecretUnavailable = true;
        using var response = await host.Callback(challenge, defect == "state", defect == "correlation", defect == "provider-mixup");
        Assert.Equal(0, host.Successes); Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(cookie => cookie.StartsWith("__Host-Tunner.User.Session", StringComparison.Ordinal)));
        if (defect is "state" or "correlation" or "provider-mixup" or "withdrawn-profile" or "secret-unavailable") Assert.Equal(0, host.Provider.TokenRequests);
    }

    [Fact]
    public async Task ReplayingTheSameStateCookiesAndCodeCannotCreateAnotherProtocolSuccess()
    {
        await using var host = await ProtocolHost.Start();
        var challenge = await host.Challenge();
        using var first = await host.Callback(challenge);
        using var repeated = await host.Callback(challenge);
        Assert.Equal(HttpStatusCode.SeeOther, first.StatusCode); Assert.Equal(HttpStatusCode.SeeOther, repeated.StatusCode);
        Assert.Equal(1, host.Successes); Assert.Equal(2, host.Provider.TokenRequests);
        // This checks the native exchange against a one-use synthetic authorization code. Durable attempt consumption
        // remains the Identity transaction owner's separate responsibility, not a claimed native state replay store.
    }

    [Fact]
    public async Task ExactCallbackBoundaryRejectsCaseAliasesAndQueryBeforeNativeExchange()
    {
        await using var host = await ProtocolHost.Start();
        var challenge = await host.Challenge();
        using var alias = await host.Callback(challenge, path: "/authentication/federation/Google/callback");
        using var query = await host.Callback(challenge, path: ProtocolHost.CallbackPath + "?code=synthetic");
        Assert.Equal(HttpStatusCode.BadRequest, alias.StatusCode); Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(0, host.Successes); Assert.Equal(0, host.Provider.TokenRequests);
    }

    private sealed record Challenge(string State, string Code, string Cookies)
    { public override string ToString() => nameof(Challenge); }

    private sealed class ProtocolHost : IAsyncDisposable
    {
        internal const string CallbackPath = "/authentication/federation/google/callback";
        private const string OtherCallback = "/authentication/federation/microsoft/callback";
        private const string Origin = "https://user.example.test";
        private const string Scheme = "synthetic-native-google";
        private const string OtherScheme = "synthetic-native-microsoft";
        private const string Attempt = "a87d097bd38f4dc98ba5010b600bbce3";
        private readonly WebApplication application;
        private readonly HttpClient client;
        private readonly X509Certificate2 certificate;
        internal readonly SyntheticProvider Provider;
        internal int Successes;
        internal int SecretReads;
        internal bool Current = true;
        internal bool SecretUnavailable;
        internal bool ExactFactsObserved;
        internal bool EssentialFreshClaimsRequested;
        internal bool PromptLoginRequested;
        internal bool ExactFreshFactsObserved;
        private int messages;
        private int tokenResponses;
        private int validatedTokens;
        private int tickets;
        private FederationOidcFailureReason? terminalFailure;
        private string failureClass = "NONE";

        internal string Diagnostics() => $"Messages={messages};SecretReads={SecretReads};TokenRequests={Provider.TokenRequests};" +
            $"PkceAndClient={Provider.PkceAndConfidentialClientObserved};TokenResponses={tokenResponses};ValidatedTokens={validatedTokens};" +
            $"Tickets={tickets};Failure={terminalFailure};Class={failureClass}";

        private ProtocolHost(WebApplication application, HttpClient client, X509Certificate2 certificate, SyntheticProvider provider)
        { this.application = application; this.client = client; this.certificate = certificate; Provider = provider; }

        internal static async Task<ProtocolHost> Start(FederationProviderKind? freshProvider = null)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var bytes = generated.Export(X509ContentType.Pkcs12);
            X509Certificate2 certificate;
            try { certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.DefaultKeySet); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            var provider = new SyntheticProvider { FreshEvidence = freshProvider is not null };
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(certificate)));
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddTunnerBffFoundation(BffSurface.User, Origin);
            ProtocolHost? host = null;
            foreach (var (scheme, callback) in new[] { (Scheme, CallbackPath), (OtherScheme, OtherCallback) })
            {
                var transport = new FederationOidcTransport(scheme, SyntheticProvider.Issuer, SyntheticProvider.Issuer + "/.well-known/openid-configuration",
                    SyntheticProvider.ClientId, Origin + callback, ["/login"], TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), TimeSpan.Zero,
                    16384, "__Host-" + scheme + ".Correlation.", "__Host-" + scheme + ".Nonce.");
                builder.Services.AddAuthentication().AddOpenIdConnect(scheme, options =>
                {
                    transport.Configure(options, TimeProvider.System, "synthetic-profile-v1", _ => host!.Current,
                        (_, _) =>
                        {
                            host!.SecretReads++;
                            return host.SecretUnavailable ? Task.FromException<string>(new InvalidOperationException("Unavailable.")) : Task.FromResult(SyntheticProvider.Secret);
                        },
                        (_, facts, _) =>
                        {
                            host!.Successes++;
                            host.ExactFactsObserved = facts.AttemptId == Attempt && facts.Subject == "opaque-subject" &&
                                facts.Issuer == SyntheticProvider.Issuer && facts.ClientId == SyntheticProvider.ClientId;
                            host.ExactFreshFactsObserved = facts.AuthenticatedAt == provider.AuthenticatedAt &&
                                facts.AuthenticationMethods is { Count: 2 } methods && methods.Contains("pwd", StringComparer.Ordinal) && methods.Contains("mfa", StringComparer.Ordinal);
                            return Task.FromResult<string?>("/login");
                        }, (_, failure, _) => { host!.terminalFailure = failure.Reason; return Task.FromResult<string?>("/login"); }, freshProvider);
                    var events = options.Events;
                    var message = events.OnMessageReceived; events.OnMessageReceived = context => { host!.messages++; return message(context); };
                    events.OnTokenResponseReceived = _ => { host!.tokenResponses++; return Task.CompletedTask; };
                    var tokenValidated = events.OnTokenValidated;
                    events.OnTokenValidated = context => { host!.validatedTokens++; return tokenValidated(context); };
                    var ticket = events.OnTicketReceived; events.OnTicketReceived = context => { host!.tickets++; return ticket(context); };
                    var authenticationFailed = events.OnAuthenticationFailed;
                    events.OnAuthenticationFailed = context => { host!.failureClass = Classify(context.Exception); return authenticationFailed(context); };
                    var remoteFailed = events.OnRemoteFailure;
                    events.OnRemoteFailure = context => { host!.failureClass = Classify(context.Failure); return remoteFailed(context); };
                    options.Backchannel = new HttpClient(provider, disposeHandler: false);
                });
                builder.Services.AddSingleton(new BffProtocolCallback(callback, 32768, 8, 128, 24576, async http =>
                {
                    var handler = await http.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>().GetHandlerAsync(http, scheme);
                    if (handler is not IAuthenticationRequestHandler native || !await native.HandleRequestAsync()) http.Response.StatusCode = 400;
                }));
            }
            var application = builder.Build(); application.UseTunnerBffFoundation();
            application.MapGet("/challenge", async (HttpContext http) =>
            {
                var properties = new AuthenticationProperties(); properties.Items[FederationOidcTransport.AttemptProperty] = Attempt;
                properties.Items[FederationOidcTransport.ProfileProperty] = "synthetic-profile-v1";
                if (freshProvider is not null) properties.Items[FederationOidcTransport.PurposeProperty] = "OperationProof";
                await http.ChallengeAsync(Scheme, properties);
            }).AllowAnonymous();
            await application.StartAsync();
            var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var clientHandler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented is not null &&
                    presented.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256) };
            var client = new HttpClient(clientHandler) { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Host = "user.example.test";
            host = new(application, client, certificate, provider); return host;
        }

        internal async Task<Challenge> Challenge()
        {
            using var response = await client.GetAsync("/challenge");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.NotNull(response.Headers.Location);
            var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
            PromptLoginRequested = query.TryGetValue("prompt", out var prompt) && prompt == "login";
            if (query.TryGetValue("claims", out var requested))
            {
                using var json = JsonDocument.Parse(requested.ToString());
                EssentialFreshClaimsRequested = json.RootElement.GetProperty("id_token").GetProperty("auth_time").GetProperty("essential").GetBoolean() &&
                    json.RootElement.GetProperty("id_token").GetProperty("amr").GetProperty("essential").GetBoolean();
            }
            Assert.True(query["response_type"] == "code" && query["response_mode"] == "form_post" && query["code_challenge_method"] == "S256");
            var code = Provider.Admit(query["nonce"].ToString(), query["code_challenge"].ToString());
            var cookies = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
            return new(query["state"].ToString(), code, cookies);
        }

        internal Task<HttpResponseMessage> Callback(Challenge challenge, bool invalidState = false, bool omitCookies = false,
            bool mixup = false, string? path = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path ?? (mixup ? OtherCallback : CallbackPath))
            { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["state"] = invalidState ? "invalid-native-state" : challenge.State, ["code"] = challenge.Code }) };
            // A real cross-site form_post has no Strict browser bootstrap/ordinary session cookie. Only native protocol cookies are supplied.
            request.Headers.Add("Origin", SyntheticProvider.Issuer);
            if (!omitCookies) request.Headers.Add("Cookie", challenge.Cookies);
            return SendAndDispose(request);
        }
        private async Task<HttpResponseMessage> SendAndDispose(HttpRequestMessage request)
        { using (request) return await client.SendAsync(request); }
        private static string Classify(Exception? error) => error switch
        {
            SecurityTokenInvalidIssuerException => "ISSUER", SecurityTokenInvalidAudienceException => "AUDIENCE",
            SecurityTokenSignatureKeyNotFoundException => "SIGNING_KEY", SecurityTokenInvalidSignatureException => "SIGNATURE",
            SecurityTokenExpiredException => "EXPIRY", SecurityTokenNotYetValidException => "NOT_YET_VALID",
            Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectProtocolInvalidNonceException => "NONCE",
            Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectProtocolException => "PROTOCOL",
            SecurityTokenException => "TOKEN", { InnerException: { } inner } => Classify(inner), _ => "OTHER"
        };
        public async ValueTask DisposeAsync()
        {
            client.Dispose(); await application.StopAsync(); await application.DisposeAsync(); Provider.Dispose(); certificate.Dispose();
        }
    }

    private sealed class SyntheticProvider : HttpMessageHandler
    {
        private static readonly string[] ResponseTypes = ["code"];
        private static readonly string[] ResponseModes = ["form_post"];
        private static readonly string[] SubjectTypes = ["public"];
        private static readonly string[] SigningAlgorithms = ["RS256"];
        private static readonly string[] AuthenticationMethods = ["client_secret_post"];
        private static readonly string[] QualifiedFreshMethods = ["pwd", "mfa"];
        private static readonly string[] DuplicateFreshMethods = ["pwd", "pwd"];
        internal const string Issuer = "https://issuer.example.test";
        internal const string ClientId = "synthetic-confidential-client";
        internal const string Secret = "synthetic-public-test-secret";
        private readonly RSA rsa = RSA.Create(2048);
        private readonly RSA wrongRsa = RSA.Create(2048);
        private string? code;
        private string? nonce;
        private string? challenge;
        private bool consumed;
        internal int TokenRequests;
        internal bool PkceAndConfidentialClientObserved;
        internal string? Defect;
        internal bool FreshEvidence;
        internal DateTimeOffset AuthenticatedAt { get; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds());

        internal string Admit(string nonce, string challenge)
        {
            this.nonce = nonce; this.challenge = challenge; code = Guid.NewGuid().ToString("N"); return code;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri == Issuer + "/.well-known/openid-configuration") return Json(new
            {
                issuer = Issuer, authorization_endpoint = Issuer + "/authorize", token_endpoint = Issuer + "/token", jwks_uri = Issuer + "/keys",
                response_types_supported = ResponseTypes, response_modes_supported = ResponseModes, subject_types_supported = SubjectTypes,
                id_token_signing_alg_values_supported = SigningAlgorithms, token_endpoint_auth_methods_supported = AuthenticationMethods
            });
            if (request.RequestUri?.AbsoluteUri == Issuer + "/keys")
            {
                var parameters = rsa.ExportParameters(false);
                return Json(new { keys = new[] { new { kty = "RSA", use = "sig", kid = "synthetic-key", alg = "RS256",
                    n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } });
            }
            if (request.RequestUri?.AbsoluteUri == Issuer + "/token" && request.Method == HttpMethod.Post)
            {
                TokenRequests++;
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                var verifier = form["code_verifier"].ToString();
                PkceAndConfidentialClientObserved = verifier.Length != 0 && Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) == challenge &&
                    form["client_id"] == ClientId && form["client_secret"] == Secret && form["grant_type"] == "authorization_code";
                if (consumed || form["code"] != code || !PkceAndConfidentialClientObserved)
                    return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
                consumed = true;
                var now = DateTime.UtcNow;
                var claims = new Dictionary<string, object> { ["sub"] = "opaque-subject" };
                if (Defect != "missing-nonce") claims["nonce"] = Defect == "nonce" ? "wrong-nonce" : nonce!;
                if (FreshEvidence)
                {
                    if (Defect != "missing-auth-time") claims["auth_time"] = Defect == "duplicate-auth-time"
                        ? new[] { AuthenticatedAt.ToUnixTimeSeconds(), AuthenticatedAt.ToUnixTimeSeconds() }
                        : Defect == "noninteger-auth-time" ? "not-an-integer" : AuthenticatedAt.ToUnixTimeSeconds();
                    if (Defect != "missing-methods") claims["amr"] = Defect == "duplicate-methods" ? DuplicateFreshMethods : QualifiedFreshMethods;
                }
                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = Defect == "issuer" ? "https://wrong-issuer.example.test" : Issuer,
                    Audience = Defect == "audience" ? "wrong-audience" : ClientId, Claims = claims,
                    IssuedAt = now.AddMinutes(-2), NotBefore = now.AddMinutes(-2), Expires = Defect == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5),
                    SigningCredentials = Defect == "unsigned" ? null : new(new RsaSecurityKey(Defect == "signature" ? wrongRsa : rsa) { KeyId = "synthetic-key" }, SecurityAlgorithms.RsaSha256)
                });
                return Json(new { access_token = "synthetic-unused-access-token", token_type = "Bearer", expires_in = 300, id_token = token });
            }
            return new(HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing) { if (disposing) { rsa.Dispose(); wrongRsa.Dispose(); } base.Dispose(disposing); }
    }
}
