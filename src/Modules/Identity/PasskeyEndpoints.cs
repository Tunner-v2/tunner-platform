using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit User-only composition. This registers no Identity authentication cookie scheme.</summary>
public static class PasskeyEndpoints
{
    public static IServiceCollection AddTunnerPasskeys(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(AuthenticationEndpoints.Surface), typeof(IPasskeyPolicySource), typeof(IPasskeyPresentationSource),
            typeof(IPasskeyRevocationEligibilitySource), typeof(IAssurancePolicySource), typeof(IStepUpPresentationSource)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type))) throw new InvalidOperationException("Passkeys require explicit approved policy, publication, eligibility and shared step-up providers.");
        if (services.Any(x => x.ServiceType == typeof(PasskeyApplication))) throw new InvalidOperationException("Passkeys may be activated only once.");
        services.AddIdentityCore<IdentityUserAdapter>();
        services.AddScoped<PasskeyApplication>(); services.AddScoped<IPasskeyCeremonyMaintenance>(provider => provider.GetRequiredService<PasskeyApplication>()); services.AddScoped<PasskeyBrowserService>();
        var fallback = services.LastOrDefault(x => x.ServiceType == typeof(IUserStepUpOperationSource));
        if (fallback is not null)
        {
            services.Remove(fallback);
            services.Add(new ServiceDescriptor(typeof(PasskeyFallbackOwner), provider => new PasskeyFallbackOwner(
                fallback.ImplementationInstance as IUserStepUpOperationSource ??
                fallback.ImplementationFactory?.Invoke(provider) as IUserStepUpOperationSource ??
                (IUserStepUpOperationSource)ActivatorUtilities.GetServiceOrCreateInstance(provider, fallback.ImplementationType!)), fallback.Lifetime));
        }
        services.AddScoped<IUserStepUpOperationSource, PasskeyStepUpOwner>(); return services;
    }
    public static IEndpointRouteBuilder MapTunnerPasskeys(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var user = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapGet("/account/passkeys", (HttpContext c, PasskeyBrowserService s) => s.ListAsync(c)).RequireAuthorization(user);
        endpoints.MapGet("/account/passkeys/options", (HttpContext c, PasskeyBrowserService s) => s.OptionsAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/passkeys/enrollment/start", (HttpContext c, PasskeyBrowserService s) => s.StartEnrollmentAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/passkeys/enrollment/options", (HttpContext c, PasskeyBrowserService s) => s.EnrollmentOptionsAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/passkeys/enrollment/complete", (HttpContext c, PasskeyBrowserService s) => s.CompleteEnrollmentAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/passkeys/revocation/start", (HttpContext c, PasskeyBrowserService s) => s.StartRevocationAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/passkeys/result", (HttpContext c, PasskeyBrowserService s) => s.ResultAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/passkeys/cancel", (HttpContext c, PasskeyBrowserService s) => s.CancelAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/authentication/passkeys/options", (HttpContext c, PasskeyBrowserService s) => s.AssertionOptionsAsync(c)).AllowAnonymous().WithMetadata(new AuthenticationEndpoints.CredentialAuthenticationMetadata());
        endpoints.MapPost("/account/passkeys/terminal-result", (HttpContext c, PasskeyBrowserService s) => s.TerminalResultAsync(c)).AllowAnonymous();
        endpoints.MapPost("/authentication/passkeys/result", (HttpContext c, PasskeyBrowserService s) => s.AssertionResultAsync(c)).AllowAnonymous();
        endpoints.MapPost("/authentication/passkeys/sign-in", (HttpContext c, PasskeyBrowserService s) => s.SignInAsync(c)).AllowAnonymous().WithMetadata(new AuthenticationEndpoints.CredentialAuthenticationMetadata());
        var assets = endpoints.ServiceProvider.GetRequiredService<IdentityWebAssets>();
        assets.MapAssets(endpoints);
        assets.MapScreens(endpoints, "passkeys.html", ["/account/security", "/account/security/sign-in-methods"],
            static context => context.User.Identity?.IsAuthenticated == true && AuthenticationIdentity.SessionId(context.User) is not null);
        return endpoints;
    }
}
internal sealed class PasskeyFallbackOwner(IUserStepUpOperationSource owner) { internal IUserStepUpOperationSource Owner { get; } = owner; }
internal sealed record PasskeyReadOnlyResult(Guid CeremonyId, Guid RequestId, Guid SessionId, string AccountId, string Origin, DateTimeOffset ExpiresAt);
internal sealed record PasskeySelection(Guid CeremonyId, Guid RequestId, Guid SessionId, string AccountId, string Origin);
internal sealed class PasskeyStepUpOwner(PasskeyApplication application, IdentityPersistenceDbContext database,
    IHttpContextAccessor accessor, IDataProtectionProvider protection, AuthenticationPartitionKey partitions, AuthenticationEndpoints.Surface surface,
    IEnumerable<PasskeyFallbackOwner> fallback) : IUserStepUpOperationOutcomeSource, IUserStepUpBrowserCompletionSource
{
    internal const string CookieName = "__Host-Tunner.Passkey.Operation";
    internal static IDataProtector Protector(IDataProtectionProvider protection) => protection.CreateProtector("Tunner.User.Passkey.Operation.v1");
    internal static void Select(HttpContext context, PasskeyCeremonyRow row, IDataProtectionProvider protection, DateTimeOffset now)
    {
        context.Response.Cookies.Delete(TotpStepUpOwner.CookieName, TotpBrowserService.Cookie());
        context.Response.Cookies.Append(CookieName,
        Protector(protection).Protect(JsonSerializer.Serialize(new PasskeySelection(row.CeremonyId, row.RequestId, row.SessionId!.Value, row.AccountId!, row.Origin))),
        new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = row.ExpiresAt - now });
    }
    internal static PasskeySelection? Selection(HttpContext context, IDataProtectionProvider protection)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie)) return null;
        try { return JsonSerializer.Deserialize<PasskeySelection>(Protector(protection).Unprotect(cookie)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
    }
    internal const string ResultCookieName = "__Host-Tunner.Passkey.Result";
    internal static IDataProtector ResultProtector(IDataProtectionProvider provider) => provider.CreateProtector("Tunner.User.Passkey.ReadOnlyResult.v1");
    public async Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current User BFF context is required.");
        var selected = Selection(http, protection);
        if (!Guid.TryParseExact(operation.Binding.CorrelationId, "N", out var id) || selected?.CeremonyId != id || selected.AccountId != operation.Actor.AccountId || selected.SessionId != operation.Actor.SessionId)
        {
            if (fallback.SingleOrDefault()?.Owner is IUserStepUpBrowserCompletionSource hook) await hook.CompleteBrowserAsync(operation, signInRequired, cancellationToken).ConfigureAwait(false);
            return;
        }
        var ct = cancellationToken;
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == id && x.RequestId == selected.RequestId, ct).ConfigureAwait(false);
        if (row is null || row.State is not ("AUTHORIZED" or "COMPLETED")) return;
        if (signInRequired)
        {
            var result = new PasskeyReadOnlyResult(row.CeremonyId, row.RequestId, row.SessionId!.Value, row.AccountId!, row.Origin, row.RecoveryExpiresAt);
            http.Response.Cookies.Append(ResultCookieName, ResultProtector(protection).Protect(JsonSerializer.Serialize(result)),
                new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true });
        }
        http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict });
    }
    public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current User BFF context is required.");
        if (!http.Request.Cookies.ContainsKey(CookieName)) return fallback.SingleOrDefault() is { } other ? await other.Owner.ResolveAsync(actor, cancellationToken).ConfigureAwait(false) : null;
        var selected = Selection(http, protection);
        if (selected is null || selected.AccountId != actor.AccountId || selected.SessionId != actor.SessionId || selected.Origin != actor.Origin || http.Connection.RemoteIpAddress is null) return null;
        var caller = StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(http.Connection.RemoteIpAddress));
        return await application.ResolveMutationAsync(caller, selected.CeremonyId, selected.RequestId, cancellationToken).ConfigureAwait(false);
    }
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken cancellationToken) =>
        (await ApplyWithOutcomeAsync(actor, operation, context, cancellationToken).ConfigureAwait(false)).Applied;
    public async Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current User BFF context is required.");
        if (context != database) throw new InvalidOperationException("The operation must share its Identity transaction.");
        if (!http.Request.Cookies.ContainsKey(CookieName))
        {
            var other = fallback.SingleOrDefault();
            if (other?.Owner is IUserStepUpOperationOutcomeSource typed) return await typed.ApplyWithOutcomeAsync(actor, operation, context, cancellationToken).ConfigureAwait(false);
            return other is not null && await other.Owner.ApplyAsync(actor, operation, context, cancellationToken).ConfigureAwait(false)
                ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
        }
        var selected = Selection(http, protection);
        if (selected is null || selected.AccountId != actor.AccountId || selected.SessionId != actor.SessionId || selected.Origin != actor.Origin || http.Connection.RemoteIpAddress is null ||
            operation.Binding.CorrelationId != selected.CeremonyId.ToString("N")) return UserStepUpOperationApplyOutcome.Conflict();
        return await application.ApplyMutationAsync(StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(http.Connection.RemoteIpAddress)), operation, http, cancellationToken).ConfigureAwait(false);
    }
}
internal sealed partial class PasskeyBrowserService(PasskeyApplication application, IdentityPersistenceDbContext database,
    IPasskeyPresentationSource presentations, IAuthenticationPolicySource authenticationPolicies, AuthenticationPartitionKey partitions,
    AuthenticationEndpoints.Surface surface, IDataProtectionProvider protection, IAntiforgery antiforgery, TimeProvider clock)
{
    internal Task<IResult> OptionsAsync(HttpContext http) => Guard(http, async () =>
    {
        var policy = application.Current(surface.Origin); PasskeyPresentation presentation;
        try { presentation = presentations.RequirePublished(policy); }
        catch (ArgumentException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        _ = Caller(http);
        var recovery = await RecoveryBootstrapAsync(http, terminalOnly: false).ConfigureAwait(false);
        return Results.Json(new { csrfToken = antiforgery.GetAndStoreTokens(http).RequestToken, requestId = Guid.NewGuid(),
            policy.MaximumCredentialJsonBytes, revocationSessionEffect = policy.RevocationSessionEffect.ToString(), recovery, presentation.Locale, presentation.Content });
    });
    internal PasskeyCapability Capability()
    {
        try { var policy = application.Current(surface.Origin); _ = presentations.RequirePublished(policy); return new(true, policy.MaximumCredentialJsonBytes); }
        catch (InvalidOperationException) { return new(false, null); } catch (ArgumentException) { return new(false, null); }
    }
    // Protected server cookies retain only the exact original workflow. No credential response or client storage is required.
    internal async Task<PasskeyRecoveryView?> RecoveryBootstrapAsync(HttpContext http, bool terminalOnly)
    {
        var now = clock.GetUtcNow(); PasskeyReadOnlyResult? handle = null;
        if (http.Request.Cookies.TryGetValue(PasskeyStepUpOwner.ResultCookieName, out var encoded))
        {
            try { handle = JsonSerializer.Deserialize<PasskeyReadOnlyResult>(PasskeyStepUpOwner.ResultProtector(protection).Unprotect(encoded)); }
            catch (CryptographicException) { } catch (JsonException) { }
        }
        var selected = terminalOnly ? null : PasskeyStepUpOwner.Selection(http, protection);
        var id = selected?.CeremonyId ?? handle?.CeremonyId; var request = selected?.RequestId ?? handle?.RequestId;
        if (id is null || request is null) return null;
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == id && x.RequestId == request && (x.Kind == "ENROLL" || x.Kind == "REVOKE") && x.Origin == surface.Origin,
            http.RequestAborted).ConfigureAwait(false);
        if (row is null || row.RecoveryExpiresAt <= now) return null;
        if (terminalOnly)
        {
            if (handle is null || handle.ExpiresAt != row.RecoveryExpiresAt || handle.AccountId != row.AccountId || handle.SessionId != row.SessionId || handle.Origin != row.Origin ||
                row.Kind != "REVOKE" || row.State != "COMPLETED" || http.User.Identity?.IsAuthenticated == true) return null;
            var ended = await database.Set<AuthenticationSessionRow>().AsNoTracking().AnyAsync(x => x.SessionId == row.SessionId && x.AccountId == row.AccountId && x.State == "REVOKED" &&
                x.TerminationCause == "PASSKEY_REVOKED" && x.OperationId == "OP-PASSKEY-" + row.CeremonyId.ToString("N"), http.RequestAborted).ConfigureAwait(false);
            if (!ended) return null;
        }
        else
        {
            var caller = Caller(http);
            if (row.AccountId != caller.AccountId || row.SessionId != caller.SessionId || row.Origin != caller.Origin) return null;
        }
        var resultHandle = PasskeyStepUpOwner.ResultProtector(protection).Protect(JsonSerializer.Serialize(new PasskeyReadOnlyResult(row.CeremonyId, row.RequestId, row.SessionId!.Value, row.AccountId!, row.Origin, row.RecoveryExpiresAt)));
        var status = row.State is "PENDING" or "AUTHORIZED" && row.ExpiresAt <= now ? "EXPIRED" : row.State;
        return new(row.RequestId, row.CeremonyId, row.Kind, status, row.CredentialReference, row.ExpectedVersion, row.ExpiresAt, resultHandle, terminalOnly);
    }
    internal Task<IResult> ListAsync(HttpContext http) => Guard(http, async () =>
    {
        var caller = Caller(http); var policy = application.Current(caller.Origin);
        await using var transaction = await database.Database.BeginTransactionAsync(http.RequestAborted).ConfigureAwait(false);
        var auth = http.RequestServices.GetRequiredService<AuthenticationApplication>();
        if (await auth.LockForOperationAsync(caller, "LIST_PASSKEYS", http.RequestAborted).ConfigureAwait(false) is null) return Problem(401, "AUTHENTICATION_FAILED");
        var rows = await database.Set<PasskeyCredentialRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId).OrderBy(x => x.CreatedAt)
            .Select(x => new { x.CredentialReference, x.State, x.Version, x.CreatedAt, x.LastUsedAt, x.RevokedAt }).ToArrayAsync(http.RequestAborted).ConfigureAwait(false);
        var currentUser = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, http.RequestAborted).ConfigureAwait(false);
        await database.SaveChangesAsync(http.RequestAborted).ConfigureAwait(false); await transaction.CommitAsync(http.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { credentials = rows, passwordAvailable = currentUser.PasswordHash is not null, enrollmentAvailable = rows.Count(x => x.State == "ACTIVE") < policy.MaximumActiveCredentials,
            revocationRequiresCurrentEligibility = true, protectionFactsAvailable = false, activityAvailable = false });
    });
    internal Task<IResult> StartEnrollmentAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<PasskeyEnrollmentCommand>(http, 4096).ConfigureAwait(false);
        var result = await application.StartMutationAsync(Caller(http), command.RequestId, null, 0, http.RequestAborted).ConfigureAwait(false);
        return SelectResult(http, result);
    });
    internal Task<IResult> StartRevocationAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<PasskeyRevokeCommand>(http, 4096).ConfigureAwait(false);
        if (command.CredentialReference == Guid.Empty || command.ExpectedVersion <= 0) return Problem(400, "VALIDATION_ERROR");
        return SelectResult(http, await application.StartMutationAsync(Caller(http), command.RequestId, command.CredentialReference, command.ExpectedVersion, http.RequestAborted).ConfigureAwait(false));
    });
    internal Task<IResult> EnrollmentOptionsAsync(HttpContext http) => Guard(http, async () =>
    {
        var expected = await Read<PasskeyResultCommand>(http, 4096).ConfigureAwait(false);
        var row = await application.ReadMutationResultAsync(Caller(http), expected.CeremonyId, expected.RequestId, http.RequestAborted).ConfigureAwait(false);
        if (row is null || row.Kind != "ENROLL" || row.State != "AUTHORIZED" || !application.CurrentBinding(row, application.Current(surface.Origin), clock.GetUtcNow())) return Problem(403, "STEP_UP_REQUIRED");
        return CeremonyResponse(row, includeOptions: true);
    });
    internal Task<IResult> CompleteEnrollmentAsync(HttpContext http) => Guard(http, async () =>
    {
        var policy = application.Current(surface.Origin);
        var command = await Read<PasskeyCompleteCommand>(http, checked((long)policy.MaximumCredentialJsonBytes + 4096)).ConfigureAwait(false);
        var result = await application.CompleteEnrollmentAsync(Caller(http), command, http, http.RequestAborted).ConfigureAwait(false);
        return result.ErrorCode is { } error ? Failure(error) : CeremonyResponse(result.Ceremony!);
    });
    internal Task<IResult> ResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<PasskeyResultCommand>(http, 4096).ConfigureAwait(false);
        var row = await application.ReadMutationResultAsync(Caller(http), command.CeremonyId, command.RequestId, http.RequestAborted).ConfigureAwait(false);
        return row is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : CeremonyResponse(row);
    });
    internal Task<IResult> CancelAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<PasskeyResultCommand>(http, 4096).ConfigureAwait(false);
        var row = await application.CancelMutationAsync(Caller(http), command.CeremonyId, command.RequestId, http.RequestAborted).ConfigureAwait(false);
        if (row is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        // Clear only the exact original selection; cancellation cannot switch another tab's pending operation.
        var selected = PasskeyStepUpOwner.Selection(http, protection);
        if (selected?.CeremonyId == row.CeremonyId && selected.RequestId == row.RequestId) http.Response.Cookies.Delete(PasskeyStepUpOwner.CookieName,
            new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict });
        return CeremonyResponse(row);
    });
    internal Task<IResult> TerminalResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<PasskeyTerminalResultCommand>(http, 16384).ConfigureAwait(false);
        var cookie = command.ResultHandle;
        if (string.IsNullOrEmpty(cookie) && !http.Request.Cookies.TryGetValue(PasskeyStepUpOwner.ResultCookieName, out cookie)) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var handle = JsonSerializer.Deserialize<PasskeyReadOnlyResult>(PasskeyStepUpOwner.ResultProtector(protection).Unprotect(cookie));
        if (handle is null || handle.CeremonyId != command.CeremonyId || handle.RequestId != command.RequestId || handle.Origin != surface.Origin || handle.ExpiresAt <= clock.GetUtcNow())
            return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (http.User.Identity?.IsAuthenticated == true && (http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value != handle.AccountId ||
            AuthenticationIdentity.SessionId(http.User) != handle.SessionId)) return Problem(409, "VERSION_CONFLICT");
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == handle.CeremonyId && x.RequestId == handle.RequestId &&
            x.AccountId == handle.AccountId && x.SessionId == handle.SessionId && x.Origin == handle.Origin && x.Kind == "REVOKE" && x.State == "COMPLETED", http.RequestAborted).ConfigureAwait(false);
        if (row is null || row.RecoveryExpiresAt != handle.ExpiresAt) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var ended = await database.Set<AuthenticationSessionRow>().AsNoTracking().AnyAsync(x => x.SessionId == handle.SessionId && x.AccountId == handle.AccountId && x.State == "REVOKED" &&
            x.TerminationCause == "PASSKEY_REVOKED" && x.OperationId == "OP-PASSKEY-" + handle.CeremonyId.ToString("N"), http.RequestAborted).ConfigureAwait(false);
        return ended ? Results.Json(new { status = "COMPLETED", command.RequestId, command.CeremonyId, signInRequired = true }) : Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
    });
    internal Task<IResult> AssertionResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<PasskeyResultCommand>(http, 4096).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var pointer = ReadSignInRecovery(http);
        string? digest = pointer is not null && pointer.RequestId == command.RequestId && pointer.CeremonyId == command.CeremonyId ? pointer.BrowserDigest : null;
        if (digest is null && AuthenticationEndpoints.TryBrowser(http, AuthenticationEndpoints.Protector(protection), now, out var browser)) digest = AuthenticationIdentity.Digest(browser!.Nonce);
        if (digest is null || command.RequestId == Guid.Empty) return Problem(409, "VERSION_CONFLICT");
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == command.CeremonyId && x.RequestId == command.RequestId &&
            x.BrowserDigest == digest && x.Kind == "SIGN_IN" && x.Origin == surface.Origin, http.RequestAborted).ConfigureAwait(false);
        if (row is null || row.RecoveryExpiresAt <= now) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (row.State != "COMPLETED")
        {
            if (http.User.Identity?.IsAuthenticated == true && AuthenticationIdentity.SessionId(http.User) != row.PreviousSessionId) return Problem(409, "VERSION_CONFLICT");
            var originalContext = AuthenticationEndpoints.TryBrowser(http, AuthenticationEndpoints.Protector(protection), now, out var originalBrowser) &&
                AuthenticationIdentity.Digest(originalBrowser!.Nonce) == row.BrowserDigest && originalBrowser.PolicyVersion == authenticationPolicies.RequireEffective(now).Version;
            var status = row.State == "PENDING" && (!originalContext || !application.CurrentBinding(row, application.Current(surface.Origin), now)) ? "EXPIRED" : row.State;
            return Results.Json(new { status, command.RequestId, command.CeremonyId, rememberMe = row.RememberMe });
        }
        if (http.User.Identity?.IsAuthenticated != true) return Results.Json(new { status = "COMPLETED", command.RequestId, command.CeremonyId, authenticated = false, signInRequired = true });
        if (AuthenticationIdentity.SessionId(http.User) != row.ResultSessionId || row.ResultSessionId is not { } resultSession ||
            !await http.RequestServices.GetRequiredService<AuthenticationApplication>().ConfirmOutcomeAsync(digest, row.RequestId, resultSession, http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty, surface.Origin, "passkey", http.RequestAborted).ConfigureAwait(false))
            return Problem(409, "VERSION_CONFLICT");
        return Results.Json(new { status = "COMPLETED", command.RequestId, command.CeremonyId, authenticated = true, signInRequired = false });
    });
    internal Task<IResult> AssertionOptionsAsync(HttpContext http) => Guard(http, async () =>
    {
        var caller = AnonymousCaller(http); var command = await Read<PasskeyAssertionOptionsCommand>(http, 4096).ConfigureAwait(false);
        if (command.RequestId == Guid.Empty) return Problem(409, "VERSION_CONFLICT");
        var original = ReadSignInRecovery(http);
        if (original is not null && original.RequestId == command.RequestId && (original.BrowserDigest != caller.BrowserDigest || original.RememberMe != command.RememberMe)) return Problem(409, "VERSION_CONFLICT");
        var result = await application.StartSignInAsync(caller, command.RequestId, command.RememberMe, http, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error);
        SetSignInRecovery(http, result.Ceremony!);
        return CeremonyResponse(result.Ceremony!, includeOptions: true);
    });
    internal Task<IResult> SignInAsync(HttpContext http) => Guard(http, async () =>
    {
        var caller = AnonymousCaller(http); var policy = application.Current(surface.Origin);
        var command = await Read<PasskeyCompleteCommand>(http, checked((long)policy.MaximumCredentialJsonBytes + 4096)).ConfigureAwait(false);
        if (command.RequestId == Guid.Empty) return Problem(400, "VALIDATION_ERROR");
        var result = await application.CompleteSignInAsync(caller, command, http, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error);
        if (result.PendingMfa is { } pending) return TotpBrowserService.PendingMfa(http, pending, protection, clock.GetUtcNow());
        var grant = result.Grant!; http.Items[AuthenticationEndpoints.GrantItem] = grant;
        await http.SignInAsync(AuthenticationIdentity.Scheme, grant.Ticket.Principal, grant.Ticket.Properties).ConfigureAwait(false);
        return Results.Json(new { status = "COMPLETED", command.RequestId, command.CeremonyId, authenticated = true, signInRequired = false });
    });
    private AuthenticationCaller AnonymousCaller(HttpContext http)
    {
        var now = clock.GetUtcNow(); var policy = authenticationPolicies.RequireEffective(now); policy.Validate();
        if (!AuthenticationEndpoints.TryBrowser(http, AuthenticationEndpoints.Protector(protection), now, out var browser) || browser!.PolicyVersion != policy.Version ||
            browser.PreviousCookieDigest != AuthenticationEndpoints.CookieDigest(http) || http.Connection.RemoteIpAddress is null) throw new ArgumentException("The authentication browser transaction changed.");
        return new(AuthenticationIdentity.Digest(browser.Nonce), partitions.Network(http.Connection.RemoteIpAddress), surface.Origin, browser.PreviousSessionId);
    }
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin,
        partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("A real peer is required.")));
    private IResult SelectResult(HttpContext http, PasskeyResult result)
    {
        if (result.ErrorCode is { } error) return Failure(error);
        var row = result.Ceremony!;
        if (row.State is "PENDING" or "AUTHORIZED" && row.ExpiresAt > clock.GetUtcNow()) PasskeyStepUpOwner.Select(http, row, protection, clock.GetUtcNow());
        var handle = PasskeyStepUpOwner.ResultProtector(protection).Protect(JsonSerializer.Serialize(new PasskeyReadOnlyResult(row.CeremonyId, row.RequestId, row.SessionId!.Value,
            row.AccountId!, row.Origin, row.RecoveryExpiresAt)));
        http.Response.Cookies.Append(PasskeyStepUpOwner.ResultCookieName, handle, new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true });
        return CeremonyResponse(row, resultHandle: handle);
    }
    private IResult CeremonyResponse(PasskeyCeremonyRow row, bool includeOptions = false, string? resultHandle = null) => Results.Json(new
    {
        row.CeremonyId, row.RequestId, resultHandle, status = row.State is "PENDING" or "AUTHORIZED" && row.ExpiresAt <= clock.GetUtcNow() ? "EXPIRED" : row.State, row.Kind, row.CredentialReference, row.ExpectedVersion, row.ExpiresAt,
        publicKey = includeOptions && row.OptionsJson.Length != 0 && row.ExpiresAt > clock.GetUtcNow() ? JsonSerializer.Deserialize<JsonElement>(row.OptionsJson) : (JsonElement?)null
    });
    private static Task<T> Read<T>(HttpContext http, long maximum) where T : class
    { if (!http.Request.HasJsonContentType()) throw new BadHttpRequestException("JSON is required.", 415); return IdentityJsonCommand.ReadAsync<T>(http, maximum); }
    private static IResult Failure(string error) => Problem(error switch { "AUTHENTICATION_FAILED" => 401, "RATE_OR_ABUSE_LIMITED" => 429,
        "VERSION_CONFLICT" => 409, "STEP_UP_REQUIRED" => 403, _ => 400 }, error);
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    private static async Task<IResult> Guard(HttpContext context, Func<Task<IResult>> action)
    {
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
        try { return await action().ConfigureAwait(false); }
        catch (AssurancePolicyVersionConflictException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException e) { return Problem(e.StatusCode is 413 or 415 ? e.StatusCode : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
}
