using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class FederationFallbackOwner(IUserStepUpOperationSource owner)
{ internal IUserStepUpOperationSource Owner { get; } = owner; }
internal sealed record FederationOperationSelection(Guid OperationId, Guid RequestId, string AccountId, Guid SessionId, string Origin, DateTimeOffset ExpiresAt)
{ public override string ToString() => nameof(FederationOperationSelection); }

/// <summary>Exact protected operation selection, independent of native protocol state. No latest-operation fallback.</summary>
internal sealed class FederationStepUpOwner(FederationLinkApplication application, IdentityPersistenceDbContext database,
    IHttpContextAccessor accessor, IDataProtectionProvider protection, AuthenticationPartitionKey partitions,
    AuthenticationEndpoints.Surface surface, IEnumerable<FederationFallbackOwner> fallback, TimeProvider clock)
    : IUserStepUpOperationOutcomeSource, IUserStepUpBrowserCompletionSource
{
    internal const string CookieName = "__Host-Tunner.Federation.Operation";
    internal const string ResultCookieName = "__Host-Tunner.Federation.Result";
    private static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Tunner.User.Federation.Operation.v1");
    internal static FederationOperationSelection? Selection(HttpContext http, IDataProtectionProvider provider, string name = CookieName)
    {
        if (!http.Request.Cookies.TryGetValue(name, out var encoded)) return null;
        try { return JsonSerializer.Deserialize<FederationOperationSelection>(Protector(provider).Unprotect(encoded)); }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }
    internal static void ClearFederationSelection(HttpContext http)
    { if (http.Request.Cookies.ContainsKey(CookieName)) http.Response.Cookies.Delete(CookieName, Cookie()); }
    internal static void Select(HttpContext http, FederationLinkOperationRow row, IDataProtectionProvider provider, DateTimeOffset at)
    {
        var selected = new FederationOperationSelection(row.OperationId, row.RequestId, row.AccountId, row.SessionId, row.Origin,
            IdentityPersistenceTime.Normalize(row.RecoveryExpiresAt));
        var value = Protector(provider).Protect(JsonSerializer.Serialize(selected));
        if (row.EndedAt is null && row.ExpiresAt > at) http.Response.Cookies.Append(CookieName, value, Cookie(row.RecoveryExpiresAt - at));
        else ClearFederationSelection(http);
        http.Response.Cookies.Append(ResultCookieName, value, Cookie(row.RecoveryExpiresAt - at));
        foreach (var other in new[] { PasskeyStepUpOwner.CookieName, TotpStepUpOwner.CookieName, RecoveryCodeStepUpOwner.CookieName })
            if (http.Request.Cookies.ContainsKey(other)) http.Response.Cookies.Delete(other, Cookie());
    }
    private static CookieOptions Cookie(TimeSpan? maxAge = null) => new()
    { Path = "/", Domain = null, HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = maxAge };
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin,
        partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("Current User peer required.")));
    private bool Matches(FederationOperationSelection? selected, UserStepUpActor actor) => selected is not null &&
        selected.AccountId == actor.AccountId && selected.SessionId == actor.SessionId && selected.Origin == actor.Origin && selected.ExpiresAt > clock.GetUtcNow();

    public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current User BFF context required.");
        if (!http.Request.Cookies.ContainsKey(CookieName)) return fallback.SingleOrDefault() is { } prior
            ? await prior.Owner.ResolveAsync(actor, ct).ConfigureAwait(false) : null;
        var selected = Selection(http, protection);
        return Matches(selected, actor) ? await application.ResolveAsync(Caller(http), selected!.OperationId, selected.RequestId, ct).ConfigureAwait(false) : null;
    }
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct)
    {
        if (FederationLinkApplication.Owns(operation.Binding.Scope) || RecoveryCodeApplication.Owns(operation.Binding.Scope))
            throw new InvalidOperationException("The selected owner requires its typed winning outcome.");
        using var result = await ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false); return result.Applied;
    }
    public async Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation,
        IdentityPersistenceDbContext context, CancellationToken ct)
    {
        if (!ReferenceEquals(context, database)) throw new InvalidOperationException("The operation must share its Identity transaction.");
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current User BFF context required.");
        if (!http.Request.Cookies.ContainsKey(CookieName))
        {
            var prior = fallback.SingleOrDefault()?.Owner;
            if (prior is IUserStepUpOperationOutcomeSource typed) return await typed.ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false);
            if (FederationLinkApplication.Owns(operation.Binding.Scope) || RecoveryCodeApplication.Owns(operation.Binding.Scope))
                return UserStepUpOperationApplyOutcome.Conflict();
            return prior is not null && await prior.ApplyAsync(actor, operation, context, ct).ConfigureAwait(false)
                ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
        }
        var selected = Selection(http, protection);
        if (!Matches(selected, actor) || !FederationLinkApplication.Owns(operation.Binding.Scope) || operation.Binding.CorrelationId != selected!.OperationId.ToString("N"))
            return UserStepUpOperationApplyOutcome.Conflict();
        var original = await database.Set<FederationLinkOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == selected.OperationId &&
            x.RequestId == selected.RequestId && x.AccountId == actor.AccountId && x.SessionId == actor.SessionId, ct).ConfigureAwait(false);
        if (original is null || original.OriginalCookieDigest is null || original.OriginalCookieDigest != AuthenticationEndpoints.CookieDigest(http))
            return UserStepUpOperationApplyOutcome.Conflict();
        return await application.ApplyAsync(Caller(http), operation, ct).ConfigureAwait(false);
    }
    public async Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken ct)
    {
        if (!FederationLinkApplication.Owns(operation.Binding.Scope))
        {
            if (fallback.SingleOrDefault()?.Owner is IUserStepUpBrowserCompletionSource hook)
                await hook.CompleteBrowserAsync(operation, signInRequired, ct).ConfigureAwait(false);
            return;
        }
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current User BFF context required.");
        var selected = Selection(http, protection);
        if (!Matches(selected, operation.Actor) || operation.Binding.CorrelationId != selected!.OperationId.ToString("N"))
            throw new InvalidOperationException("The original federation selection does not match.");
        // This is after commit, including an already ended actor. Never recreate authority or run the mutation again.
        var row = await database.Set<FederationLinkOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == selected.OperationId &&
            x.RequestId == selected.RequestId && x.AccountId == selected.AccountId && x.SessionId == selected.SessionId && x.Origin == selected.Origin, ct).ConfigureAwait(false);
        if (row?.State != "COMPLETED" || row.ContextDigest != operation.Binding.ContextDigest)
            throw new InvalidOperationException("The original federation operation is not completed.");
        Select(http, row, protection, clock.GetUtcNow());
    }
}
