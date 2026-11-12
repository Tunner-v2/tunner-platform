using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Tunner.User.Bff;

/// <summary>Terminal native protocol facts only; not current Identity authority, a session or fresh-authentication proof.</summary>
internal sealed class FederationOidcIdentityFacts(string issuer, string subject, string clientId, string attemptId)
{
    internal string Issuer { get; } = issuer;
    internal string Subject { get; } = subject;
    internal string ClientId { get; } = clientId;
    internal string AttemptId { get; } = attemptId;
    public override string ToString() => nameof(FederationOidcIdentityFacts);
}

internal enum FederationOidcFailureReason { ProtocolRejected, InvalidEvidence, OwnerUnavailable }

internal sealed class FederationOidcFailure(FederationOidcFailureReason reason, string? attemptId)
{
    internal FederationOidcFailureReason Reason { get; } = reason;
    internal string? AttemptId { get; } = attemptId;
    public override string ToString() => nameof(FederationOidcFailure);
}

/// <summary>
/// Uncomposed native transport. No routes, handlers, owner stores or authentication cookies are registered here.
/// The host must register its exact bounded terminal protocol callback and suppress native protocol diagnostics
/// before activation; native handlers can log before events. Owner callbacks receive no native context or tokens.
/// </summary>
internal sealed class FederationOidcTransport
{
    internal const string AttemptProperty = ".tunner.federation.attempt";
    internal const string ProfileProperty = ".tunner.federation.profile-version";
    internal const string NoSignInScheme = "Tunner.Federation.Transport.NoSignIn";
    private readonly string scheme;
    private readonly string issuer;
    private readonly HashSet<string> admittedIssuers;
    private readonly string metadataAddress;
    private readonly string clientId;
    private readonly Uri callback;
    private readonly HashSet<string> allowedContinuations;
    private readonly TimeSpan timeout;
    private readonly TimeSpan nonceLifetime;
    private readonly TimeSpan clockSkew;
    private readonly int maximumTokenBytes;
    private readonly string correlationCookieName;
    private readonly string nonceCookieName;

    internal FederationOidcTransport(string scheme, string issuer, string metadataAddress, string clientId, string callbackUri,
        IReadOnlyCollection<string> allowedContinuations, TimeSpan timeout, TimeSpan nonceLifetime, TimeSpan clockSkew,
        int maximumTokenBytes, string correlationCookieName, string nonceCookieName, IReadOnlyCollection<string>? admittedIssuers = null)
    {
        RequireOpaque(scheme); RequireOpaque(clientId);
        if (scheme == NoSignInScheme) throw new ArgumentException("The terminal transport scheme must be distinct.");
        _ = RequireHttps(issuer); _ = RequireHttps(metadataAddress); callback = RequireHttps(callbackUri);
        if (!StringComparer.Ordinal.Equals(callbackUri, callback.AbsoluteUri) || !CleanPath(callback.AbsolutePath))
            throw new ArgumentException("An exact clean callback is required.");
        ArgumentNullException.ThrowIfNull(allowedContinuations);
        if (allowedContinuations.Count == 0 || allowedContinuations.Any(path => !CleanPath(path)))
            throw new ArgumentException("Explicit clean local continuations are required.");
        if (timeout <= TimeSpan.Zero || nonceLifetime <= TimeSpan.Zero || clockSkew < TimeSpan.Zero || maximumTokenBytes <= 0)
            throw new ArgumentException("Explicit positive protocol bounds and nonnegative clock skew are required.");
        RequireCookieName(correlationCookieName); RequireCookieName(nonceCookieName);
        if (correlationCookieName == nonceCookieName) throw new ArgumentException("Protocol cookie identities must be distinct.");
        this.scheme = scheme; this.issuer = issuer; this.metadataAddress = metadataAddress; this.clientId = clientId;
        this.admittedIssuers = new(admittedIssuers ?? [issuer], StringComparer.Ordinal);
        if (!this.admittedIssuers.Contains(issuer) || this.admittedIssuers.Any(value => !Opaque(value)))
            throw new ArgumentException("Explicit admitted issuers must include the primary issuer.");
        this.allowedContinuations = new(allowedContinuations, StringComparer.Ordinal);
        this.timeout = timeout; this.nonceLifetime = nonceLifetime; this.clockSkew = clockSkew; this.maximumTokenBytes = maximumTokenBytes;
        this.correlationCookieName = correlationCookieName; this.nonceCookieName = nonceCookieName;
    }

    internal void Configure(OpenIdConnectOptions options, string clientSecret, TimeProvider clock,
        Func<FederationOidcIdentityFacts, CancellationToken, Task<string?>> completed,
        Func<FederationOidcFailure, CancellationToken, Task<string?>> failed)
        => ConfigureCore(options, clientSecret, clock, completed, failed, null, null, null);

    internal void Configure(OpenIdConnectOptions options, TimeProvider clock, string profileVersion,
        Func<HttpContext, bool> current,
        Func<HttpContext, CancellationToken, Task<string>> secret,
        Func<HttpContext, FederationOidcIdentityFacts, CancellationToken, Task<string?>> completed,
        Func<HttpContext, FederationOidcFailure, CancellationToken, Task<string?>> failed)
    {
        RequireOpaque(profileVersion); ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(secret);
        ConfigureCore(options, null, clock, (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(),
            profileVersion, current, secret, completed, failed);
    }

    private void ConfigureCore(OpenIdConnectOptions options, string? clientSecret, TimeProvider clock,
        Func<FederationOidcIdentityFacts, CancellationToken, Task<string?>> completed,
        Func<FederationOidcFailure, CancellationToken, Task<string?>> failed, string? profileVersion,
        Func<HttpContext, bool>? current, Func<HttpContext, CancellationToken, Task<string>>? secret,
        Func<HttpContext, FederationOidcIdentityFacts, CancellationToken, Task<string?>>? completedWithContext = null,
        Func<HttpContext, FederationOidcFailure, CancellationToken, Task<string?>>? failedWithContext = null)
    {
        ArgumentNullException.ThrowIfNull(options); if (clientSecret is not null) RequireOpaque(clientSecret); ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(completed); ArgumentNullException.ThrowIfNull(failed);
        bool Current(HttpContext http, AuthenticationProperties? properties, bool requireProtectedProfile) =>
            current is null || current(http) && (!requireProtectedProfile || properties is not null &&
                properties.Items.TryGetValue(ProfileProperty, out var version) && StringComparer.Ordinal.Equals(version, profileVersion));
        Task Fail(HttpContext http, FederationOidcFailureReason reason, string? attempt) => Failure(http, reason, attempt,
            (failure, ct) => failedWithContext is null ? failed(failure, ct) : failedWithContext(http, failure, ct));
        options.Authority = issuer; options.MetadataAddress = metadataAddress; options.RequireHttpsMetadata = true;
        options.ClientId = clientId; options.ClientSecret = clientSecret; options.CallbackPath = callback.AbsolutePath;
        options.RemoteSignOutPath = PathString.Empty; options.SignedOutCallbackPath = PathString.Empty;
        // Never resolve the application's ordinary/default sign-in scheme, even if a future event is misconfigured.
        // The host must not register this sentinel as a sign-in handler.
        options.SignInScheme = NoSignInScheme;
        options.ResponseType = OpenIdConnectResponseType.Code; options.ResponseMode = OpenIdConnectResponseMode.FormPost;
        options.UsePkce = true; options.MapInboundClaims = false; options.SaveTokens = false;
        // Native default deletion actions otherwise erase these validated JWT facts before TicketReceived.
        // Retain only the two required projection inputs; no native principal crosses the owner boundary.
        options.ClaimActions.Remove("iss"); options.ClaimActions.Remove("aud");
        options.GetClaimsFromUserInfoEndpoint = false; options.UseTokenLifetime = false; options.SkipUnrecognizedRequests = false;
        options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable; options.DisableTelemetry = true;
        options.Scope.Clear(); options.Scope.Add("openid"); options.TimeProvider = clock;
        options.RemoteAuthenticationTimeout = timeout; options.BackchannelTimeout = timeout;
        options.CorrelationCookie = Cookie(correlationCookieName, timeout); options.NonceCookie = Cookie(nonceCookieName, nonceLifetime);
        // Preserve ASP.NET's native validator: the handler validates protected state + correlation and clears
        // protocol State before invoking it. A bare replacement wrongly re-enables a second incompatible state check.
        options.ProtocolValidator.RequireNonce = true; options.ProtocolValidator.NonceLifetime = nonceLifetime;
        // IdentityModel8.19.2 token/nonce validation uses its native system UTC clock; its TimeProvider is internal.
        // Retain native validators, not a hand-written lifetime/JWT validator or a claimed injectable token clock.
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true, ValidIssuer = issuer, ValidIssuers = admittedIssuers.ToArray(), ValidateAudience = true, ValidAudience = clientId,
            RequireAudience = true, IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            ClockSkew = clockSkew, ValidAlgorithms = [SecurityAlgorithms.RsaSha256], SaveSigninToken = false,
            IncludeTokenOnFailedValidation = false, LogTokenId = false, LogValidationExceptions = false
        };
        options.TokenHandler.MaximumTokenSizeInBytes = maximumTokenBytes;
        options.Events = new()
        {
            OnRedirectToIdentityProvider = async context =>
            {
                if (current is not null && (!Current(context.HttpContext, context.Properties, true) || Attempt(context.Properties) is null))
                {
                    context.HandleResponse(); await Fail(context.HttpContext, FederationOidcFailureReason.InvalidEvidence, null).ConfigureAwait(false); return;
                }
                context.ProtocolMessage.ResponseType = OpenIdConnectResponseType.Code;
                context.ProtocolMessage.ResponseMode = OpenIdConnectResponseMode.FormPost;
                context.ProtocolMessage.ClientId = clientId; context.ProtocolMessage.RedirectUri = callback.AbsoluteUri;
            },
            OnMessageReceived = async context =>
            {
                var message = context.ProtocolMessage;
                if (Current(context.HttpContext, context.Properties, false) && ExpectedCallback(context.HttpContext, context.Scheme.Name) && message is not null && !string.IsNullOrEmpty(message.Code) &&
                    string.IsNullOrEmpty(message.IdToken) && string.IsNullOrEmpty(message.AccessToken) && string.IsNullOrEmpty(message.Error)) return;
                context.HandleResponse();
                await Fail(context.HttpContext, FederationOidcFailureReason.ProtocolRejected, null).ConfigureAwait(false);
            },
            OnAuthorizationCodeReceived = async context =>
            {
                if (secret is null) return;
                try
                {
                    if (!Current(context.HttpContext, context.Properties, true) || Attempt(context.Properties) is null || context.TokenEndpointRequest is null)
                        throw new InvalidOperationException();
                    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext.RequestAborted);
                    bounded.CancelAfter(timeout);
                    var value = await secret(context.HttpContext, bounded.Token).ConfigureAwait(false); RequireOpaque(value);
                    if (!Current(context.HttpContext, context.Properties, true)) throw new InvalidOperationException();
                    // This message belongs to this exchange. Never mutate shared named options per request.
                    context.TokenEndpointRequest.ClientSecret = value;
                }
                catch (Exception)
                {
                    context.HandleResponse(); await Fail(context.HttpContext, FederationOidcFailureReason.OwnerUnavailable, Attempt(context.Properties)).ConfigureAwait(false);
                }
            },
            OnTokenValidated = context =>
            {
                // Native code-flow redemption allows unsigned tokens over TLS even when the configured
                // RequireSignedTokens is true. Require our supported signed JWS shape after native validation.
                // Nonempty signatures still undergo native verification. The framework's JwtSecurityToken
                // conversion drops SigningKey, so its absence here is not evidence of a failed signature.
                var token = context.SecurityToken;
                if (token is null || token.InnerToken is not null || token.SignatureAlgorithm != SecurityAlgorithms.RsaSha256 ||
                    string.IsNullOrEmpty(token.RawSignature)) context.Fail("FEDERATION_PROTOCOL_REJECTED");
                // Rejection only: protocol/nonce checks still follow; facts are projected only in TicketReceived.
                return Task.CompletedTask;
            },
            OnTicketReceived = async context =>
            {
                context.HandleResponse(); context.ReturnUri = null;
                if (context.Properties is { } properties) properties.RedirectUri = null;
                var attempt = Attempt(context.Properties);
                var claims = context.Principal;
                var subject = SingleClaim(claims, "sub"); var actualIssuer = SingleClaim(claims, "iss"); var audience = SingleClaim(claims, "aud");
                if (!Current(context.HttpContext, context.Properties, true) || !ExpectedCallback(context.HttpContext, context.Scheme.Name) ||
                    attempt is null || subject is null || actualIssuer is null || !admittedIssuers.Contains(actualIssuer) || audience != clientId)
                {
                    await Fail(context.HttpContext, FederationOidcFailureReason.InvalidEvidence, attempt).ConfigureAwait(false);
                    return;
                }
                try
                {
                    var facts = new FederationOidcIdentityFacts(actualIssuer, subject, clientId, attempt);
                    var continuation = await (completedWithContext is null ? completed(facts, context.HttpContext.RequestAborted) :
                        completedWithContext(context.HttpContext, facts, context.HttpContext.RequestAborted)).ConfigureAwait(false);
                    Finish(context.HttpContext, continuation);
                }
                catch (Exception)
                {
                    // A failed/uncertain owner callback is not retried, signed in or serialized as a raw error.
                    await Fail(context.HttpContext, FederationOidcFailureReason.OwnerUnavailable, attempt).ConfigureAwait(false);
                }
            },
            OnAuthenticationFailed = async context =>
            {
                context.HandleResponse();
                await Fail(context.HttpContext, FederationOidcFailureReason.ProtocolRejected, Attempt(context.Properties)).ConfigureAwait(false);
            },
            OnRemoteFailure = async context =>
            {
                context.HandleResponse();
                await Fail(context.HttpContext, FederationOidcFailureReason.ProtocolRejected, Attempt(context.Properties)).ConfigureAwait(false);
            }
        };
    }

    private bool ExpectedCallback(HttpContext context, string actualScheme) => actualScheme == scheme && context.Request.IsHttps &&
        HttpMethods.IsPost(context.Request.Method) && context.Request.ContentType?.Split(';')[0].Trim().Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true &&
        StringComparer.Ordinal.Equals(context.Request.Scheme + "://" + context.Request.Host.Value + context.Request.Path.Value, callback.AbsoluteUri) &&
        !context.Request.QueryString.HasValue;

    private async Task Failure(HttpContext context, FederationOidcFailureReason reason, string? attempt,
        Func<FederationOidcFailure, CancellationToken, Task<string?>> failed)
    {
        Finish(context, null);
        try { Finish(context, await failed(new(reason, attempt), context.RequestAborted).ConfigureAwait(false)); }
        catch (Exception) { Finish(context, null); }
    }

    private void Finish(HttpContext context, string? continuation)
    {
        context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Remove("Location"); context.Response.StatusCode = StatusCodes.Status400BadRequest;
        if (continuation is not null && allowedContinuations.Contains(continuation))
        { context.Response.StatusCode = StatusCodes.Status303SeeOther; context.Response.Headers.Location = continuation; }
    }

    private static string? Attempt(AuthenticationProperties? properties) => properties is not null &&
        properties.Items.TryGetValue(AttemptProperty, out var attempt) && Opaque(attempt) ? attempt : null;

    private static string? SingleClaim(ClaimsPrincipal? principal, string type)
    {
        var claims = principal?.FindAll(type).Take(2).ToArray();
        return claims is { Length: 1 } && Opaque(claims[0].Value) ? claims[0].Value : null;
    }

    private static bool Opaque(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Contains('\0');
    private static void RequireOpaque(string value) { if (!Opaque(value)) throw new ArgumentException("An exact trusted transport value is required."); }
    private static Uri RequireHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || value.Contains('*')) throw new ArgumentException("An explicit exact HTTPS endpoint is required.");
        return uri;
    }

    private static bool CleanPath(string? path) => !string.IsNullOrWhiteSpace(path) && path[0] == '/' &&
        !path.Contains("//", StringComparison.Ordinal) && !path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '?' or '#' or '%' or ':') &&
        !path.Split('/').Any(segment => segment is "." or "..");

    private static void RequireCookieName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("__Host-", StringComparison.Ordinal) ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new ArgumentException("An explicit host-only protocol cookie identity is required.");
    }

    private static CookieBuilder Cookie(string name, TimeSpan lifetime) => new()
    {
        Name = name, Path = "/", Domain = null, HttpOnly = true, SecurePolicy = CookieSecurePolicy.Always,
        SameSite = SameSiteMode.None, IsEssential = true, Expiration = lifetime, MaxAge = lifetime
    };

    public override string ToString() => nameof(FederationOidcTransport);
}
