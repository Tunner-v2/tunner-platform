using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Tunner.Platform.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Identity.Persistence;

/// <summary>
/// Explicit module-factory facade, not automatically registered endpoints. Ordinary calls require the existing
/// User BFF HTTPS/host/Origin/CSRF boundary. Native RecordVerified accepts only terminal adapter-private facts.
/// A cross-site callback never calls Current(), issues a Tunner cookie or fabricates a caller from the ledger.
/// </summary>
public sealed partial class FederationBrowserService
{
    private const string PendingCookie = "__Host-Tunner.Federation.Pending";
    private readonly FederationAttemptApplication application;
    private readonly IFederationProfileSource profiles;
    private readonly IAuthenticationPolicySource authenticationPolicies;
    private readonly AuthenticationPartitionKey partitions;
    private readonly IDataProtectionProvider protection;
    private readonly IAntiforgery antiforgery;
    private readonly TimeProvider clock;
    private readonly string origin;
    private readonly IDataProtector pointerProtection;
    private readonly HashSet<FederationProviderKind> enabledTransports;
    private readonly FederationLinkApplication? management;
    private readonly IRegistrationApplication? registrations;

    internal FederationBrowserService(FederationAttemptApplication application, IFederationProfileSource profiles,
        IAuthenticationPolicySource authenticationPolicies, AuthenticationPartitionKey partitions, IDataProtectionProvider protection,
        IAntiforgery antiforgery, TimeProvider clock, string origin, IReadOnlyCollection<FederationProviderKind> enabledTransports, FederationLinkApplication? management = null, IRegistrationApplication? registrations = null)
    {
        this.application = application; this.profiles = profiles; this.authenticationPolicies = authenticationPolicies;
        this.partitions = partitions; this.protection = protection; this.antiforgery = antiforgery; this.clock = clock; this.origin = origin;
        this.management = management; this.registrations = registrations;
        ArgumentNullException.ThrowIfNull(enabledTransports);
        if (enabledTransports.Count == 0 || enabledTransports.Any(kind => !Enum.IsDefined(kind))) throw new ArgumentException("Explicit enabled transports are required.");
        this.enabledTransports = new(enabledTransports);
        pointerProtection = protection.CreateProtector("Tunner.User.Federation.OriginalAttemptPointer.v2");
    }

    private sealed record Pointer(Guid AttemptId, Guid RequestId, Guid BrowserKey, string BrowserDigest, string Origin, DateTimeOffset ExpiresAt, FederationProviderKind Provider)
    { public override string ToString() => nameof(Pointer); }

    public async Task<FederationBootstrap> BootstrapAsync(HttpContext http, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.RequestAborted);
        var google = Available(FederationProviderKind.Google); var microsoft = Available(FederationProviderKind.Microsoft);
        var registration = RegistrationAvailability();
        var pointer = ReadPointer(http); if (pointer is null) return new(google, microsoft, null, Registration: registration);
        try
        {
            var browser = Current(http); if (!PointerMatches(pointer, browser)) return new(google, microsoft, null, Registration: registration);
            var result = await application.ReadOriginalAsync(browser, pointer.AttemptId, linked.Token, RegistrationCurrent(http,false)).ConfigureAwait(false);
            // Bootstrap only describes the exact original operation. It never emits a cookie or treats session presence as completion.
            var originalResultSession = result.ErrorCode is null && result.Row?.State == "COMPLETED" && result.Authentication?.Grant is { } grant
                ? AuthenticationIdentity.SessionId(grant.Ticket.Principal) : null;
            var preserve = originalResultSession is not null && originalResultSession == AuthenticationIdentity.SessionId(http.User);
            return new(google, microsoft, View(result), preserve, registration);
        }
        catch (ArgumentException) { return new(google, microsoft, null, Registration: registration); }
        catch (InvalidOperationException) { return new(google, microsoft, new(pointer.AttemptId, pointer.RequestId, "UNKNOWN", "DEPENDENCY_UNAVAILABLE"), Registration: registration); }
        catch (CryptographicException) { return new(google, microsoft, null, Registration: registration); }
        catch (Exception) { return new(google, microsoft, new(pointer.AttemptId, pointer.RequestId, "UNKNOWN", "DEPENDENCY_UNAVAILABLE"), Registration: registration); }
    }

    public Task<IResult> StartAsync(HttpContext http, Guid requestId, FederationProviderKind provider, bool rememberMe, CancellationToken ct = default) => Guard(http, async token =>
    {
        if (!enabledTransports.Contains(provider)) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        await RequirePost(http).ConfigureAwait(false);
        var managementPointer = ReadManagementPointer(http);
        if (managementPointer is not null && managementPointer.BrowserExpiresAt > clock.GetUtcNow() && AuthenticationIdentity.SessionId(http.User) == managementPointer.SessionId)
        {
            var currentManagement = ManagementCurrent(http, managementPointer);
            var pending = await application.ReadManagementOriginalAsync(currentManagement.Browser, currentManagement.Caller, managementPointer.AttemptId, token).ConfigureAwait(false);
            if (pending.Row is { State: "PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED" } active && active.ExpiresAt > clock.GetUtcNow())
                return Problem(409, "VERSION_CONFLICT");
        }
        var browser = Current(http); var prior = ReadPointer(http);
        if (prior is not null && PointerMatches(prior, browser) && prior.RequestId != requestId)
        {
            var original = await application.ReadOriginalAsync(browser, prior.AttemptId, token).ConfigureAwait(false);
            if (original.Row is { } active && active.State is ("PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED") && active.ExpiresAt > clock.GetUtcNow())
                return Problem(409, "VERSION_CONFLICT");
        }
        var registrationBrowser = RegistrationAvailable(provider) ? RegistrationCurrent(http,true,requestId) : null;
        var result = await application.StartAsync(browser, requestId, provider, FederationAttemptPurpose.SignIn, rememberMe, token, registrationBrowser).ConfigureAwait(false);
        if (result.Row is { } row && result.ErrorCode is null)
        {
            ClearManagementPointer(http);
            var expires = row.ExpiresAt + TimeSpan.FromTicks(row.ResultRecoveryLifetimeTicks);
            var pointer = new Pointer(row.AttemptId, row.RequestId, row.BrowserKey, row.BrowserDigest, row.Origin, expires, provider);
            http.Response.Cookies.Append(PendingCookie, pointerProtection.Protect(JsonSerializer.Serialize(pointer)), Cookie(expires - clock.GetUtcNow()));
        }
        if (result.ErrorCode is null && result.Row?.State == "PREPARED")
            return Results.Json(new { authenticated = false, operation = View(result), challengePath = provider == FederationProviderKind.Google
                ? "/authentication/federation/google/challenge" : "/authentication/federation/microsoft/challenge" });
        return await Render(http, result).ConfigureAwait(false);
    }, ct);

    public async Task<FederationChallengeAdmission?> AdmitChallengeAsync(HttpContext http, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.RequestAborted);
        // One fixed clean local GET. Original reference comes from the protected Strict pointer, never a URL.
        if (!HttpMethods.IsGet(http.Request.Method) || http.Request.QueryString.HasValue ||
            !StringComparer.Ordinal.Equals(http.Request.Headers["Sec-Fetch-Site"].ToString(), "same-origin")) return null;
        var managementPointer = ReadManagementPointer(http);
        if (managementPointer is not null)
        {
            if (!enabledTransports.Contains(managementPointer.Provider)) return null;
            try
            {
                var actual = ManagementCurrent(http, managementPointer);
                return await application.AdmitChallengeAsync(actual.Browser, managementPointer.AttemptId, linked.Token, actual.Caller).ConfigureAwait(false);
            }
            catch (ArgumentException) { return null; }
        }
        var pointer = ReadPointer(http); if (pointer is null || !enabledTransports.Contains(pointer.Provider)) return null;
        var browser = Current(http); if (!PointerMatches(pointer, browser)) return null;
        return await application.AdmitChallengeAsync(browser, pointer.AttemptId, linked.Token, registrationBrowser: RegistrationCurrent(http,false)).ConfigureAwait(false);
    }

    public Task<FederationCallbackResult> RecordVerifiedAsync(FederationProtocolEvidence evidence, CancellationToken ct = default) =>
        application.RecordVerifiedAsync(evidence, ct);

    public Task<IResult> ReadOriginalAsync(HttpContext http, CancellationToken ct = default) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false); var browser = Current(http); var pointer = ReadPointer(http);
        if (pointer is null || !PointerMatches(pointer, browser)) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        return await Render(http, await application.ReadOriginalAsync(browser, pointer.AttemptId, token, RegistrationCurrent(http,false)).ConfigureAwait(false)).ConfigureAwait(false);
    }, ct);

    public Task<IResult> CompleteSignInAsync(HttpContext http, CancellationToken ct = default) => CompleteSignInAsync(http, null, ct);

    public Task<IResult> CompleteSignInAsync(HttpContext http, FederationRegistrationAcknowledgement? acknowledgement, CancellationToken ct) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false); var browser = Current(http); var pointer = ReadPointer(http);
        if (pointer is null || !PointerMatches(pointer, browser)) return Problem(409, "VERSION_CONFLICT");
        return await Render(http, await application.CompleteSignInAsync(browser, pointer.AttemptId, token, RegistrationCurrent(http,false), acknowledgement).ConfigureAwait(false)).ConfigureAwait(false);
    }, ct);

    public Task<IResult> CancelAsync(HttpContext http, CancellationToken ct = default) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false); var browser = Current(http); var pointer = ReadPointer(http);
        if (pointer is null || !PointerMatches(pointer, browser)) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var result = await application.CancelAsync(browser, pointer.AttemptId, token).ConfigureAwait(false);
        if (result.Row?.State is "REJECTED" or "EXPIRED")
        {
            http.Response.Cookies.Delete(PendingCookie, Cookie());
            return Results.Json(new { authenticated = false, operation = View(result) });
        }
        return await Render(http, result).ConfigureAwait(false);
    }, ct);

    private FederationCurrentBrowser Current(HttpContext http)
    {
        var now = clock.GetUtcNow(); var policy = authenticationPolicies.RequireEffective(now); policy.Validate();
        if (!http.Request.IsHttps || http.Request.QueryString.HasValue ||
            !StringComparer.Ordinal.Equals(http.Request.Scheme + "://" + http.Request.Host.Value, origin) ||
            !AuthenticationEndpoints.TryBrowser(http, AuthenticationEndpoints.Protector(protection), now, out var browser) ||
            browser!.PolicyVersion != policy.Version || http.Connection.RemoteIpAddress is null ||
            http.User.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType != AuthenticationIdentity.Scheme))
            throw new ArgumentException("The original authentication browser context is unavailable.");
        // PreviousSessionId below is observed NOW. The ledger never manufactures AuthenticationCaller values.
        var caller = new AuthenticationCaller(AuthenticationIdentity.Digest(browser.Nonce), partitions.Network(http.Connection.RemoteIpAddress),
            origin, AuthenticationIdentity.SessionId(http.User)); caller.Validate();
        return new(caller, browser.Key, browser.ExpiresAt, policy.Version, browser.PreviousCookieDigest, browser.PreviousSessionId, AuthenticationEndpoints.CookieDigest(http));
    }

    private async Task RequirePost(HttpContext http)
    {
        var origins = http.Request.Headers.Origin;
        if (!HttpMethods.IsPost(http.Request.Method) || origins.Count != 1 || !StringComparer.Ordinal.Equals(origins[0], origin))
            throw new ArgumentException("An original same-origin credential request is required.");
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
    }

    private Pointer? ReadPointer(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(PendingCookie, out var encoded)) return null;
        try
        {
            var pointer = JsonSerializer.Deserialize<Pointer>(pointerProtection.Unprotect(encoded));
            return pointer is not null && Enum.IsDefined(pointer.Provider) && pointer.AttemptId != Guid.Empty && pointer.RequestId != Guid.Empty && pointer.Origin == origin &&
                pointer.ExpiresAt > clock.GetUtcNow() ? pointer : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }
    private static bool PointerMatches(Pointer pointer, FederationCurrentBrowser browser) => pointer.BrowserDigest == browser.Caller.BrowserDigest &&
        pointer.BrowserKey == browser.BrowserKey && pointer.Origin == browser.Caller.Origin;
    private bool Available(FederationProviderKind provider)
    {
        if (!enabledTransports.Contains(provider)) return false;
        try { profiles.RequireCurrent(provider, clock.GetUtcNow()).Require(origin, clock.GetUtcNow()); return true; }
        catch (InvalidOperationException) { return false; }
        catch (ArgumentException) { return false; }
    }
    public async Task<FederationRegistrationIntakeView?> ReadRegistrationIntakeAsync(HttpContext http, CancellationToken ct = default)
    {
        var pointer = ReadPointer(http); if (pointer is null || !RegistrationAvailable(pointer.Provider)) return null;
        if (await application.OriginalPointerIsTerminalAsync(pointer.AttemptId,pointer.BrowserKey,pointer.BrowserDigest,pointer.Origin,ct).ConfigureAwait(false)) return null;
        var registration = RegistrationCurrent(http,false); if (registration is null) return null;
        var browser = Current(http); if (!PointerMatches(pointer,browser)) return null;
        return await application.ReadRegistrationIntakeAsync(browser,registration,pointer.AttemptId,ct).ConfigureAwait(false);
    }

    private bool RegistrationAvailable(FederationProviderKind provider)
    {
        if (provider != FederationProviderKind.Google || registrations is not RegistrationApplication || !enabledTransports.Contains(provider)) return false;
        try { var profile = profiles.RequireCurrent(provider,clock.GetUtcNow()); profile.Require(origin,clock.GetUtcNow()); return profile.Values.Registration is { Complete: true }; }
        catch (InvalidOperationException) { return false; }
    }
    private FederationRegistrationAvailability? RegistrationAvailability()
    {
        if (registrations is not RegistrationApplication) return null;
        var configured = enabledTransports.Any(provider => {
            try { return profiles.RequireCurrent(provider,clock.GetUtcNow()).Values.Registration is not null; }
            catch (InvalidOperationException) { return false; }
        });
        return configured ? new(RegistrationAvailable(FederationProviderKind.Google),false) : null;
    }
    private FederationRegistrationBrowser? RegistrationCurrent(HttpContext http, bool create, Guid? requestId = null) =>
        registrations is RegistrationApplication ? RegistrationEndpoints.FederationBrowser(http,create,requestId) : null;
    private static FederationPendingView? View(FederationAttemptResult result) => result.Row is { } row
        ? new(row.AttemptId, row.RequestId, result.ProjectedState ?? row.State, result.ErrorCode ?? row.ErrorCode, row.Resolution == "REGISTRATION_INTAKE" ? row.Resolution : null) : null;
    private async Task<IResult> Render(HttpContext http, FederationAttemptResult result)
    {
        if (result.Registration is { } registration) return Results.Json(new { authenticated = false, operation = View(result), registration });
        if (result.ErrorCode is null && result.Row is { State: "VERIFIED", Resolution: "REGISTRATION_INTAKE" })
        {
            var policy = http.RequestServices.GetRequiredService<IRegistrationPolicySource>().RequireEffective(clock.GetUtcNow()); policy.Validate();
            return Results.Json(new { authenticated = false, operation = View(result), registrationRequired = true,
                registrationPolicyVersion = policy.Version, policy.TermsVersion, policy.PrivacyVersion });
        }
        if (result.Authentication is { ErrorCode: null } authentication)
        {
            if (authentication.PendingMfa is { } pending) return TotpBrowserService.PendingMfa(http, pending, protection, clock.GetUtcNow());
            if (authentication.Grant is { } grant)
            {
                http.Items[AuthenticationEndpoints.GrantItem] = grant;
                await http.SignInAsync(AuthenticationIdentity.Scheme, grant.Ticket.Principal, grant.Ticket.Properties).ConfigureAwait(false);
                return Results.Json(new { authenticated = true, operation = View(result) });
            }
        }
        if (result.ErrorCode is { } error) return Problem(error is "RATE_OR_ABUSE_LIMITED" ? 429 : error is "VERSION_CONFLICT" ? 409 : 404, error);
        return Results.Json(new { authenticated = false, operation = View(result) });
    }
    private static async Task<IResult> Guard(HttpContext http, Func<CancellationToken, Task<IResult>> action, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.RequestAborted);
        try { return await action(linked.Token).ConfigureAwait(false); }
        catch (ArgumentException) { return Problem(403, "FORBIDDEN_SCOPE"); }
        catch (AntiforgeryValidationException) { return Problem(403, "FORBIDDEN_SCOPE"); }
        catch (Exception)
        {
            // Includes uncertain commit/disposal or response writes: preserve the original pointer; no automatic replay.
            return Problem(503, "DEPENDENCY_UNAVAILABLE");
        }
    }
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    private static CookieOptions Cookie(TimeSpan? maxAge = null) => new()
    { Path = "/", Domain = null, HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = maxAge };
}
