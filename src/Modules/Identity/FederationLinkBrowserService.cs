using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Tunner.Identity.Persistence;

public sealed partial class FederationBrowserService
{
    internal bool ManagementEnabled => management is not null;
    private const string ManagementCookie = "__Host-Tunner.Federation.Management";
    private IDataProtector ManagementProtection => protection.CreateProtector("Tunner.User.Federation.ManagementPointer.v1");
    private sealed record ManagementPointer(Guid OperationId, Guid AttemptId, Guid RequestId, Guid BrowserKey, string BrowserDigest,
        string OriginalCookieDigest, string Origin, Guid SessionId, string AuthenticationPolicyVersion, DateTimeOffset BrowserExpiresAt,
        DateTimeOffset ExpiresAt, FederationProviderKind Provider, FederationAttemptPurpose Purpose)
    { public override string ToString() => nameof(ManagementPointer); }

    private ManagementPointer? ReadManagementPointer(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(ManagementCookie, out var value)) return null;
        try
        {
            var pointer = JsonSerializer.Deserialize<ManagementPointer>(ManagementProtection.Unprotect(value));
            return pointer is not null && pointer.OperationId != Guid.Empty && pointer.RequestId != Guid.Empty && pointer.SessionId != Guid.Empty &&
                pointer.BrowserKey != Guid.Empty && pointer.Origin == origin && pointer.ExpiresAt > clock.GetUtcNow() && Enum.IsDefined(pointer.Provider) &&
                pointer.Purpose is FederationAttemptPurpose.Link or FederationAttemptPurpose.OperationProof ? pointer : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }
    private (StepUpCaller Caller, FederationCurrentBrowser Browser) ManagementCurrent(HttpContext http, ManagementPointer? pointer)
    {
        var now = clock.GetUtcNow(); var policy = authenticationPolicies.RequireEffective(now); policy.Validate();
        if (!http.Request.IsHttps || http.Request.QueryString.HasValue || http.Request.Scheme + "://" + http.Request.Host.Value != origin ||
            http.Connection.RemoteIpAddress is null) throw new ArgumentException("A current original User browser is required.");
        var network = partitions.Network(http.Connection.RemoteIpAddress);
        var actual = StepUpCaller.FromPrincipal(http.User, origin, network);
        var digest = PasskeyApplication.MutationBrowser(actual);
        var cookieDigest = AuthenticationEndpoints.CookieDigest(http);
        if (pointer is not null && (pointer.SessionId != actual.SessionId || pointer.BrowserDigest != digest ||
            pointer.Origin != origin || pointer.AuthenticationPolicyVersion != policy.Version || pointer.BrowserExpiresAt <= now))
            throw new ArgumentException("The original management browser no longer matches.");
        // Only original comparison values come from the Strict pointer. Current session/principal/cookie are observed now.
        return (actual, new(new(digest, network, origin, actual.SessionId), pointer?.BrowserKey ?? Guid.NewGuid(),
            pointer?.BrowserExpiresAt ?? now + policy.BrowserContextLifetime, policy.Version, pointer?.OriginalCookieDigest ?? cookieDigest,
            pointer?.SessionId ?? actual.SessionId, cookieDigest));
    }
    private void WriteManagementPointer(HttpContext http, FederationAttemptRow attempt, Guid operationId, FederationAttemptPurpose purpose)
    {
        if (attempt.OwnerSessionId is not { } session || purpose is not (FederationAttemptPurpose.Link or FederationAttemptPurpose.OperationProof))
            throw new InvalidOperationException("An exact original management attempt is required.");
        var expires = attempt.ExpiresAt + TimeSpan.FromTicks(attempt.ResultRecoveryLifetimeTicks);
        var pointer = new ManagementPointer(operationId, attempt.AttemptId, attempt.RequestId, attempt.BrowserKey, attempt.BrowserDigest,
            attempt.PreviousCookieDigest, attempt.Origin, session, attempt.AuthenticationPolicyVersion, attempt.ExpiresAt, expires,
            Enum.Parse<FederationProviderKind>(attempt.Provider, false), purpose);
        http.Response.Cookies.Append(ManagementCookie, ManagementProtection.Protect(JsonSerializer.Serialize(pointer)), Cookie(expires - clock.GetUtcNow()));
    }
    private static void ClearManagementPointer(HttpContext http)
    { if (http.Request.Cookies.ContainsKey(ManagementCookie)) http.Response.Cookies.Delete(ManagementCookie, Cookie()); }

    public Task<IResult> StartLinkAsync(HttpContext http, Guid requestId, FederationProviderKind provider, Guid? linkReference,
        long expectedLinkVersion, long expectedAccountVersion, CancellationToken ct = default) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false);
        if (management is null || !enabledTransports.Contains(provider)) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var prior = ReadManagementPointer(http);
        var actual = ManagementCurrent(http, prior is not null && prior.BrowserExpiresAt > clock.GetUtcNow() ? prior : null);
        if (prior is not null && prior.RequestId != requestId)
        {
            var pending = await application.ReadManagementOriginalAsync(actual.Browser, actual.Caller, prior.AttemptId, token).ConfigureAwait(false);
            if (pending.Row is { State: "PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED" } active && active.ExpiresAt > clock.GetUtcNow())
                return Problem(409, "VERSION_CONFLICT");
            actual = ManagementCurrent(http, null);
        }
        var signInPointer = ReadPointer(http);
        if (signInPointer is not null && !await application.OriginalPointerIsTerminalAsync(signInPointer.AttemptId, signInPointer.BrowserKey,
            signInPointer.BrowserDigest, signInPointer.Origin, token).ConfigureAwait(false)) return Problem(409, "VERSION_CONFLICT");
        var result = await management.StartLinkAsync(actual.Browser, actual.Caller, requestId, provider, linkReference, expectedLinkVersion, expectedAccountVersion, token).ConfigureAwait(false);
        if (result.ErrorCode is null && result.Attempt is { State: "PREPARED" } attempt && result.Row is { } operation &&
            attempt.ExpiresAt > clock.GetUtcNow() && attempt.BrowserKey == actual.Browser.BrowserKey)
        {
            WriteManagementPointer(http, attempt, operation.OperationId, FederationAttemptPurpose.Link);
            FederationStepUpOwner.Select(http, operation, protection, clock.GetUtcNow());
            http.Response.Cookies.Delete(PendingCookie, Cookie());
            return Results.Json(new { operation = await management.ViewAsync(actual.Caller, operation, token).ConfigureAwait(false), challengePath = provider == FederationProviderKind.Google
                ? "/authentication/federation/google/challenge" : "/authentication/federation/microsoft/challenge" });
        }
        return await ManagementResult(http, actual, result, token).ConfigureAwait(false);
    }, ct);

    public Task<IResult> StartUnlinkAsync(HttpContext http, Guid requestId, FederationProviderKind provider, Guid linkReference,
        long expectedLinkVersion, long expectedAccountVersion, CancellationToken ct = default) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false);
        if (management is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var actual = ManagementCurrent(http, null);
        var result = await management.StartUnlinkAsync(actual.Caller, requestId, provider, linkReference, expectedLinkVersion, expectedAccountVersion,
            token, actual.Browser.ActualCookieDigest).ConfigureAwait(false);
        return await ManagementResult(http, actual, result, token).ConfigureAwait(false);
    }, ct);
    public Task<IResult> ConfirmLinkOperationAsync(HttpContext http, Guid operationId, Guid requestId, long expectedVersion, bool confirmed,
        CancellationToken ct = default) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false); if (management is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var actual = ManagementCurrent(http, ReadManagementPointer(http));
        return await ManagementResult(http, actual, await management.ConfirmAsync(actual.Caller, operationId, requestId, expectedVersion, confirmed, token).ConfigureAwait(false), token).ConfigureAwait(false);
    }, ct);
    public Task<IResult> ReadLinkOperationAsync(HttpContext http, Guid operationId, Guid requestId, CancellationToken ct = default) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false); if (management is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var actual = ManagementCurrent(http, null);
        var result = operationId == Guid.Empty ? await management.ReadOriginalByRequestAsync(actual.Caller, requestId, token).ConfigureAwait(false)
            : await management.ReadAsync(actual.Caller, operationId, requestId, token).ConfigureAwait(false);
        if (result.ErrorCode is null && result.Row is { } original && result.Attempt is { } attempt && attempt.State is ("PREPARED" or "CHALLENGE_ADMITTED" or "VERIFIED") &&
            management.CanRestoreOriginal(original, attempt, actual.Caller, actual.Browser, clock.GetUtcNow()))
            WriteManagementPointer(http, attempt, original.OperationId, FederationAttemptPurpose.Link);
        // Exact original read may restore context, never a challenge or a second mutation.
        return await ManagementResult(http, actual, result, token).ConfigureAwait(false);
    }, ct);
    public Task<IResult> CancelLinkOperationAsync(HttpContext http, Guid operationId, Guid requestId, CancellationToken ct = default) => Guard(http, async token =>
    {
        await RequirePost(http).ConfigureAwait(false); if (management is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var actual = ManagementCurrent(http, null);
        var result = await management.CancelAsync(actual.Caller, operationId, requestId, token).ConfigureAwait(false);
        if (result.Row?.State is "REJECTED" or "EXPIRED" && ReadManagementPointer(http)?.OperationId == operationId) ClearManagementPointer(http);
        if (result.Row?.State is "REJECTED" or "EXPIRED" && FederationStepUpOwner.Selection(http, protection)?.OperationId == operationId)
            FederationStepUpOwner.ClearFederationSelection(http);
        return await ManagementResult(http, actual, result, token).ConfigureAwait(false);
    }, ct);
    public Task<IResult> ReadLinkSettingsAsync(HttpContext http, CancellationToken ct = default) => Guard(http, async token =>
    {
        if (management is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var actual = ManagementCurrent(http, null);
        var value = await management.SettingsAsync(actual.Caller, enabledTransports, token).ConfigureAwait(false);
        if (value is null) return Problem(403, "FORBIDDEN_SCOPE");
        var hasActive = http.Request.Cookies.ContainsKey(FederationStepUpOwner.CookieName);
        var selected = FederationStepUpOwner.Selection(http, protection, hasActive ? FederationStepUpOwner.CookieName : FederationStepUpOwner.ResultCookieName);
        if (selected is not null && selected.AccountId == actual.Caller.AccountId && selected.SessionId == actual.Caller.SessionId &&
            selected.Origin == origin && selected.ExpiresAt > clock.GetUtcNow())
        {
            var result = await management.ReadAsync(actual.Caller, selected.OperationId, selected.RequestId, token).ConfigureAwait(false);
            if (result.ErrorCode is null && result.Row is { } row && (hasActive || row.EndedAt is not null))
            {
                var operation = await management.ViewAsync(actual.Caller, row, token).ConfigureAwait(false);
                if (!management.CanRestoreOriginal(row, result.Attempt, actual.Caller, actual.Browser, clock.GetUtcNow()))
                    operation = operation with { CanConfirm = false, CanRequestProof = false };
                value = value with { Operation = operation };
            }
        }
        return Results.Json(value);
    }, ct);
    private async Task<IResult> ManagementResult(HttpContext http, (StepUpCaller Caller, FederationCurrentBrowser Browser) actual,
        FederationLinkOperationResult result, CancellationToken ct)
    {
        if (result.ErrorCode is { } code) return Problem(code == "RATE_OR_ABUSE_LIMITED" ? 429 : code == "VERSION_CONFLICT" ? 409 : code == "VALIDATION_ERROR" ? 400 : 404, code);
        if (result.Row is not { } row || management is null) return Results.Json(new { operation = (FederationLinkOperationView?)null });
        var view = await management.ViewAsync(actual.Caller, row, ct).ConfigureAwait(false);
        if (management.CanRestoreOriginal(row, result.Attempt, actual.Caller, actual.Browser, clock.GetUtcNow()))
            FederationStepUpOwner.Select(http, row, protection, clock.GetUtcNow());
        else view = view with { CanConfirm = false, CanRequestProof = false };
        return Results.Json(new { operation = view });
    }
}
