using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit approved transport capacity, not browser input or a production default.</summary>
public sealed class SessionListingHttpBudget
{
    public const string Scope = "USER_BFF_SESSION_LISTING";
    public SessionListingHttpBudget(PolicyVersionCandidate<string> metadata, int maximumRows, int maximumRequestBytes)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        ArgumentOutOfRangeException.ThrowIfEqual(maximumRows, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestBytes);
        Metadata = metadata; MaximumRows = maximumRows; MaximumRequestBytes = maximumRequestBytes;
    }
    public PolicyVersionCandidate<string> Metadata { get; }
    public int MaximumRows { get; }
    public int MaximumRequestBytes { get; }
    public override string ToString() => nameof(SessionListingHttpBudget);
}
public interface ISessionListingHttpBudgetSource
{
    SessionListingHttpBudget RequireEffective(DateTimeOffset at);
}
public sealed class SessionListingHttpBudgetCatalog : ISessionListingHttpBudgetSource
{
    private readonly SessionListingHttpBudget[] versions;
    public SessionListingHttpBudgetCatalog(IEnumerable<SessionListingHttpBudget> versions)
    {
        ArgumentNullException.ThrowIfNull(versions); this.versions = versions.ToArray();
        if (this.versions.Any(x => x is null)) throw new ArgumentException("Budget records cannot be null.", nameof(versions));
    }
    public SessionListingHttpBudget RequireEffective(DateTimeOffset at)
    {
        var selected = EffectivePolicyVersionResolver.Resolve(at, SessionListingHttpBudget.Scope, versions.Select(x => x.Metadata),
            static (a, b) => StringComparer.Ordinal.Equals(a, b), static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => ReferenceEquals(x.Metadata, selected));
    }
}

/// <summary>Optional private User read. Register an explicit budget source to activate; no shared host change.</summary>
internal static class SessionListingEndpoints
{
    internal const string Path = "/account/sessions/list";
    private const string Purpose = "Tunner.User.SessionListing.Continuation.v1";
    internal static void MapIfConfigured(IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        if (!services.IsService(typeof(ISessionListingHttpBudgetSource))) return;
        var user = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapPost(Path, ReadAsync).RequireAuthorization(user);
        SessionRevocationBrowserEndpoints.MapIfConfigured(endpoints);
        if (!services.IsService(typeof(ISessionListingPresentationSource))) return;
        endpoints.MapGet("/account/sessions/options", Options).RequireAuthorization(user);
        var assets = endpoints.ServiceProvider.GetRequiredService<IdentityWebAssets>();
        var surface = endpoints.ServiceProvider.GetRequiredService<AuthenticationEndpoints.Surface>();
        assets.MapScreens(endpoints, "sessions.html", ["/account/sessions", "/account/sessions/details"], context =>
            context.User.Identity is { IsAuthenticated: true, AuthenticationType: AuthenticationIdentity.Scheme } &&
            AuthenticationIdentity.SessionId(context.User) is not null && context.User.FindFirst(AuthenticationIdentity.OriginClaim)?.Value == surface.Origin);
    }
    private static IResult Options(HttpContext http, ISessionListingPresentationSource source, ISessionListingHttpBudgetSource budgets,
        IAuthenticationPolicySource authenticationPolicies, TimeProvider clock)
    {
        if (http.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
        try
        {
            var now = clock.GetUtcNow(); var budget = Current(budgets, now);
            var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
            var presentation = source.RequirePublished(auth) ?? throw new InvalidOperationException("Sessions publication is unavailable.");
            now = clock.GetUtcNow(); var currentAuth = authenticationPolicies.RequireEffective(now); currentAuth.Validate();
            if (auth != currentAuth || !SameBudget(budget, Current(budgets, now)))
                return Problem(503, "DEPENDENCY_UNAVAILABLE");
            return Results.Json(new { presentation.Locale, presentation.Content });
        }
        catch (ArgumentException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class Command
    {
        public string? Continuation { get; init; }
        public override string ToString() => nameof(Command);
    }
    private sealed record Cursor(SessionListingContinuation Boundary, string BudgetVersion, int MaximumRows, int MaximumRequestBytes,
        string AuthenticationPolicyVersion, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
    { public override string ToString() => nameof(Cursor); }
    private sealed record Item(Guid Reference, string Version, bool IsCurrent, DateTimeOffset AuthenticatedAt,
        DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt, string RecordedState, string EffectiveState, DateTimeOffset EvaluatedAt)
    { public override string ToString() => nameof(Item); }
    private sealed record Response(IReadOnlyList<Item> Sessions, bool HasMore, string? Continuation)
    { public override string ToString() => nameof(Response); }

    private static async Task<IResult> ReadAsync(HttpContext http, AuthenticationApplication application,
        ISessionListingHttpBudgetSource budgets, IAuthenticationPolicySource authenticationPolicies,
        AuthenticationEndpoints.Surface surface, AuthenticationPartitionKey partitions, IDataProtectionProvider protection, TimeProvider clock)
    {
        try
        {
            if (http.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
            if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
            var now = clock.GetUtcNow(); var budget = Current(budgets, now);
            var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
            if (http.Connection.RemoteIpAddress is null) return Problem(401, "AUTHENTICATION_FAILED");
            var caller = StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(http.Connection.RemoteIpAddress));
            var command = await IdentityJsonCommand.ReadAsync<Command>(http, budget.MaximumRequestBytes).ConfigureAwait(false);
            var protector = protection.CreateProtector(Purpose);
            Cursor? original = null;
            if (command.Continuation is not null)
            {
                if (string.IsNullOrWhiteSpace(command.Continuation)) return Problem(400, "VALIDATION_ERROR");
                try { original = JsonSerializer.Deserialize<Cursor>(protector.Unprotect(command.Continuation)); }
                catch (CryptographicException) { return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE"); }
                catch (JsonException) { return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE"); }
                now = clock.GetUtcNow();
                if (original?.Boundary is not { } boundary || boundary.AccountId != caller.AccountId || boundary.ActorSessionId != caller.SessionId ||
                    boundary.Origin != caller.Origin || boundary.SessionId == Guid.Empty || now < original.CreatedAt || now >= original.ExpiresAt)
                    return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
                if (original.BudgetVersion != budget.Metadata.VersionId || original.MaximumRows != budget.MaximumRows ||
                    original.MaximumRequestBytes != budget.MaximumRequestBytes || original.AuthenticationPolicyVersion != auth.Version)
                    return Problem(409, "VERSION_CONFLICT");
            }
            var page = await application.ListOwnedSessionsAsync(caller, new(budget.MaximumRows, original?.Boundary), http.RequestAborted).ConfigureAwait(false);
            if (page is null) return Problem(401, "AUTHENTICATION_FAILED");
            now = clock.GetUtcNow();
            var finalBudget = Current(budgets, now); var finalAuth = authenticationPolicies.RequireEffective(now); finalAuth.Validate();
            if (!SameBudget(budget, finalBudget) || auth != finalAuth) return Problem(409, "VERSION_CONFLICT");
            var created = original?.CreatedAt ?? now;
            var expires = original?.ExpiresAt ?? created + auth.BrowserContextLifetime;
            if (budget.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
            if (now < created || now >= expires) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
            var continuation = page.Next is null ? null : protector.Protect(JsonSerializer.Serialize(new Cursor(page.Next, budget.Metadata.VersionId,
                budget.MaximumRows, budget.MaximumRequestBytes, auth.Version, created, expires)));
            var items = page.Items.Select(x => new Item(x.SessionReference, x.Version.ToString(CultureInfo.InvariantCulture), x.IsCurrent,
                x.AuthenticatedAt, x.LastSeenAt, x.ExpiresAt, State(x.RecordedState), State(x.EffectiveState), x.EvaluatedAt)).ToArray();
            return Results.Json(new Response(Array.AsReadOnly(items), page.HasMore, continuation));
        }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
    private static SessionListingHttpBudget Current(ISessionListingHttpBudgetSource source, DateTimeOffset at)
    {
        var value = source.RequireEffective(at);
        if (value.Metadata.Scope != SessionListingHttpBudget.Scope || value.Metadata.Status != "ACTIVE" || !value.Metadata.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Approved session listing capacity is unavailable.");
        return value;
    }
    private static bool SameBudget(SessionListingHttpBudget a, SessionListingHttpBudget b) =>
        a.Metadata.VersionId == b.Metadata.VersionId && a.MaximumRows == b.MaximumRows && a.MaximumRequestBytes == b.MaximumRequestBytes &&
        a.Metadata.EffectiveInterval.From == b.Metadata.EffectiveInterval.From && a.Metadata.EffectiveInterval.Until == b.Metadata.EffectiveInterval.Until &&
        a.Metadata.EffectiveInterval.IncludesFrom == b.Metadata.EffectiveInterval.IncludesFrom && a.Metadata.EffectiveInterval.IncludesUntil == b.Metadata.EffectiveInterval.IncludesUntil &&
        a.Metadata.ApprovalEvidenceReferences.SequenceEqual(b.Metadata.ApprovalEvidenceReferences, StringComparer.Ordinal);
    private static string State(SessionListingState state) => state switch
    {
        SessionListingState.Active => "ACTIVE", SessionListingState.Revoked => "REVOKED", SessionListingState.Expired => "EXPIRED",
        _ => throw new InvalidOperationException("Unsupported listing lifecycle state.")
    };
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
}
