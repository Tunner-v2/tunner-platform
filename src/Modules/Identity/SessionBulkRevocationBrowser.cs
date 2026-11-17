using System.Collections.ObjectModel;
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
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit transport authority. The cookie bound includes framework-serialized name, value and attributes.</summary>
public sealed class SessionBulkRevocationHttpBudget(PolicyVersionCandidate<string> metadata, int maximumRequestBytes, int maximumSetCookieBytes)
{
    public const string Scope = "USER_BFF_SESSION_BULK_TRANSPORT";
    public PolicyVersionCandidate<string> Metadata { get; } = metadata ?? throw new ArgumentNullException(nameof(metadata));
    public int MaximumRequestBytes { get; } = maximumRequestBytes is > 0 and <= 4096 ? maximumRequestBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumRequestBytes));
    // RFC6265 section 6.1's minimum supported capacity is a technical ceiling, never a production default.
    public int MaximumSetCookieBytes { get; } = maximumSetCookieBytes is > 0 and <= 4096 ? maximumSetCookieBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumSetCookieBytes));
    internal static SessionBulkRevocationHttpBudget Current(ISessionBulkRevocationHttpBudgetSource source, DateTimeOffset at)
    {
        var value = source.RequireEffective(at) ?? throw new InvalidOperationException("Bulk transport authority is unavailable.");
        _ = EffectivePolicyVersionResolver.Resolve(at, Scope, new[] { value.Metadata }, static (a, b) => a == b,
            static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return value;
    }
    public override string ToString() => nameof(SessionBulkRevocationHttpBudget);
}
public interface ISessionBulkRevocationHttpBudgetSource
{
    SessionBulkRevocationHttpBudget RequireEffective(DateTimeOffset at);
}
/// <summary>Separate optional publication; no change to listing or single-session content requirements.</summary>
public sealed class SessionBulkRevocationPresentation
{
    internal static readonly ReadOnlyCollection<string> RequiredKeys = Array.AsReadOnly<string>([
        "account.sessions.bulk.action", "account.sessions.bulk.review.title", "account.sessions.bulk.review.description",
        "account.sessions.bulk.review.originalSet", "account.sessions.bulk.confirm", "account.sessions.bulk.cancel",
        "account.sessions.bulk.loading", "account.sessions.bulk.empty", "account.sessions.bulk.changed",
        "account.sessions.bulk.completed", "account.sessions.bulk.unknown", "account.sessions.bulk.check"
    ]);
    public SessionBulkRevocationPresentation(string locale, IEnumerable<RegistrationPublishedText> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (RequiredKeys.Any(key => !values.ContainsKey(key)) || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
            new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Bulk revocation requires complete published content with provenance.", nameof(content));
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(values);
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
}
public interface ISessionBulkRevocationPresentationSource
{
    SessionBulkRevocationPresentation RequirePublished(AuthenticationPolicy policy);
}

public static class SessionBulkRevocationBrowserEndpoints
{
    internal const string Root = "/account/sessions/bulk-revocation";
    internal const string CookieName = "__Host-Tunner.SessionBulkRevocation";
    public static IServiceCollection AddTunnerSessionBulkRevocation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(ISessionListingHttpBudgetSource), typeof(ISessionBulkRevocationDiscoverySource),
            typeof(ISessionBulkRevocationHttpBudgetSource), typeof(ISessionBulkRevocationPresentationSource),
            typeof(IAssurancePolicySource), typeof(IStepUpPresentationSource)];
        if (required.Any(t => !services.Any(x => x.ServiceType == t)))
            throw new InvalidOperationException("Bulk session revocation requires explicit discovery, transport, publication and assurance sources.");
        if (services.Any(x => x.ServiceType == typeof(SessionBulkRevocationBrowserService)))
            throw new InvalidOperationException("Bulk revocation may be activated only once.");
        services.TryAddScoped<IUserStepUpOperationSource, SessionRevocationEmptyOwner>();
        services.AddScoped<SessionBulkRevocationBrowserService>(); return services;
    }
    internal static void MapIfConfigured(IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(SessionBulkRevocationBrowserService))) return;
        var user = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapGet(Root + "/options", (HttpContext c, SessionBulkRevocationBrowserService s) => s.OptionsAsync(c)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/preview", (HttpContext c, SessionBulkRevocationBrowserService s) => s.PreviewAsync(c)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/activate", (HttpContext c, SessionBulkRevocationBrowserService s) => s.ActivateAsync(c)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/cancel", (HttpContext c, SessionBulkRevocationBrowserService s) => s.CancelAsync(c)).RequireAuthorization(user);
        endpoints.MapGet(Root + "/context", (HttpContext c, SessionBulkRevocationBrowserService s) => s.ContextAsync(c)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/result", (HttpContext c, SessionBulkRevocationBrowserService s) => s.ResultAsync(c)).RequireAuthorization(user);
    }
}

internal sealed class SessionBulkRevocationBrowserService(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    StepUpApplication stepUp, IUserStepUpOperationSource fallback, IAssurancePolicySource assurancePolicies,
    IAuthenticationPolicySource authenticationPolicies, ISessionBulkRevocationDiscoverySource discovery,
    ISessionBulkRevocationHttpBudgetSource transport, ISessionBulkRevocationPresentationSource presentations,
    AuthenticationPasswordWork passwordWork, AuthenticationEndpoints.Surface surface, AuthenticationPartitionKey partitions,
    IDataProtectionProvider protection, IHttpContextAccessor accessor, TimeProvider clock)
{
    private readonly SessionBulkRevocationApplication application = new(authentication, stepUp, discovery, assurancePolicies);
    private readonly IDataProtector protector = protection.CreateProtector("Tunner.User.SessionBulkRevocation.Original.v1");
    private sealed record Original(string AccountId, Guid ActorSessionId, string Origin, Guid RequestId, string CorrelationId,
        SessionBulkRevocationTarget[] Targets, DateTimeOffset DiscoveredAt, string AuthenticationVersion, string AssuranceVersion,
        string DiscoveryVersion, int MaximumCandidates, string TransportVersion, int MaximumRequestBytes, int MaximumSetCookieBytes,
        DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string? FallbackDigest, string SelectionBaseline, bool Activated)
    { public override string ToString() => nameof(Original); }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class Command
    { public required Guid RequestId { get; init; } public override string ToString() => nameof(Command); }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class PreviewCommand
    {
        private Guid? replay;
        public Guid RequestId
        {
            get => replay ?? Guid.Empty;
            init { if (value == Guid.Empty) throw new ArgumentException("A replay requires a nonempty request identity."); replay = value; }
        }
        internal Guid? Replay => replay;
        public override string ToString() => nameof(PreviewCommand);
    }
    private sealed record State(Original Original, SessionBulkRevocationManifest Manifest, StepUpCaller Caller, string Status);
    private StepUpCaller Caller(HttpContext http) => http.Connection.RemoteIpAddress is { } peer
        ? StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(peer))
        : throw new ArgumentException("A current authenticated peer is required.");
    private async ValueTask<SessionBulkRevocationPresentation> PublicationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
            if (presentations is ISessionBulkRevocationAsyncPresentationSource asynchronous)
            {
                var result = await asynchronous.RequirePublishedAsync(auth, cancellationToken).ConfigureAwait(false);
                var current = authenticationPolicies.RequireEffective(clock.GetUtcNow()); current.Validate();
                if (current != auth) throw new InvalidOperationException("Bulk authentication policy changed during publication resolution.");
                return result ?? throw new InvalidOperationException("Bulk publication is unavailable.");
            }
            return presentations.RequirePublished(auth) ?? throw new InvalidOperationException("Bulk publication is unavailable.");
        }
        catch (ArgumentException) { throw new InvalidOperationException("Bulk publication is unavailable."); }
    }
    internal Task<IResult> OptionsAsync(HttpContext http) => Guard(http, async () =>
    {
        _ = Caller(http); var at = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(at); auth.Validate();
        _ = SessionBulkRevocationHttpBudget.Current(transport, at); var published = await PublicationAsync(http.RequestAborted).ConfigureAwait(false);
        return auth == authenticationPolicies.RequireEffective(clock.GetUtcNow())
            ? Results.Json(new { published.Locale, published.Content }) : Problem(503, "DEPENDENCY_UNAVAILABLE");
    });
    private async Task<Command> ReadCommand(HttpContext http)
    {
        var budget = SessionBulkRevocationHttpBudget.Current(transport, clock.GetUtcNow());
        var command = await IdentityJsonCommand.ReadAsync<Command>(http, budget.MaximumRequestBytes).ConfigureAwait(false);
        if (command.RequestId == Guid.Empty) throw new ArgumentException("An exact original request is required.");
        return command;
    }
    internal Task<IResult> PreviewAsync(HttpContext http) => Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var capacity = SessionBulkRevocationHttpBudget.Current(transport, clock.GetUtcNow());
        var command = await IdentityJsonCommand.ReadAsync<PreviewCommand>(http, capacity.MaximumRequestBytes).ConfigureAwait(false);
        var caller = Caller(http); _ = await PublicationAsync(http.RequestAborted).ConfigureAwait(false);
        var existing = ReadOriginal(http, caller);
        if (command.Replay is { } replay && (existing is null || existing.RequestId != replay))
            return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (existing is null && (http.Request.Cookies.ContainsKey(SessionBulkRevocationBrowserEndpoints.CookieName) ||
            SessionRevocationOriginalSelection.ReadEvidence(http, protection, new(caller))?.Family == "BULK"))
            return Problem(409, "RESULT_UNCONFIRMED");
        if (existing is not null)
        {
            var previous = await CurrentAsync(caller, existing, http.RequestAborted).ConfigureAwait(false);
            if (command.Replay is not null) return previous is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : View(previous);
            if (existing.Activated && previous?.Status != "COMPLETED") return Problem(409, "RESULT_UNCONFIRMED");
        }
        var prepared = await application.PrepareAsync(caller, Guid.NewGuid(), "COR-SESSION-REVOKE-OTHERS-" + Guid.NewGuid().ToString("N"), http.RequestAborted).ConfigureAwait(false);
        if (prepared.Manifest is not { } manifest) return Failure(prepared.ErrorCode ?? "VERSION_CONFLICT");
        var now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        var assurance = assurancePolicies.RequireEffective(SessionBulkRevocationApplication.Scope, now, manifest.AssuranceVersion);
        var discoveryBudget = SessionBulkRevocationDiscoveryBudget.Current(discovery, now);
        var budget = SessionBulkRevocationHttpBudget.Current(transport, now);
        if (auth.Version != manifest.AuthenticationVersion || discoveryBudget.Metadata.VersionId != manifest.DiscoveryVersion ||
            discoveryBudget.MaximumCandidates != manifest.MaximumCandidates) return Problem(409, "VERSION_CONFLICT");
        var expires = now + auth.BrowserContextLifetime;
        foreach (var until in new[] { assurance.Metadata.EffectiveInterval.Until, discoveryBudget.Metadata.EffectiveInterval.Until, budget.Metadata.EffectiveInterval.Until })
            if (until is { } end && end < expires) expires = end;
        var other = await fallback.ResolveAsync(new(caller), http.RequestAborted).ConfigureAwait(false);
        var original = new Original(caller.AccountId, caller.SessionId, caller.Origin, manifest.RequestId, manifest.CorrelationId,
            manifest.Targets.ToArray(), manifest.DiscoveredAt, manifest.AuthenticationVersion, manifest.AssuranceVersion,
            manifest.DiscoveryVersion, manifest.MaximumCandidates, budget.Metadata.VersionId, budget.MaximumRequestBytes, budget.MaximumSetCookieBytes,
            now, expires, other is null ? null : StepUpBrowserService.Digest(other), SessionRevocationOriginalSelection.Fingerprint(http), false);
        var finalAt = clock.GetUtcNow();
        var finalDiscovery = SessionBulkRevocationDiscoveryBudget.Current(discovery, finalAt);
        if (finalAt >= expires || authenticationPolicies.RequireEffective(finalAt) != auth || !TransportCurrent(original, finalAt) ||
            finalDiscovery.Metadata.VersionId != manifest.DiscoveryVersion || finalDiscovery.MaximumCandidates != manifest.MaximumCandidates ||
            assurancePolicies.RequireEffective(SessionBulkRevocationApplication.Scope, finalAt, manifest.AssuranceVersion).Metadata.VersionId != manifest.AssuranceVersion)
            return Problem(409, "VERSION_CONFLICT");
        var encoded = protector.Protect(JsonSerializer.Serialize(original));
        // Activation changes only one boolean. Reserve capacity using the longer draft representation,
        // and also size the actual protected selector before either capability is issued.
        var selection = Selection(caller, original, manifest);
        var selected = SessionRevocationOriginalSelection.Protect(protection, selection);
        if (!Fits(original, encoded, selected, finalAt)) return Problem(409, "TRANSPORT_CAPACITY_EXCEEDED");
        http.Response.Cookies.Append(SessionBulkRevocationBrowserEndpoints.CookieName, encoded, Cookie(expires - finalAt));
        return View(new(original, manifest, caller, "DRAFT"));
    });
    internal Task<IResult> ActivateAsync(HttpContext http) => Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await ReadCommand(http).ConfigureAwait(false); var caller = Caller(http); _ = await PublicationAsync(http.RequestAborted).ConfigureAwait(false);
        var original = ReadOriginal(http, caller);
        if (original is null || original.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var current = await CurrentAsync(caller, original, http.RequestAborted).ConfigureAwait(false);
        if (current is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        // Replays never reselect an activated operation or supersede a subsequently selected family.
        if (original.Activated || current.Status == "COMPLETED") return View(current);
        if (original.SelectionBaseline != SessionRevocationOriginalSelection.Fingerprint(http) || current.Status == "CHANGED")
            return Problem(409, "VERSION_CONFLICT");
        var activated = original with { Activated = true }; var at = clock.GetUtcNow();
        var encoded = protector.Protect(JsonSerializer.Serialize(activated));
        var selected = SessionRevocationOriginalSelection.Protect(protection, Selection(caller, activated, current.Manifest));
        if (at >= original.ExpiresAt || !TransportCurrent(original, at)) return Problem(409, "VERSION_CONFLICT");
        if (!Fits(activated, encoded, selected, at)) return Problem(409, "TRANSPORT_CAPACITY_EXCEEDED");
        http.Response.Cookies.Append(SessionBulkRevocationBrowserEndpoints.CookieName, encoded, Cookie(original.ExpiresAt - at));
        http.Response.Cookies.Append(SessionRevocationOriginalSelection.CookieName, selected, Cookie(original.ExpiresAt - at));
        http.Response.Cookies.Delete(StepUpBrowserEndpoints.CookieName, Cookie(TimeSpan.Zero));
        return View(current with { Original = activated, Status = "PENDING" });
    });
    internal Task<IResult> CancelAsync(HttpContext http) => Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await ReadCommand(http).ConfigureAwait(false); var caller = Caller(http); var original = ReadOriginal(http, caller);
        if (original is null || original.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (original.Activated || IsSelected(new(caller))) return Problem(409, "RESULT_UNCONFIRMED");
        http.Response.Cookies.Delete(SessionBulkRevocationBrowserEndpoints.CookieName, Cookie(TimeSpan.Zero));
        return Results.Json(new { original.RequestId, status = "CANCELLED" });
    });
    internal Task<IResult> ContextAsync(HttpContext http) => Guard(http, async () =>
    {
        var caller = Caller(http); var original = ReadOriginal(http, caller);
        var state = original is null ? null : await CurrentAsync(caller, original, http.RequestAborted).ConfigureAwait(false);
        return state is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : View(state);
    });
    internal Task<IResult> ResultAsync(HttpContext http) => Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await ReadCommand(http).ConfigureAwait(false); var caller = Caller(http); var original = ReadOriginal(http, caller);
        if (original is null || original.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var manifest = Restore(caller, original); var result = await application.ReadResultAsync(caller, manifest, http.RequestAborted).ConfigureAwait(false);
        if (!result.Applied) return Problem(result.ErrorCode == "AUTHENTICATION_FAILED" ? 401 : 409, "RESULT_UNCONFIRMED");
        return Results.Json(new { original.RequestId, operationReference = original.CorrelationId, status = "REVOKED",
            targets = result.Receipts!.Select(x => new { reference = x.SessionReference,
                version = x.RevokedVersion.ToString(CultureInfo.InvariantCulture), x.RevokedAt }) });
    });
    private Original? ReadOriginal(HttpContext http, StepUpCaller caller)
    {
        if (!http.Request.Cookies.TryGetValue(SessionBulkRevocationBrowserEndpoints.CookieName, out var value) || value.Length > 4096) return null;
        Original? original;
        try { original = JsonSerializer.Deserialize<Original>(protector.Unprotect(value)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
        var now = clock.GetUtcNow();
        if (original is null || original.AccountId != caller.AccountId || original.ActorSessionId != caller.SessionId || original.Origin != caller.Origin ||
            original.RequestId == Guid.Empty || original.Targets is not { Length: > 0 } || original.CreatedAt > now || now >= original.ExpiresAt ||
            original.DiscoveredAt > original.CreatedAt || original.MaximumSetCookieBytes is < 1 or > 4096 ||
            original.MaximumRequestBytes is < 1 or > 4096 || authenticationPolicies.RequireEffective(now).Version != original.AuthenticationVersion) return null;
        _ = Restore(caller, original); return original;
    }
    private static SessionBulkRevocationManifest Restore(StepUpCaller caller, Original original) => SessionBulkRevocationManifest.RestoreTrustedOriginal(caller,
        original.RequestId, original.CorrelationId, original.Targets, original.DiscoveredAt, original.AuthenticationVersion,
        original.AssuranceVersion, original.DiscoveryVersion, original.MaximumCandidates);
    private static UserStepUpOperationContext Operation(StepUpCaller caller, Original original, SessionBulkRevocationManifest manifest, bool completed = false)
    {
        var b = manifest.Operation(caller);
        return new(new(caller), b.OperationId, b.CorrelationId, b.Scope, b.ResourceId, b.ExpectedVersion, b.ContextDigest, original.AssuranceVersion, completed);
    }
    private static SessionRevocationOriginalSelection.Selection Selection(StepUpCaller caller, Original original, SessionBulkRevocationManifest manifest)
        => SessionRevocationOriginalSelection.Create("BULK", original.RequestId, Operation(caller, original, manifest),
            original.AuthenticationVersion, original.CreatedAt, original.ExpiresAt);
    private bool TransportCurrent(Original original, DateTimeOffset at)
    {
        var current = SessionBulkRevocationHttpBudget.Current(transport, at);
        return current.Metadata.VersionId == original.TransportVersion && current.MaximumRequestBytes == original.MaximumRequestBytes &&
            current.MaximumSetCookieBytes == original.MaximumSetCookieBytes;
    }
    private static bool Fits(Original original, string encoded, string selected, DateTimeOffset at)
        => SessionRevocationOriginalSelection.Fits(SessionBulkRevocationBrowserEndpoints.CookieName, encoded, Cookie(original.ExpiresAt - at), original.MaximumSetCookieBytes) &&
            SessionRevocationOriginalSelection.Fits(SessionRevocationOriginalSelection.CookieName, selected, Cookie(original.ExpiresAt - at), original.MaximumSetCookieBytes);
    private async Task<State?> CurrentAsync(StepUpCaller caller, Original original, CancellationToken ct)
    {
        var manifest = Restore(caller, original);
        // Read exact causal completion before consulting old discovery/assurance availability.
        var result = await application.ReadResultAsync(caller, manifest, ct).ConfigureAwait(false);
        if (result.ErrorCode == "AUTHENTICATION_FAILED") return null;
        if (result.Applied) return new(original, manifest, caller, "COMPLETED");
        try
        {
            if (!TransportCurrent(original, clock.GetUtcNow())) return new(original, manifest, caller, "CHANGED");
            var pending = await application.ReadOriginalAsync(caller, manifest, ct).ConfigureAwait(false);
            if (pending.ErrorCode == "AUTHENTICATION_FAILED") return null;
            return new(original, manifest, caller, pending.Applied ? "COMPLETED" : pending.ErrorCode is null ? original.Activated ? "PENDING" : "DRAFT" : "CHANGED");
        }
        catch (AssurancePolicyVersionConflictException) { return new(original, manifest, caller, "CHANGED"); }
    }
    internal bool IsSelected(UserStepUpActor actor)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current request is required.");
        var caller = Caller(http); var original = ReadOriginal(http, caller);
        return original?.Activated == true && SessionRevocationOriginalSelection.Matches(http, protection, actor,
            authenticationPolicies.RequireEffective(clock.GetUtcNow()).Version, clock.GetUtcNow(), "BULK", original.RequestId,
            Operation(caller, original, Restore(caller, original)));
    }
    internal async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, IUserStepUpOperationSource otherOwner, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current request is required.");
        var other = await otherOwner.ResolveAsync(actor, ct).ConfigureAwait(false);
        var caller = Caller(http); var original = ReadOriginal(http, caller); if (original?.Activated != true || !IsSelected(actor)) return other;
        if (other is not null && StepUpBrowserService.Digest(other) != original.FallbackDigest) return other;
        var manifest = Restore(caller, original);
        if (other is not null && (HttpMethods.IsGet(http.Request.Method) && http.Request.Path == "/authentication/step-up/options" ||
            StepUpBrowserService.BrowserBoundTo(http, protection, clock.GetUtcNow(), other, authenticationPolicies.RequireEffective(clock.GetUtcNow()).Version)))
        {
            var set = AuthenticationApplication.SessionRevocationExecutionSet.Bulk(database, authentication, caller, manifest, null, null);
            if (await set.ReadReceiptAsync(null, ct).ConfigureAwait(false) is not null) return other;
            if (database.Database.CurrentTransaction is not null) return null;
        }
        if (database.Database.CurrentTransaction is not null)
        {
            // Native proof owners re-resolve their already selected binding inside their own
            // actor-authorized transaction. Do not nest a read transaction or lock targets
            // after that actor; typed execution will lock and validate the complete set.
            var at = clock.GetUtcNow(); var operation = Operation(caller, original, manifest);
            return TransportCurrent(original, at) && StepUpBrowserService.BrowserBoundTo(http, protection, at, operation,
                authenticationPolicies.RequireEffective(at).Version) ? operation : null;
        }
        var current = await CurrentAsync(caller, original, ct).ConfigureAwait(false);
        return current is null || current.Status is "CHANGED" or "DRAFT" ? null : Operation(caller, original, manifest, current.Status == "COMPLETED");
    }
    internal async Task<StepUpExecutionResult> ExecuteAsync(StepUpCaller caller, UserStepUpOperationContext operation, string key, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current request is required.");
        var original = ReadOriginal(http, caller); if (original?.Activated != true || !IsSelected(new(caller))) return new(false, "VERSION_CONFLICT");
        var manifest = Restore(caller, original);
        if (StepUpBrowserService.Digest(Operation(caller, original, manifest)) != StepUpBrowserService.Digest(operation)) return new(false, "VERSION_CONFLICT");
        var bounded = new StepUpApplication(database, authentication, authenticationPolicies,
            new OriginalAssurance(assurancePolicies, authenticationPolicies, transport, original), passwordWork, protection, clock);
        return await bounded.ExecuteSessionBulkRevocationAsync(caller, manifest, discovery, key, ct).ConfigureAwait(false);
    }
    private sealed class OriginalAssurance(IAssurancePolicySource source, IAuthenticationPolicySource auth,
        ISessionBulkRevocationHttpBudgetSource transport, Original original) : IAssurancePolicySource
    {
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset at, string? expectedVersion = null)
        {
            var budget = SessionBulkRevocationHttpBudget.Current(transport, at);
            if (scope != SessionBulkRevocationApplication.Scope || at < original.CreatedAt || at >= original.ExpiresAt ||
                auth.RequireEffective(at).Version != original.AuthenticationVersion || budget.Metadata.VersionId != original.TransportVersion ||
                budget.MaximumRequestBytes != original.MaximumRequestBytes || budget.MaximumSetCookieBytes != original.MaximumSetCookieBytes)
                throw new AssurancePolicyVersionConflictException();
            return source.RequireEffective(scope, at, expectedVersion ?? original.AssuranceVersion);
        }
    }
    private static IResult View(State state) => Results.Json(new { state.Original.RequestId, operationReference = state.Original.CorrelationId,
        state.Status, activated = state.Original.Activated, preparedAt = state.Manifest.DiscoveredAt, targets = state.Manifest.Targets.Select(x => new
            { reference = x.SessionReference, version = x.ExpectedVersion.ToString(CultureInfo.InvariantCulture) }), state.Original.ExpiresAt });
    private static CookieOptions Cookie(TimeSpan remaining) => SessionRevocationOriginalSelection.Cookie(remaining);
    private static IResult Failure(string code) => Problem(code == "AUTHENTICATION_FAILED" ? 401 : 409, code);
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
        catch (BadHttpRequestException e) { return Problem(e.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
}
