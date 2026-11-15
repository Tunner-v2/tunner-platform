using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

public static class SessionRevocationBrowserEndpoints
{
    internal const string Root = "/account/sessions/revocation";
    internal const string CookieName = "__Host-Tunner.SessionRevocation";
    public static IServiceCollection AddTunnerSessionRevocation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(ISessionListingHttpBudgetSource), typeof(ISessionRevocationPresentationSource),
            typeof(IAssurancePolicySource), typeof(IStepUpPresentationSource)];
        if (required.Any(t => !services.Any(x => x.ServiceType == t)))
            throw new InvalidOperationException("Session revocation requires explicit listing, publication and assurance sources.");
        if (services.Any(x => x.ServiceType == typeof(SessionRevocationBrowserService)))
            throw new InvalidOperationException("Session revocation may be activated only once.");
        services.TryAddScoped<IUserStepUpOperationSource, SessionRevocationEmptyOwner>();
        services.AddScoped<SessionRevocationBrowserService>(); return services;
    }
    internal static void MapIfConfigured(IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(SessionRevocationBrowserService))) return;
        var user = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapGet(Root + "/options", (HttpContext c, SessionRevocationBrowserService s) => s.OptionsAsync(c)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/prepare", (HttpContext c, SessionRevocationBrowserService s) => s.PrepareAsync(c)).RequireAuthorization(user);
        endpoints.MapGet(Root + "/context", (HttpContext c, SessionRevocationBrowserService s) => s.ContextAsync(c)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/result", (HttpContext c, SessionRevocationBrowserService s) => s.ResultAsync(c)).RequireAuthorization(user);
    }
}

internal sealed class SessionRevocationEmptyOwner : IUserStepUpOperationSource
{
    public Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken cancellationToken) => Task.FromResult<UserStepUpOperationContext?>(null);
    public Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext database, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

/// <summary>Per-request routing adapter, preserving the registered/decorated owner's exact behavior.</summary>
internal sealed class SessionRevocationOwnerRouter(IUserStepUpOperationSource fallback, SessionRevocationBrowserService? sessions,
    SessionBulkRevocationBrowserService? bulk = null)
    : IUserStepUpOperationOutcomeSource, IUserStepUpBrowserCompletionSource
{
    internal static IUserStepUpOperationSource Wrap(IUserStepUpOperationSource fallback, SessionRevocationBrowserService? sessions)
        => sessions is null ? fallback : new SessionRevocationOwnerRouter(fallback, sessions);
    internal static IUserStepUpOperationSource Wrap(IUserStepUpOperationSource fallback, SessionRevocationBrowserService? sessions,
        SessionBulkRevocationBrowserService? bulk) => sessions is null && bulk is null ? fallback : new SessionRevocationOwnerRouter(fallback, sessions, bulk);
    public Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken cancellationToken)
        => bulk?.IsSelected(actor) == true ? bulk.ResolveAsync(actor, fallback, cancellationToken)
            : sessions?.IsSelected(actor) == true ? sessions.ResolveAsync(actor, fallback, cancellationToken)
            : fallback.ResolveAsync(actor, cancellationToken);
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext database, CancellationToken cancellationToken)
    { using var result = await ApplyWithOutcomeAsync(actor, operation, database, cancellationToken).ConfigureAwait(false); return result.Applied; }
    public Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext database, CancellationToken cancellationToken)
    {
        if (SessionRevocationApplication.Owns(operation.Binding.Scope) || SessionBulkRevocationApplication.Owns(operation.Binding.Scope))
            throw new InvalidOperationException("Session revocation requires typed pre-transaction dispatch.");
        return fallback is IUserStepUpOperationOutcomeSource typed ? typed.ApplyWithOutcomeAsync(actor, operation, database, cancellationToken)
            : ApplyFallbackAsync(actor, operation, database, cancellationToken);
    }
    private async Task<UserStepUpOperationApplyOutcome> ApplyFallbackAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext database, CancellationToken ct)
        => await fallback.ApplyAsync(actor, operation, database, ct).ConfigureAwait(false) ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
    public Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken cancellationToken)
        => !SessionRevocationApplication.Owns(operation.Binding.Scope) && !SessionBulkRevocationApplication.Owns(operation.Binding.Scope) && fallback is IUserStepUpBrowserCompletionSource hook
            ? hook.CompleteBrowserAsync(operation, signInRequired, cancellationToken) : Task.CompletedTask;
}

internal sealed class SessionRevocationBrowserService(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    StepUpApplication stepUp, IUserStepUpOperationSource fallback, IAssurancePolicySource assurancePolicies,
    IAuthenticationPolicySource authenticationPolicies, ISessionRevocationPresentationSource presentations,
    AuthenticationPasswordWork passwordWork, AuthenticationEndpoints.Surface surface, AuthenticationPartitionKey partitions,
    IDataProtectionProvider protection, IHttpContextAccessor accessor, TimeProvider clock)
{
    private readonly SessionRevocationApplication application = new(authentication, stepUp);
    private readonly IDataProtector protector = protection.CreateProtector("Tunner.User.SessionRevocation.Original.v1");
    private sealed record Original(string AccountId, Guid ActorSessionId, string Origin, Guid RequestId, Guid TargetReference,
        long ExpectedVersion, DateTimeOffset LastSeenAt, string CorrelationId, string AuthenticationVersion, string AssuranceVersion,
        DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string? FallbackDigest)
    { public override string ToString() => nameof(Original); }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class PrepareCommand
    {
        public required Guid RequestId { get; init; }
        public required Guid TargetReference { get; init; }
        public required string ExpectedVersion { get; init; }
        public override string ToString() => nameof(PrepareCommand);
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class ResultCommand
    { public required Guid RequestId { get; init; } public override string ToString() => nameof(ResultCommand); }
    private sealed record State(Original Original, StepUpCaller Caller, SessionRevocationRequest Request, string Status);
    private StepUpCaller Caller(HttpContext http) => http.Connection.RemoteIpAddress is { } peer
        ? StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(peer))
        : throw new ArgumentException("A current authenticated peer is required.");
    private SessionRevocationPresentation Publication()
    {
        try
        {
            var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
            return presentations.RequirePublished(auth) ?? throw new InvalidOperationException("Session revocation publication is unavailable.");
        }
        catch (ArgumentException) { throw new InvalidOperationException("Session revocation publication is unavailable."); }
    }
    internal Task<IResult> OptionsAsync(HttpContext http) => Guard(http, () =>
    {
        _ = Caller(http); var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
        // Published copy is capability discovery, not a grant to revoke any target.
        var published = Publication();
        return Task.FromResult(auth == authenticationPolicies.RequireEffective(clock.GetUtcNow())
            ? Results.Json(new { published.Locale, published.Content }) : Problem(503, "DEPENDENCY_UNAVAILABLE"));
    });
    internal Task<IResult> PrepareAsync(HttpContext http) => Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<PrepareCommand>(http, 4096).ConfigureAwait(false);
        if (!long.TryParse(command.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1 ||
            version.ToString(CultureInfo.InvariantCulture) != command.ExpectedVersion) return Problem(400, "VALIDATION_ERROR");
        var caller = Caller(http); _ = Publication(); var now = clock.GetUtcNow();
        var existing = ReadOriginal(http, caller, false);
        if (existing is not null && existing.RequestId == command.RequestId)
        {
            if (existing.TargetReference != command.TargetReference || existing.ExpectedVersion != version) return Problem(409, "VERSION_CONFLICT");
            var prior = await CurrentAsync(http, caller, http.RequestAborted).ConfigureAwait(false);
            return prior is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : View(prior);
        }
        var correlation = "COR-SESSION-REVOKE-" + Guid.NewGuid().ToString("N");
        var request = new SessionRevocationRequest(command.RequestId, command.TargetReference, version, correlation);
        if (await application.PrepareAsync(caller, request, http.RequestAborted).ConfigureAwait(false) is null) return Problem(409, "VERSION_CONFLICT");
        var target = await database.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == command.TargetReference, http.RequestAborted).ConfigureAwait(false);
        if (target.Version != version) return Problem(409, "VERSION_CONFLICT");
        now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        var assurance = assurancePolicies.RequireEffective(SessionRevocationBinding.Scope, now);
        var expires = now + auth.BrowserContextLifetime;
        if (assurance.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
        var other = await fallback.ResolveAsync(new(caller), http.RequestAborted).ConfigureAwait(false);
        var original = new Original(caller.AccountId, caller.SessionId, caller.Origin, command.RequestId, command.TargetReference, version,
            target.LastSeenAt, correlation, auth.Version, assurance.Metadata.VersionId, now, expires,
            other is null ? null : StepUpBrowserService.Digest(other));
        var finalAt = clock.GetUtcNow();
        if (finalAt >= expires || authenticationPolicies.RequireEffective(finalAt) != auth ||
            assurancePolicies.RequireEffective(SessionRevocationBinding.Scope, finalAt, assurance.Metadata.VersionId).Metadata.VersionId != assurance.Metadata.VersionId)
            return Problem(409, "VERSION_CONFLICT");
        var selection = SessionRevocationOriginalSelection.Create("SINGLE", original.RequestId, Operation(caller, original), auth.Version, now, expires);
        var selectedCookie = SessionRevocationOriginalSelection.Protect(protection, selection);
        if (!SessionRevocationOriginalSelection.Fits(SessionRevocationOriginalSelection.CookieName, selectedCookie, Cookie(expires - finalAt), 4096))
            return Problem(503, "DEPENDENCY_UNAVAILABLE");
        http.Response.Cookies.Append(SessionRevocationBrowserEndpoints.CookieName, protector.Protect(JsonSerializer.Serialize(original)), Cookie(expires - finalAt));
        http.Response.Cookies.Append(SessionRevocationOriginalSelection.CookieName, selectedCookie, Cookie(expires - finalAt));
        http.Response.Cookies.Delete(StepUpBrowserEndpoints.CookieName, Cookie(TimeSpan.Zero));
        return View(new(original, caller, request, "PENDING"));
    });
    internal Task<IResult> ContextAsync(HttpContext http) => Guard(http, async () =>
    {
        var state = await CurrentAsync(http, Caller(http), http.RequestAborted).ConfigureAwait(false);
        return state is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : View(state);
    });
    internal Task<IResult> ResultAsync(HttpContext http) => Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<ResultCommand>(http, 4096).ConfigureAwait(false);
        var caller = Caller(http); var original = ReadOriginal(http, caller, true);
        if (original is null || original.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var result = await application.ReadResultAsync(caller, Request(original), http.RequestAborted).ConfigureAwait(false);
        if (result.Receipt is not { } receipt) return Problem(result.ErrorCode == "AUTHENTICATION_FAILED" ? 401 : 409, "RESULT_UNCONFIRMED");
        return Results.Json(new { original.RequestId, operationReference = original.CorrelationId, status = "REVOKED",
            target = new { reference = receipt.SessionReference, version = receipt.RevokedVersion.ToString(CultureInfo.InvariantCulture), original.LastSeenAt }, receipt.RevokedAt });
    });
    private Original? ReadOriginal(HttpContext http, StepUpCaller caller, bool requireCurrentPolicy)
    {
        if (!http.Request.Cookies.TryGetValue(SessionRevocationBrowserEndpoints.CookieName, out var value)) return null;
        Original? original;
        try { original = JsonSerializer.Deserialize<Original>(protector.Unprotect(value)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
        var now = clock.GetUtcNow();
        if (original is null || original.AccountId != caller.AccountId || original.ActorSessionId != caller.SessionId || original.Origin != caller.Origin ||
            original.RequestId == Guid.Empty || original.TargetReference == Guid.Empty || original.TargetReference == caller.SessionId ||
            now < original.CreatedAt || now >= original.ExpiresAt) return null;
        if (requireCurrentPolicy && authenticationPolicies.RequireEffective(now).Version != original.AuthenticationVersion) return null;
        return original;
    }
    private static SessionRevocationRequest Request(Original original) => new(original.RequestId, original.TargetReference, original.ExpectedVersion, original.CorrelationId);
    private static UserStepUpOperationContext Operation(StepUpCaller caller, Original original, bool completed = false)
    {
        var binding = SessionRevocationBinding.Operation(caller, Request(original));
        return new(new(caller), binding.OperationId, binding.CorrelationId, binding.Scope, binding.ResourceId,
            binding.ExpectedVersion, binding.ContextDigest, original.AssuranceVersion, completed);
    }
    internal bool IsSelected(UserStepUpActor actor)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current request is required.");
        var caller = Caller(http); var original = ReadOriginal(http, caller, true);
        return original is not null && SessionRevocationOriginalSelection.Matches(http, protection, actor,
            authenticationPolicies.RequireEffective(clock.GetUtcNow()).Version, clock.GetUtcNow(), "SINGLE", original.RequestId, Operation(caller, original));
    }
    private async Task<State?> CurrentAsync(HttpContext http, StepUpCaller caller, CancellationToken ct)
    {
        var original = ReadOriginal(http, caller, true); if (original is null) return null;
        var request = Request(original); var result = await application.ReadResultAsync(caller, request, ct).ConfigureAwait(false);
        if (result.ErrorCode == "AUTHENTICATION_FAILED") return null;
        if (result.Applied) return new(original, caller, request, "COMPLETED");
        var pending = await application.PrepareAsync(caller, request, ct).ConfigureAwait(false);
        if (pending is null) return new(original, caller, request, "CHANGED");
        try { _ = assurancePolicies.RequireEffective(SessionRevocationBinding.Scope, clock.GetUtcNow(), original.AssuranceVersion); }
        catch (AssurancePolicyVersionConflictException) { return new(original, caller, request, "CHANGED"); }
        return new(original, caller, request, "PENDING");
    }
    internal async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, IUserStepUpOperationSource otherOwner, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current request is required.");
        var other = await otherOwner.ResolveAsync(actor, ct).ConfigureAwait(false);
        if (!http.Request.Cookies.ContainsKey(SessionRevocationBrowserEndpoints.CookieName)) return other;
        var caller = Caller(http); var original = ReadOriginal(http, caller, true); if (original is null) return other;
        // A newly selected independent owner's binding supersedes this original selection.
        // An always-present fixture/fallback owner matching the captured baseline does not mask it.
        if (other is not null && StepUpBrowserService.Digest(other) != original.FallbackDigest)
        {
            return other; // Retain the original capability for explicit result-only recovery.
        }
        if (other is not null)
        {
            var now = clock.GetUtcNow();
            // Generic options resume the independent owner after the exact session effect.
            // Original session-bound proof/native POSTs still resolve that completed effect;
            // only an exact protected fallback view selects the fallback on later requests.
            if (HttpMethods.IsGet(http.Request.Method) && http.Request.Path == "/authentication/step-up/options" ||
                StepUpBrowserService.BrowserBoundTo(http, protection, now, other, authenticationPolicies.RequireEffective(now).Version))
            {
                var request = Request(original);
                var completed = new AuthenticationApplication.SessionRevocationExecution(database, authentication, caller, request,
                    SessionRevocationBinding.Operation(caller, request));
                if (await completed.ReadReceiptAsync(null, ct).ConfigureAwait(false) is not null) return other;
                // Generic execution resolves its owner again inside its already authorized
                // transaction. An uncompleted session intent cannot start a nested read there.
                if (database.Database.CurrentTransaction is not null) return null;
            }
        }
        if (database.Database.CurrentTransaction is not null)
        {
            // Native proof preparation re-resolves this exact protected selection inside its
            // own actor-authorized transaction; typed execution owns target locks/currentness.
            var at = clock.GetUtcNow(); var operation = Operation(caller, original);
            return IsSelected(actor) && StepUpBrowserService.BrowserBoundTo(http, protection, at, operation,
                authenticationPolicies.RequireEffective(at).Version) ? operation : null;
        }
        var current = await CurrentAsync(http, caller, ct).ConfigureAwait(false);
        if (current is null || current.Status == "CHANGED") return null;
        var binding = SessionRevocationBinding.Operation(caller, current.Request);
        return new(new(caller), binding.OperationId, binding.CorrelationId, binding.Scope, binding.ResourceId,
            binding.ExpectedVersion, binding.ContextDigest, original.AssuranceVersion, current.Status == "COMPLETED");
    }
    internal async Task<StepUpExecutionResult> ExecuteAsync(StepUpCaller caller, UserStepUpOperationContext operation, string key, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current request is required.");
        var original = ReadOriginal(http, caller, true); if (original is null) return new(false, "VERSION_CONFLICT");
        var binding = SessionRevocationBinding.Operation(caller, Request(original));
        if (binding.OperationId != operation.Binding.OperationId || binding.ContextDigest != operation.Binding.ContextDigest ||
            operation.ExpectedPolicyVersion != original.AssuranceVersion) return new(false, "VERSION_CONFLICT");
        var bounded = new StepUpApplication(database, authentication, authenticationPolicies,
            new OriginalAssurance(assurancePolicies, authenticationPolicies, original), passwordWork, protection, clock);
        return await bounded.ExecuteSessionRevocationAsync(caller, binding, Request(original), key, ct).ConfigureAwait(false);
    }
    // Enforce the original browser capability interval at the existing final proof-policy decision,
    // without changing an approved policy's values or introducing another proof lifecycle.
    private sealed class OriginalAssurance(IAssurancePolicySource source, IAuthenticationPolicySource auth, Original original) : IAssurancePolicySource
    {
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset at, string? expectedVersion = null)
        {
            if (scope != SessionRevocationBinding.Scope || at < original.CreatedAt || at >= original.ExpiresAt ||
                auth.RequireEffective(at).Version != original.AuthenticationVersion) throw new AssurancePolicyVersionConflictException();
            return source.RequireEffective(scope, at, expectedVersion ?? original.AssuranceVersion);
        }
    }
    private static IResult View(State state) => Results.Json(new { state.Original.RequestId, operationReference = state.Original.CorrelationId,
        state.Status, target = new { reference = state.Original.TargetReference, version = state.Original.ExpectedVersion.ToString(CultureInfo.InvariantCulture), state.Original.LastSeenAt }, state.Original.ExpiresAt });
    private static CookieOptions Cookie(TimeSpan remaining) => new() { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = remaining };
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    private static async Task<IResult> Guard(HttpContext http, Func<Task<IResult>> action)
    {
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (http.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
        try { return await action().ConfigureAwait(false); }
        catch (AssurancePolicyVersionConflictException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
}
