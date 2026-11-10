using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

internal sealed record StepUpBrowserOperation(StepUpCaller Caller, UserStepUpActor Actor, UserStepUpOperationContext Operation, StepUpBrowserService.Browser Browser);
internal sealed record PasskeyProofSelection(Guid RequestId, Guid AttemptId, Guid CeremonyId, string OperationReference, string BindingDigest,
    string AccountId, Guid SessionId, string Origin, DateTimeOffset ExpiresAt);
internal sealed record NativeProofCompletion(Guid RequestId, Guid AttemptId, Guid CeremonyId);
internal sealed record PasskeyProofRecovery(Guid RequestId, Guid AttemptId, Guid CeremonyId, string OperationReference, string Status, DateTimeOffset ExpiresAt, JsonElement? PublicKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyProofOptionsCommand
{
    public required Guid RequestId { get; init; }
    public required Guid AttemptId { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyProofCommand
{
    public required Guid RequestId { get; init; }
    public required Guid AttemptId { get; init; }
    public required Guid CeremonyId { get; init; }
    public required JsonElement Credential { get; init; }
    public override string ToString() => nameof(PasskeyProofCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyProofContinueCommand
{
    public required Guid RequestId { get; init; }
    public required Guid AttemptId { get; init; }
    public required Guid CeremonyId { get; init; }
}
internal sealed partial class StepUpBrowserService
{
    private const string ProofRecoveryCookie = "__Host-Tunner.Passkey.StepUp";
    private IDataProtector ProofRecoveryProtection => protection.CreateProtector("Tunner.User.Passkey.StepUpRecovery.v1");
    private async Task<StepUpBrowserOperation?> CurrentProofOperationAsync(HttpContext http, Guid requestId)
    {
        var caller = Caller(http); var now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (requestId == Guid.Empty || !TryBrowser(http, now, out var browser) || browser!.RequestId != requestId || browser.AuthenticationPolicyVersion != auth.Version) return null;
        var actor = new UserStepUpActor(caller); var operation = await owner.ResolveAsync(actor, http.RequestAborted).ConfigureAwait(false);
        return MatchesActor(operation, actor) && Digest(operation!) == browser.BindingDigest ? new(caller, actor, operation!, browser) : null;
    }
    internal Task<IResult> PasskeyOptionsAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = passkeyApplications.SingleOrDefault(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<PasskeyProofOptionsCommand>(http, 4096).ConfigureAwait(false);
        if (command.AttemptId == Guid.Empty) return Problem(400, "VALIDATION_ERROR");
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Problem(409, "VERSION_CONFLICT");
        if (frame.Operation.Completed) return await ReadNativeCompletedAsync(http, frame, native, command.AttemptId, null, null).ConfigureAwait(false);
        var result = await native.StartOperationProofAsync(frame.Caller, frame.Operation, command.RequestId, command.AttemptId, http, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return await NativeProofFailureAsync(error, result.Ceremony, frame, native, http.RequestAborted).ConfigureAwait(false);
        var row = result.Ceremony!;
        var selected = new PasskeyProofSelection(command.RequestId, command.AttemptId, row.CeremonyId, frame.Operation.Binding.CorrelationId, frame.Browser.BindingDigest,
            frame.Caller.AccountId, frame.Caller.SessionId, frame.Caller.Origin, IdentityPersistenceTime.Normalize(row.RecoveryExpiresAt));
        http.Response.Cookies.Append(ProofRecoveryCookie, ProofRecoveryProtection.Protect(JsonSerializer.Serialize(selected)),
            new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = row.RecoveryExpiresAt - clock.GetUtcNow() });
        return Results.Json(new { command.RequestId, command.AttemptId, row.CeremonyId, operationReference = selected.OperationReference,
            status = await native.OperationProofStatusAsync(row, http.RequestAborted).ConfigureAwait(false), row.ExpiresAt,
            publicKey = row.State == "PENDING" && row.ExpiresAt > clock.GetUtcNow() ? JsonSerializer.Deserialize<JsonElement>(row.OptionsJson) : (JsonElement?)null });
    });
    internal Task<IResult> PasskeyAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = passkeyApplications.SingleOrDefault(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var maximum = native.Current(surface.Origin).MaximumCredentialJsonBytes;
        var command = await IdentityJsonCommand.ReadAsync<PasskeyProofCommand>(http, checked((long)maximum + 4096)).ConfigureAwait(false);
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Problem(409, "VERSION_CONFLICT");
        if (frame.Operation.Completed) return await ReadNativeCompletedAsync(http, frame, native, command.AttemptId, command.CeremonyId, command.Credential).ConfigureAwait(false);
        var result = await native.CompleteOperationProofAsync(frame.Caller, frame.Operation, command.RequestId, command.AttemptId, command.CeremonyId,
            command.Credential, http.RequestAborted, http).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return await NativeProofFailureAsync(error, result.Ceremony, frame, native, http.RequestAborted).ConfigureAwait(false);
        return await ExecuteProofAsync(http, frame, result.Proof!.Key, new(command.RequestId, command.AttemptId, command.CeremonyId)).ConfigureAwait(false);
    });
    internal Task<IResult> PasskeyContinueAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = passkeyApplications.SingleOrDefault(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<PasskeyProofContinueCommand>(http, 4096).ConfigureAwait(false);
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Problem(409, "VERSION_CONFLICT");
        if (frame.Operation.Completed) return await ReadNativeCompletedAsync(http, frame, native, command.AttemptId, command.CeremonyId, null).ConfigureAwait(false);
        var result = await native.CompleteOperationProofAsync(frame.Caller, frame.Operation, command.RequestId, command.AttemptId, command.CeremonyId,
            null, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return await NativeProofFailureAsync(error, result.Ceremony, frame, native, http.RequestAborted).ConfigureAwait(false);
        return await ExecuteProofAsync(http, frame, result.Proof!.Key, new(command.RequestId, command.AttemptId, command.CeremonyId)).ConfigureAwait(false);
    });
    private static IResult ProofCompleted(UserStepUpOperationContext operation, bool signInRequired, NativeProofCompletion? native) => native is null
        ? Results.Json(new { status = "COMPLETED", operationReference = operation.Binding.CorrelationId, signInRequired })
        : Results.Json(new { status = "COMPLETED", native.RequestId, native.AttemptId, native.CeremonyId, operationReference = operation.Binding.CorrelationId, signInRequired });
    private static async Task<IResult> ReadNativeCompletedAsync(HttpContext http, StepUpBrowserOperation frame, PasskeyApplication native, Guid attemptId, Guid? ceremonyId, JsonElement? response)
    {
        var row = await native.ReadConsumedOperationProofAsync(frame.Caller, frame.Operation, frame.Browser.RequestId, attemptId, ceremonyId, response, http.RequestAborted).ConfigureAwait(false);
        return row is null ? Problem(409, "VERSION_CONFLICT") : ProofCompleted(frame.Operation, false, new(frame.Browser.RequestId, attemptId, row.CeremonyId));
    }
    private async Task<PasskeyProofRecovery?> ReadPasskeyRecoveryAsync(HttpContext http, StepUpBrowserOperation frame, PasskeyApplication native)
    {
        if (!http.Request.Cookies.TryGetValue(ProofRecoveryCookie, out var cookie)) return null;
        PasskeyProofSelection? selected;
        try { selected = JsonSerializer.Deserialize<PasskeyProofSelection>(ProofRecoveryProtection.Unprotect(cookie)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
        if (selected is null || selected.RequestId != frame.Browser.RequestId || selected.BindingDigest != frame.Browser.BindingDigest || selected.OperationReference != frame.Operation.Binding.CorrelationId ||
            selected.AccountId != frame.Caller.AccountId || selected.SessionId != frame.Caller.SessionId || selected.Origin != frame.Caller.Origin || selected.ExpiresAt <= clock.GetUtcNow()) return null;
        var row = await native.ReadOperationProofAsync(frame.Caller, frame.Operation, selected.RequestId, selected.AttemptId, selected.CeremonyId, http.RequestAborted).ConfigureAwait(false);
        return row is null ? null : new(selected.RequestId, selected.AttemptId, selected.CeremonyId, selected.OperationReference,
            await native.OperationProofStatusAsync(row, http.RequestAborted).ConfigureAwait(false), row.ExpiresAt);
    }
    private static async Task<IResult> NativeProofFailureAsync(string error, PasskeyCeremonyRow? row, StepUpBrowserOperation frame, PasskeyApplication native, CancellationToken ct)
    {
        var problem = TechnicalProblemDetails.Create(error switch { "AUTHENTICATION_FAILED" => 401, "RATE_OR_ABUSE_LIMITED" => 429, "VERSION_CONFLICT" => 409,
            "STEP_UP_REQUIRED" => 403, _ => 400 }, "about:blank", "The request could not be completed.", new(error, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
        if (row is not null && row.AccountId == frame.Caller.AccountId && row.SessionId == frame.Caller.SessionId && row.StepUpRequestId == frame.Browser.RequestId && row.BoundContextDigest == frame.Operation.Binding.ContextDigest)
        {
            problem.Extensions.Add("ceremony_status", await native.OperationProofStatusAsync(row, ct).ConfigureAwait(false));
            problem.Extensions.Add("requestId", frame.Browser.RequestId); problem.Extensions.Add("attemptId", row.RequestId); problem.Extensions.Add("ceremonyId", row.CeremonyId);
            problem.Extensions.Add("operationReference", frame.Operation.Binding.CorrelationId);
        }
        return Results.Json(problem, contentType: TechnicalProblemDetails.MediaType, statusCode: problem.Status);
    }
}
