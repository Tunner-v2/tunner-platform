using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

public static class TotpEndpoints
{
    public static IServiceCollection AddTunnerTotp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(AuthenticationEndpoints.Surface), typeof(ITotpPolicySource), typeof(ITotpPresentationSource), typeof(ITotpSeedStore),
            typeof(ITotpMutationEligibilitySource), typeof(ITotpMfaRequirementSource), typeof(IAssurancePolicySource), typeof(IStepUpPresentationSource)];
        if (required.Any(t => !services.Any(x => x.ServiceType == t))) throw new InvalidOperationException("TOTP requires explicit approved policy, presentation, provider, eligibility, MFA and shared proof owners.");
        if (services.Any(x => x.ServiceType == typeof(TotpApplication))) throw new InvalidOperationException("TOTP may be activated only once.");
        services.AddIdentityCore<IdentityUserAdapter>(); services.AddScoped<TotpApplication>(); services.AddScoped<TotpNativeVerifier>(); services.AddScoped<TotpBrowserService>();
        var fallback = services.LastOrDefault(x => x.ServiceType == typeof(IUserStepUpOperationSource));
        if (fallback is not null)
        {
            services.Remove(fallback);
            services.Add(new ServiceDescriptor(typeof(TotpFallbackOwner), p => new TotpFallbackOwner(fallback.ImplementationInstance as IUserStepUpOperationSource ??
                fallback.ImplementationFactory?.Invoke(p) as IUserStepUpOperationSource ?? (IUserStepUpOperationSource)ActivatorUtilities.GetServiceOrCreateInstance(p, fallback.ImplementationType!)), fallback.Lifetime));
        }
        services.AddScoped<IUserStepUpOperationSource, TotpStepUpOwner>(); return services;
    }
    public static IEndpointRouteBuilder MapTunnerTotp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints); var user = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapGet("/account/totp", (HttpContext c, TotpBrowserService s) => s.ListAsync(c)).RequireAuthorization(user);
        endpoints.MapGet("/account/totp/options", (HttpContext c, TotpBrowserService s) => s.OptionsAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/totp/enrollment/start", (HttpContext c, TotpBrowserService s) => s.StartAsync(c, "ENROLL")).RequireAuthorization(user);
        endpoints.MapPost("/account/totp/disable/start", (HttpContext c, TotpBrowserService s) => s.StartAsync(c, "DISABLE")).RequireAuthorization(user);
        endpoints.MapPost("/account/totp/reset/start", (HttpContext c, TotpBrowserService s) => s.StartAsync(c, "RESET")).RequireAuthorization(user);
        endpoints.MapPost("/account/totp/enrollment/disclose", (HttpContext c, TotpBrowserService s) => s.DiscloseAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/totp/enrollment/activate", (HttpContext c, TotpBrowserService s) => s.ActivateAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/totp/result", (HttpContext c, TotpBrowserService s) => s.ResultAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/totp/terminal-result", (HttpContext c, TotpBrowserService s) => s.TerminalResultAsync(c)).AllowAnonymous();
        endpoints.MapGet("/authentication/totp/options", (HttpContext c, TotpBrowserService s) => s.MfaOptionsAsync(c)).AllowAnonymous();
        endpoints.MapPost("/authentication/totp/complete", (HttpContext c, TotpBrowserService s) => s.CompleteMfaAsync(c)).AllowAnonymous().WithMetadata(new AuthenticationEndpoints.CredentialAuthenticationMetadata());
        endpoints.MapPost("/authentication/totp/result", (HttpContext c, TotpBrowserService s) => s.MfaResultAsync(c)).AllowAnonymous().WithMetadata(new AuthenticationEndpoints.CredentialAuthenticationMetadata());
        return endpoints;
    }
}
internal sealed class TotpFallbackOwner(IUserStepUpOperationSource owner) { internal IUserStepUpOperationSource Owner { get; } = owner; }
internal sealed record TotpSelection(Guid EnrollmentId, Guid RequestId, string AccountId, Guid SessionId, string Origin, DateTimeOffset ExpiresAt);
internal sealed class TotpStepUpOwner(TotpApplication application, IdentityPersistenceDbContext database, IHttpContextAccessor accessor,
    AuthenticationPartitionKey partitions, AuthenticationEndpoints.Surface surface, IDataProtectionProvider protection, IEnumerable<TotpFallbackOwner> fallback)
    : IUserStepUpOperationOutcomeSource, IUserStepUpBrowserCompletionSource
{
    internal const string CookieName = "__Host-Tunner.Totp.Operation";
    internal const string ResultCookieName = "__Host-Tunner.Totp.Result";
    internal static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Tunner.User.Totp.Operation.v1");
    internal static TotpSelection? Selection(HttpContext context, IDataProtectionProvider provider, string name = CookieName)
    {
        if (!context.Request.Cookies.TryGetValue(name, out var value)) return null;
        try { return JsonSerializer.Deserialize<TotpSelection>(Protector(provider).Unprotect(value)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
    }
    internal static void Select(HttpContext context, TotpOperationRow row, IDataProtectionProvider provider, DateTimeOffset now)
    {
        var selected = new TotpSelection(row.EnrollmentId, row.RequestId, row.AccountId, row.SessionId, row.Origin, row.RecoveryExpiresAt);
        var value = Protector(provider).Protect(JsonSerializer.Serialize(selected));
        context.Response.Cookies.Append(CookieName, value, TotpBrowserService.Cookie(row.RecoveryExpiresAt - now));
        context.Response.Cookies.Append(ResultCookieName, value, TotpBrowserService.Cookie(row.RecoveryExpiresAt - now));
        context.Response.Cookies.Delete(PasskeyStepUpOwner.CookieName, TotpBrowserService.Cookie());
    }
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("Current peer required.")));
    public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current BFF context required.");
        if (!http.Request.Cookies.ContainsKey(CookieName)) return fallback.SingleOrDefault() is { } other ? await other.Owner.ResolveAsync(actor, ct).ConfigureAwait(false) : null;
        var selected = Selection(http, protection);
        return selected is not null && selected.AccountId == actor.AccountId && selected.SessionId == actor.SessionId && selected.Origin == actor.Origin
            ? await application.ResolveAsync(Caller(http), selected.EnrollmentId, selected.RequestId, ct).ConfigureAwait(false) : null;
    }
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct) =>
        (await ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false)).Applied;
    public async Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct)
    {
        if (context != database) throw new InvalidOperationException("Shared Identity transaction required.");
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current BFF context required.");
        if (!http.Request.Cookies.ContainsKey(CookieName))
        {
            var other = fallback.SingleOrDefault()?.Owner;
            return other is IUserStepUpOperationOutcomeSource typed ? await typed.ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false) :
                other is not null && await other.ApplyAsync(actor, operation, context, ct).ConfigureAwait(false) ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
        }
        var selected = Selection(http, protection);
        return selected is not null && selected.AccountId == actor.AccountId && selected.SessionId == actor.SessionId && selected.Origin == actor.Origin && operation.Binding.CorrelationId == selected.EnrollmentId.ToString("N")
            ? await application.ApplyAsync(Caller(http), operation, ct).ConfigureAwait(false) : UserStepUpOperationApplyOutcome.Conflict();
    }
    public async Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current BFF context required."); var selected = Selection(http, protection);
        if (selected is null || operation.Binding.CorrelationId != selected.EnrollmentId.ToString("N"))
        {
            if (fallback.SingleOrDefault()?.Owner is IUserStepUpBrowserCompletionSource hook) await hook.CompleteBrowserAsync(operation, signInRequired, ct).ConfigureAwait(false); return;
        }
        http.Response.Cookies.Append(ResultCookieName, Protector(protection).Protect(JsonSerializer.Serialize(selected)), TotpBrowserService.Cookie(selected.ExpiresAt - DateTimeOffset.UtcNow));
        // Keep the exact selected result available to shared owner-failure reconciliation.
    }
}
internal sealed record TotpMfaSelection(Guid PendingId, Guid RequestId, string BrowserDigest, string Origin, Guid? PreviousSessionId, DateTimeOffset ExpiresAt);
internal sealed class TotpBrowserService(TotpApplication application, AuthenticationApplication authentication, IdentityPersistenceDbContext database,
    ITotpPresentationSource presentations, ITotpSeedStore seeds, TotpNativeVerifier verifier, AuthenticationPartitionKey partitions,
    AuthenticationEndpoints.Surface surface, IDataProtectionProvider protection, IAntiforgery antiforgery, TimeProvider clock)
{
    internal const string MfaCookieName = "__Host-Tunner.Totp.Pending";
    internal static CookieOptions Cookie(TimeSpan? age = null) => new() { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = age };
    private static IDataProtector MfaProtector(IDataProtectionProvider provider) => provider.CreateProtector("Tunner.User.Totp.PendingAuthentication.v1");
    internal static IResult PendingMfa(HttpContext http, TotpPendingAuthenticationRow pending, IDataProtectionProvider provider, DateTimeOffset now)
    {
        var selected = new TotpMfaSelection(pending.PendingId, pending.RequestId, pending.BrowserDigest, pending.Origin, pending.PreviousSessionId, pending.RecoveryExpiresAt);
        http.Response.Cookies.Append(MfaCookieName, MfaProtector(provider).Protect(JsonSerializer.Serialize(selected)), Cookie(pending.RecoveryExpiresAt - now));
        return Results.Json(new { status = "MFA_REQUIRED", authenticated = false, pending.RequestId, pending.ExpiresAt });
    }
    internal Task<IResult> OptionsAsync(HttpContext http) => Guard(http, async () =>
    {
        _ = Caller(http); var policy = application.Current(surface.Origin); var presentation = presentations.RequirePublished(policy);
        var selection = TotpStepUpOwner.Selection(http, protection); TotpOperationRow? recovery = null;
        if (selection is not null) recovery = await application.ReadAsync(Caller(http), selection.EnrollmentId, selection.RequestId, http.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { csrfToken = antiforgery.GetAndStoreTokens(http).RequestToken, requestId = Guid.NewGuid(), policy.Version, policy.MaximumActiveAuthenticators,
            policy.MaximumPendingOperations, disableSessionEffect = policy.DisableSessionEffect.ToString(), resetSessionEffect = policy.ResetSessionEffect.ToString(),
            resetBehavior = policy.ResetBehavior.ToString(), replacementOrdering = policy.ReplacementOrdering.ToString(), recovery = recovery is null ? null : View(recovery), presentation.Locale, presentation.Content });
    });
    internal Task<IResult> ListAsync(HttpContext http) => Guard(http, async () =>
    {
        var caller = Caller(http); _ = application.Current(caller.Origin);
        await using var transaction = await database.Database.BeginTransactionAsync(http.RequestAborted).ConfigureAwait(false);
        if (await authentication.LockForOperationAsync(caller, "LIST_TOTP", http.RequestAborted).ConfigureAwait(false) is null) return Failure("AUTHENTICATION_FAILED");
        var values = await database.Set<TotpAuthenticatorRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId).OrderBy(x => x.CreatedAt)
            .Select(x => new { x.AuthenticatorReference, x.State, x.Version, x.CreatedAt, x.ActivatedAt, x.LastUsedAt, x.DisabledAt }).ToArrayAsync(http.RequestAborted).ConfigureAwait(false);
        await database.SaveChangesAsync(http.RequestAborted).ConfigureAwait(false); await transaction.CommitAsync(http.RequestAborted).ConfigureAwait(false); return Results.Json(new { authenticators = values });
    });
    internal Task<IResult> StartAsync(HttpContext http, string kind) => Guard(http, async () =>
    {
        var command = await Read<TotpStartCommand>(http).ConfigureAwait(false); var result = await application.StartAsync(Caller(http), kind, command, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error); TotpStepUpOwner.Select(http, result.Operation!, protection, clock.GetUtcNow()); return Results.Json(View(result.Operation!));
    });
    internal Task<IResult> DiscloseAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<TotpOperationCommand>(http).ConfigureAwait(false); var result = await application.DiscloseAsync(Caller(http), command, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error);
        return Results.Json(new { status = "AWAITING_FIRST_CODE", command.RequestId, command.EnrollmentId, manualKey = result.Disclosure!.Key, otpAuthUri = result.Disclosure.Uri, result.Operation!.ExpiresAt });
    });
    internal Task<IResult> ActivateAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<TotpCodeCommand>(http).ConfigureAwait(false); var result = await application.ActivateAsync(Caller(http), command, http.RequestAborted).ConfigureAwait(false);
        return result.ErrorCode is { } error ? Failure(error) : Results.Json(View(result.Operation!));
    });
    internal Task<IResult> ResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<TotpOperationCommand>(http).ConfigureAwait(false); var row = await application.ReadAsync(Caller(http), command.EnrollmentId, command.RequestId, http.RequestAborted).ConfigureAwait(false);
        return row is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : Results.Json(View(row));
    });
    internal Task<IResult> TerminalResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<TotpOperationCommand>(http).ConfigureAwait(false); var selected = TotpStepUpOwner.Selection(http, protection, TotpStepUpOwner.ResultCookieName);
        if (selected is null || selected.EnrollmentId != command.EnrollmentId || selected.RequestId != command.RequestId || selected.Origin != surface.Origin || selected.ExpiresAt <= clock.GetUtcNow()) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var row = await database.Set<TotpOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EnrollmentId == selected.EnrollmentId && x.RequestId == selected.RequestId && x.AccountId == selected.AccountId && x.SessionId == selected.SessionId && x.Origin == selected.Origin && x.State == "COMPLETED" && (x.Kind == "DISABLE" || x.Kind == "RESET"), http.RequestAborted).ConfigureAwait(false);
        if (row is null || row.RecoveryExpiresAt != selected.ExpiresAt || !await database.Set<AuthenticationSessionRow>().AsNoTracking().AnyAsync(x => x.SessionId == selected.SessionId && x.AccountId == selected.AccountId && x.State == "REVOKED" && x.TerminationCause == "TOTP_DISABLED" && x.OperationId == "OP-TOTP-" + row.EnrollmentId.ToString("N"), http.RequestAborted).ConfigureAwait(false)) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        return Results.Json(new { operation = View(row), signInRequired = true });
    });
    private TotpMfaSelection? MfaSelection(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(MfaCookieName, out var value)) return null;
        try { var row = JsonSerializer.Deserialize<TotpMfaSelection>(MfaProtector(protection).Unprotect(value)); return row?.Origin == surface.Origin && row.ExpiresAt > clock.GetUtcNow() ? row : null; }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
    }
    internal Task<IResult> MfaOptionsAsync(HttpContext http) => Guard(http, async () =>
    {
        var selected = MfaSelection(http); if (selected is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var pending = await database.Set<TotpPendingAuthenticationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.PendingId == selected.PendingId && x.RequestId == selected.RequestId && x.BrowserDigest == selected.BrowserDigest && x.Origin == selected.Origin, http.RequestAborted).ConfigureAwait(false);
        if (pending is null || pending.RecoveryExpiresAt != selected.ExpiresAt) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var policy = application.Current(surface.Origin); var presentation = presentations.RequirePublished(policy);
        var factors = await database.Set<TotpAuthenticatorRow>().AsNoTracking().Where(x => x.AccountId == pending.AccountId && x.State == "ACTIVE").Select(x => new { x.AuthenticatorReference }).ToArrayAsync(http.RequestAborted).ConfigureAwait(false);
        var prior = http.User; string? csrf;
        try { http.User = new ClaimsPrincipal(new ClaimsIdentity()); csrf = antiforgery.GetAndStoreTokens(http).RequestToken; } finally { http.User = prior; }
        return Results.Json(new { status = pending.State == "PENDING" && pending.ExpiresAt <= clock.GetUtcNow() ? "EXPIRED" : pending.State, pending.RequestId, pending.ExpiresAt, authenticators = factors, csrfToken = csrf, presentation.Locale, presentation.Content });
    });
    internal Task<IResult> CompleteMfaAsync(HttpContext http) => Guard(http, async () =>
    {
        var selected = MfaSelection(http); if (selected is null || http.Connection.RemoteIpAddress is null) return Problem(403, "FORBIDDEN_SCOPE");
        if (!AuthenticationEndpoints.TryBrowser(http, AuthenticationEndpoints.Protector(protection), clock.GetUtcNow(), out var browser) || AuthenticationIdentity.Digest(browser!.Nonce) != selected.BrowserDigest)
            return Problem(403, "FORBIDDEN_SCOPE");
        var command = await Read<TotpCodeCommand>(http).ConfigureAwait(false); if (command.RequestId != selected.RequestId) return Failure("VERSION_CONFLICT");
        var caller = new AuthenticationCaller(selected.BrowserDigest, partitions.Network(http.Connection.RemoteIpAddress), surface.Origin, selected.PreviousSessionId);
        var result = await authentication.CompleteTotpMfaAsync(caller, selected.PendingId, command, seeds, verifier, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error);
        var grant = result.Grant!; http.Items[AuthenticationEndpoints.GrantItem] = grant;
        await http.SignInAsync(AuthenticationIdentity.Scheme, grant.Ticket.Principal, grant.Ticket.Properties).ConfigureAwait(false);
        return Results.Json(new { status = "COMPLETED", authenticated = true, command.RequestId });
    });
    internal Task<IResult> MfaResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var selected = MfaSelection(http); if (selected is null || http.Connection.RemoteIpAddress is null) return Problem(403, "FORBIDDEN_SCOPE");
        if (!AuthenticationEndpoints.TryBrowser(http, AuthenticationEndpoints.Protector(protection), clock.GetUtcNow(), out var browser) || AuthenticationIdentity.Digest(browser!.Nonce) != selected.BrowserDigest)
            return Problem(403, "FORBIDDEN_SCOPE");
        var command = await Read<TotpMfaResultCommand>(http).ConfigureAwait(false); if (command.RequestId != selected.RequestId) return Failure("VERSION_CONFLICT");
        var caller = new AuthenticationCaller(selected.BrowserDigest, partitions.Network(http.Connection.RemoteIpAddress), surface.Origin, selected.PreviousSessionId);
        var result = await authentication.RecoverCompletedTotpMfaAsync(caller, selected.PendingId, command.RequestId, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error);
        var grant = result.Grant!; http.Items[AuthenticationEndpoints.GrantItem] = grant;
        await http.SignInAsync(AuthenticationIdentity.Scheme, grant.Ticket.Principal, grant.Ticket.Properties).ConfigureAwait(false);
        return Results.Json(new { status = "COMPLETED", authenticated = true, command.RequestId });
    });
    private object View(TotpOperationRow row) => new { row.EnrollmentId, row.RequestId, row.Kind, row.AuthenticatorReference, row.ExpectedVersion,
        status = row.EndedAt is null && row.ExpiresAt <= clock.GetUtcNow() ? "EXPIRED" : row.State, row.ExpiresAt,
        disclosureAvailable = row.DisclosureClaimedAt is null && row.State is "AUTHORIZED" or "PREPARING", disclosureConsumed = row.DisclosureClaimedAt is not null,
        activationCompleted = row.Kind == "ENROLL" && row.State == "COMPLETED" };
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("Current peer required.")));
    private static Task<T> Read<T>(HttpContext http) where T : class { if (!http.Request.HasJsonContentType()) throw new BadHttpRequestException("JSON required.", 415); return IdentityJsonCommand.ReadAsync<T>(http, 4096); }
    private static IResult Failure(string error) => Problem(error switch { "AUTHENTICATION_FAILED" => 401, "VERSION_CONFLICT" or "DISCLOSURE_UNAVAILABLE" => 409, "STEP_UP_REQUIRED" => 403,
        "RATE_OR_ABUSE_LIMITED" or "REPLAY_CAPACITY_EXHAUSTED" or "VERIFICATION_BUDGET_EXHAUSTED" => 429, "DEPENDENCY_UNAVAILABLE" or "CLOCK_OUT_OF_BOUNDS" or "PREPARATION_UNKNOWN" => 503, _ => 400 }, error);
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.", new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    private static async Task<IResult> Guard(HttpContext http, Func<Task<IResult>> action)
    {
        http.Response.Headers.CacheControl = "no-store"; http.Response.Headers.Pragma = "no-cache"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (http.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
        try { return await action().ConfigureAwait(false); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException ex) { return Problem(ex.StatusCode == 413 ? 413 : ex.StatusCode == 415 ? 415 : 400, "VALIDATION_ERROR"); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (AssurancePolicyVersionConflictException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
}
