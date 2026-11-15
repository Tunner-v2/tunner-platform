using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

/// <summary>Owner opt-in. The shared BFF HTTPS/origin/CSRF middleware must run before these routes.</summary>
public static class AuthenticationEndpoints
{
    public const string BrowserCookieName = "__Host-Tunner.Authentication";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    internal static readonly object GrantItem = new();
    internal const string SignInPath = "/authentication/sign-in";
    internal sealed class CredentialAuthenticationMetadata;
    internal sealed record Surface(string Origin);
    internal sealed record Browser(string Nonce, Guid Key, DateTimeOffset ExpiresAt, string PolicyVersion, string PreviousCookieDigest, Guid? PreviousSessionId);
    private sealed record AuthenticationSessionResponse(bool Authenticated, string? CsrfToken,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ProductAuthorizationPending);
    private sealed record AuthenticationBootstrapResponse(string? CsrfToken, Guid IdempotencyKey, int PasswordMaximumLength,
        bool PasskeyAvailable, int? MaximumCredentialJsonBytes, object? PasskeyRecovery, string Locale,
        IReadOnlyDictionary<string, RegistrationPublishedText> Content, Uri TermsUri, Uri PrivacyUri, Uri? SupportUri,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FederationBootstrap? Federation,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RecoveryAvailable)
    {
        public override string ToString() => nameof(AuthenticationBootstrapResponse);
    }

    public static IServiceCollection AddTunnerPasswordAuthentication(this IServiceCollection services, string browserOrigin)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Uri.TryCreate(browserOrigin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("A canonical HTTPS User BFF origin is required.", nameof(browserOrigin));
        Type[] required = [typeof(IAuthenticationPolicySource), typeof(IAccountAuthenticationAccessSource), typeof(AuthenticationPartitionKey), typeof(IdentityPersistenceDbContext), typeof(IAuthenticationPresentationSource)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)))
            throw new InvalidOperationException("Authentication requires explicit policy, Account access, partition key and persistence providers.");
        if (services.Any(x => x.ServiceType == typeof(Surface))) throw new InvalidOperationException("Authentication may be activated only once.");
        services.AddSingleton(new Surface(uri.GetLeftPart(UriPartial.Authority)));
        services.TryAddSingleton(TimeProvider.System); services.AddHttpContextAccessor();
        services.TryAddSingleton<IdentityWebAssets>();
        services.AddSingleton<AuthenticationPasswordWork>(); services.AddScoped<AuthenticationApplication>();
        services.AddScoped<IAuthenticationSessionMaintenance>(provider => provider.GetRequiredService<AuthenticationApplication>());
        services.AddOptions<CookieAuthenticationOptions>(AuthenticationIdentity.Scheme).Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        services.Replace(ServiceDescriptor.Singleton<ITicketStore, AuthenticationTicketStore>());
        services.PostConfigure<CookieAuthenticationOptions>(AuthenticationIdentity.Scheme, options =>
        {
            options.CookieManager = new CredentialCookieManager();
            options.Events.OnSigningIn = context =>
            {
                if (!context.HttpContext.Items.TryGetValue(GrantItem, out var item) || item is not AuthenticationGrant grant ||
                    context.Principal != grant.Ticket.Principal || context.Properties != grant.Ticket.Properties ||
                    grant.Ticket.AuthenticationScheme != context.Scheme.Name || context.Properties.IssuedUtc is null || context.Properties.ExpiresUtc is null)
                    throw new InvalidOperationException("Only an Identity-issued authentication grant may establish a User session.");
                return Task.CompletedTask;
            };
        });
        return services;
    }

    public static IEndpointRouteBuilder MapTunnerPasswordAuthentication(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/authentication/options", async (HttpContext context, IAuthenticationPolicySource policies, Surface surface,
            IDataProtectionProvider protection, IAntiforgery antiforgery, IAuthenticationPresentationSource presentationSource, TimeProvider clock) =>
        {
            try
            {
                var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
                var presentation = presentationSource.RequirePublished(policy);
                var passkeys = context.RequestServices.GetService<PasskeyBrowserService>();
                var signInRecovery = passkeys is null ? null : await passkeys.SignInRecoveryBootstrapAsync(context).ConfigureAwait(false);
                var federation = context.RequestServices.GetService<FederationBrowserService>();
                // This describes an exact original result under the observed principal. It does not
                // generate a second antiforgery identity in this anonymous-credential bootstrap.
                var federationBootstrap = federation is null ? null : await federation.BootstrapAsync(context).ConfigureAwait(false);
                var protector = Protector(protection); var fingerprint = CookieDigest(context);
                if (!TryBrowser(context, protector, now, out var browser) || browser!.PolicyVersion != policy.Version ||
                    browser.PreviousCookieDigest != fingerprint && signInRecovery is null && federationBootstrap?.PreserveOriginalBrowser != true)
                {
                    browser = new Browser(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Guid.NewGuid(), now + policy.BrowserContextLifetime,
                        policy.Version, fingerprint, AuthenticationIdentity.SessionId(context.User));
                    context.Response.Cookies.Append(BrowserCookieName, protector.Protect(JsonSerializer.Serialize(browser)), new CookieOptions
                        { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = policy.BrowserContextLifetime });
                }
                // Login is a new anonymous credential transaction even when this browser has a prior authenticated session.
                var prior = context.User; string? csrf;
                try { context.User = new ClaimsPrincipal(new ClaimsIdentity()); csrf = antiforgery.GetAndStoreTokens(context).RequestToken; }
                finally { context.User = prior; }
                var capability = passkeys?.Capability();
                var terminalRecovery = passkeys is null ? null : await passkeys.RecoveryBootstrapAsync(context, terminalOnly: true).ConfigureAwait(false);
                var passkeyRecovery = terminalRecovery ?? signInRecovery;
                return Results.Json(new AuthenticationBootstrapResponse(csrf, browser!.Key, policy.PasswordMaximumLength,
                    capability?.Available == true, capability?.MaximumCredentialJsonBytes, passkeyRecovery, presentation.Locale,
                    presentation.Content, presentation.TermsUri, presentation.PrivacyUri, presentation.SupportUri, federationBootstrap,
                    context.RequestServices.GetService<RecoveryBrowserService>()?.Available == true ? true : null));
            }
            catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
            catch (ArgumentException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
            catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        }).AllowAnonymous();
        endpoints.MapPost(SignInPath, SignInAsync).AllowAnonymous().WithMetadata(new CredentialAuthenticationMetadata());
        endpoints.MapPost("/authentication/result", PasswordResultAsync).RequireAuthorization();
        endpoints.MapGet("/authentication/session", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            var continuation = context.RequestServices.GetService<ProductAuthorizationContinuation>();
            var pending = continuation is not null && await continuation.PendingAsync(context, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(new AuthenticationSessionResponse(true, antiforgery.GetAndStoreTokens(context).RequestToken, pending ? true : null));
        }).RequireAuthorization();
        endpoints.MapPost("/authentication/sign-out", async (HttpContext context) =>
        {
            await context.SignOutAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
            context.Response.Cookies.Delete(PasskeyBrowserService.SignInRecoveryCookie, new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict });
            context.Response.Cookies.Delete(BrowserCookieName, new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict });
            return Results.Json(new { authenticated = false });
        }).RequireAuthorization();
        SessionListingEndpoints.MapIfConfigured(endpoints);
        ProductAuthorizationContinuationEndpoints.MapIfConfigured(endpoints);
        MapScreens(endpoints);
        return endpoints;
    }

    private static async Task<IResult> PasswordResultAsync(HttpContext context, IDataProtectionProvider protection,
        AuthenticationApplication application, Surface surface, TimeProvider clock)
    {
        try
        {
            if (context.Request.QueryString.HasValue || !context.Request.HasJsonContentType()) return Problem(400, "VALIDATION_ERROR");
            var command = await IdentityJsonCommand.ReadAsync<AuthenticationOutcomeCommand>(context, 4096).ConfigureAwait(false);
            if (command.IdempotencyKey == Guid.Empty || !TryBrowser(context, Protector(protection), clock.GetUtcNow(), out var browser) ||
                browser!.Key != command.IdempotencyKey || AuthenticationIdentity.SessionId(context.User) is not { } currentSession)
                return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
            // The old raw cookie digest is deliberately not result proof: a committed response rotates the cookie.
            var completed = await application.ConfirmPasswordOutcomeAsync(AuthenticationIdentity.Digest(browser.Nonce), command,
                currentSession, context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, surface.Origin, context.RequestAborted).ConfigureAwait(false);
            return completed ? Results.Json(new { status = "COMPLETED", command.IdempotencyKey }) : Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
    private static async Task<IResult> SignInAsync(HttpContext context, IAuthenticationPolicySource policies,
        IDataProtectionProvider protection, AuthenticationPartitionKey partitions, AuthenticationApplication application, Surface surface, TimeProvider clock)
    {
        try
        {
            if (!context.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
            var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
            if (!TryBrowser(context, Protector(protection), now, out var browser) || browser!.PolicyVersion != policy.Version ||
                browser.PreviousCookieDigest != CookieDigest(context) || context.Connection.RemoteIpAddress is null)
                return Problem(403, TechnicalProblemCodes.ForbiddenScope);
            var maximum = checked((long)policy.PasswordMaximumLength * 6 + 4096);
            var command = await IdentityJsonCommand.ReadAsync<PasswordSignInCommand>(context, maximum).ConfigureAwait(false);
            if (command is null || command.IdempotencyKey != browser.Key) return Problem(400, "VALIDATION_ERROR");
            var caller = new AuthenticationCaller(AuthenticationIdentity.Digest(browser.Nonce), partitions.Network(context.Connection.RemoteIpAddress), surface.Origin, browser.PreviousSessionId);
            var result = await application.SignInAsync(caller, command, context.RequestAborted).ConfigureAwait(false);
            if (result.ErrorCode is { } error) return Problem(error switch { "RATE_OR_ABUSE_LIMITED" => 429, "VERSION_CONFLICT" => 409, "AUTHENTICATION_FAILED" => 401, _ => 400 }, error);
            if (result.PendingMfa is { } pending) return TotpBrowserService.PendingMfa(context, pending, protection, clock.GetUtcNow());
            var grant = result.Grant!; context.Items[GrantItem] = grant;
            await context.SignInAsync(AuthenticationIdentity.Scheme, grant.Ticket.Principal, grant.Ticket.Properties).ConfigureAwait(false);
            return Results.Json(new { authenticated = true });
        }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
    private static void MapScreens(IEndpointRouteBuilder endpoints)
    {
        var assets = endpoints.ServiceProvider.GetRequiredService<IdentityWebAssets>();
        assets.MapAssets(endpoints);
        assets.MapScreens(endpoints, "authentication.html", ["/login", "/signed-in"],
            static context => context.Request.Path != "/signed-in" || context.User.Identity?.IsAuthenticated == true);
    }
    internal static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Tunner.User.Authentication.Browser.v1");
    internal static bool TryBrowser(HttpContext context, IDataProtector protector, DateTimeOffset now, out Browser? browser)
    {
        browser = null;
        if (!context.Request.Cookies.TryGetValue(BrowserCookieName, out var cookie)) return false;
        try { browser = JsonSerializer.Deserialize<Browser>(protector.Unprotect(cookie)); }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
        return browser is { Nonce.Length: 64 } && browser.Nonce.All(Uri.IsHexDigit) && browser.Key != Guid.Empty && browser.ExpiresAt > now;
    }
    internal static string CookieDigest(HttpContext context) => AuthenticationIdentity.Digest(new ChunkingCookieManager().GetRequestCookie(context, AuthenticationIdentity.CookieName) ?? string.Empty);
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));

    /// <summary>Framework still owns cookie wire format; only this credential POST starts a new anonymous authentication transaction.</summary>
    private sealed class CredentialCookieManager : ICookieManager
    {
        private readonly ChunkingCookieManager inner = new();
        public string? GetRequestCookie(HttpContext context, string key) => key == AuthenticationIdentity.CookieName &&
            HttpMethods.IsPost(context.Request.Method) && context.GetEndpoint()?.Metadata.GetMetadata<CredentialAuthenticationMetadata>() is not null
                ? null : inner.GetRequestCookie(context, key);
        public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options) => inner.AppendResponseCookie(context, key, value, options);
        public void DeleteCookie(HttpContext context, string key, CookieOptions options) => inner.DeleteCookie(context, key, options);
    }
}

internal sealed class AuthenticationTicketStore(IServiceScopeFactory scopes, IHttpContextAccessor accessor, AuthenticationEndpoints.Surface surface) : ITicketStore
{
    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, RequireContext(), CancellationToken.None);
    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue(AuthenticationEndpoints.GrantItem, out var item) || item is not AuthenticationGrant grant ||
            AuthenticationIdentity.SessionId(ticket.Principal) != AuthenticationIdentity.SessionId(grant.Ticket.Principal) || ticket.AuthenticationScheme != AuthenticationIdentity.Scheme ||
            ticket.Properties.ExpiresUtc != grant.Ticket.Properties.ExpiresUtc)
            throw new InvalidOperationException("A trusted Identity grant is required.");
        await using var scope = scopes.CreateAsyncScope();
        if (await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().RetrieveAsync(grant.Key, surface.Origin, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException("The issued session is no longer active.");
        if (scope.ServiceProvider.GetService<DeviceBrowserService>() is { } devices)
            await devices.ObserveAsync(context, grant, cancellationToken).ConfigureAwait(false);
        return grant.Key;
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket) => throw new InvalidOperationException("Authentication must rotate the session lookup key; implicit renewal is disabled.");
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().RetrieveAsync(key, surface.Origin, RequireContext().RequestAborted).ConfigureAwait(false);
    }
    public async Task RemoveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().RemoveAsync(key, surface.Origin, RequireContext().RequestAborted).ConfigureAwait(false);
    }
    private HttpContext RequireContext() => accessor.HttpContext ?? throw new InvalidOperationException("A User BFF HTTP context is required.");
}
