using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Tunner.Identity.Persistence;

internal sealed record TotpProofSelection(Guid RequestId, Guid AttemptId, Guid AuthenticatorReference, string BindingDigest,
    string AccountId, Guid SessionId, string Origin, DateTimeOffset ExpiresAt);
internal sealed partial class StepUpBrowserService
{
    private const string TotpProofCookie = "__Host-Tunner.Totp.StepUp";
    private IDataProtector TotpProofProtection => protection.CreateProtector("Tunner.User.Totp.StepUpRecovery.v1");
    private void SelectTotpProof(HttpContext http, StepUpBrowserOperation frame, TotpApplication native, Guid attempt, Guid reference, DateTimeOffset proofExpires)
    {
        var expires = IdentityPersistenceTime.Normalize(proofExpires + native.Current(frame.Caller.Origin).ResultRecoveryLifetime);
        var selected = new TotpProofSelection(frame.Browser.RequestId, attempt, reference, frame.Browser.BindingDigest, frame.Caller.AccountId, frame.Caller.SessionId, frame.Caller.Origin, expires);
        http.Response.Cookies.Append(TotpProofCookie, TotpProofProtection.Protect(JsonSerializer.Serialize(selected)), TotpBrowserService.Cookie(expires - clock.GetUtcNow()));
    }
    private async Task<TotpProofRecovery?> ReadTotpRecoveryAsync(HttpContext http, StepUpBrowserOperation frame, TotpApplication native)
    {
        if (!http.Request.Cookies.TryGetValue(TotpProofCookie, out var value)) return null; TotpProofSelection? selected;
        try { selected = JsonSerializer.Deserialize<TotpProofSelection>(TotpProofProtection.Unprotect(value)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
        if (selected is null || selected.RequestId != frame.Browser.RequestId || selected.BindingDigest != frame.Browser.BindingDigest || selected.AccountId != frame.Caller.AccountId ||
            selected.SessionId != frame.Caller.SessionId || selected.Origin != frame.Caller.Origin || selected.ExpiresAt <= clock.GetUtcNow()) return null;
        return await native.ReadProofRecoveryAsync(frame.Caller, frame.Operation, selected.RequestId, selected.AttemptId, selected.AuthenticatorReference, http.RequestAborted).ConfigureAwait(false);
    }
    private async Task<IResult> CompletedTotpAsync(HttpContext http, StepUpBrowserOperation frame, TotpApplication native, Guid attempt, Guid reference)
    {
        var current = await owner.ResolveAsync(frame.Actor, http.RequestAborted).ConfigureAwait(false);
        if (!MatchesActor(current, frame.Actor) || current?.Completed != true || Digest(current) != frame.Browser.BindingDigest) return Failure("VERSION_CONFLICT");
        var original = await native.ReadProofRecoveryAsync(frame.Caller, current, frame.Browser.RequestId, attempt, reference, http.RequestAborted).ConfigureAwait(false);
        return original?.Status == "CONSUMED" ? Results.Json(new { status = "COMPLETED", operationReference = current.Binding.CorrelationId, frame.Browser.RequestId, attemptId = attempt }) : Failure("VERSION_CONFLICT");
    }
    internal Task<IResult> TotpContinueAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = totpApplications?.SingleOrDefault(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<TotpProofContinueCommand>(http, 4096).ConfigureAwait(false);
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Failure("VERSION_CONFLICT");
        if (frame.Operation.Completed) return await CompletedTotpAsync(http, frame, native, command.AttemptId, command.AuthenticatorReference).ConfigureAwait(false);
        var recovered = await native.RecoverOperationProofAsync(frame.Caller, frame.Operation, command.AttemptId, command.AuthenticatorReference, http.RequestAborted).ConfigureAwait(false);
        if (recovered.ErrorCode is { } error) return Failure(error);
        SelectTotpProof(http, frame, native, command.AttemptId, command.AuthenticatorReference, recovered.Grant!.ExpiresAt);
        return await ExecuteProofAsync(http, frame, recovered.Grant.Key).ConfigureAwait(false);
    });
}
