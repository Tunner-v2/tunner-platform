using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Tunner.Identity.Persistence;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class FederationProofStartCommand
{
    public required Guid RequestId { get; init; }
    public required string Provider { get; init; }
    public required Guid LinkReference { get; init; }
    public override string ToString() => nameof(FederationProofStartCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class FederationProofRequestCommand
{
    public required Guid RequestId { get; init; }
    public override string ToString() => nameof(FederationProofRequestCommand);
}

internal sealed partial class StepUpBrowserService
{
    private FederationBrowserService? FederationProofModule()
    {
        var values = federationApplications?.Take(2).ToArray();
        return values is { Length: 1 } ? values[0] : null;
    }

    private static async Task<bool> FederationPostAllowedAsync(HttpContext http, FederationBrowserService native)
    {
        try { await native.RequireOperationProofPostAsync(http).ConfigureAwait(false); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private async Task<FederationProofOptions?> ReadFederationOptionsAsync(HttpContext http, StepUpBrowserOperation frame,
        AssurancePolicyVersion? policy = null, StepUpPresentation? presentation = null)
    {
        var native = FederationProofModule(); if (native is null) return null;
        try
        {
            policy ??= assurancePolicies.RequireEffective(frame.Operation.Binding.Scope, clock.GetUtcNow(), frame.Operation.ExpectedPolicyVersion);
            presentation ??= presentations.RequirePublished(policy);
            var result = await native.ReadOperationProofOptionsAsync(http, frame.Caller, frame.Operation, frame.Browser.RequestId,
                frame.Operation.Completed ? null : policy, http.RequestAborted).ConfigureAwait(false);
            if (result is not null) RequireFederationPresentation(presentation, result);
            return result;
        }
        catch (ArgumentException) { return null; } // Only this optional method is unavailable; other published methods remain usable.
        catch (InvalidOperationException) { return null; }
    }

    private static readonly string[] FederationPresentationKeys = ["stepUp.federation.description", "stepUp.federation.source",
        "stepUp.federation.pending", "stepUp.federation.unknown", "stepUp.federation.check", "stepUp.federation.continue", "stepUp.federation.cancel"];
    private static void RequireFederationPresentation(StepUpPresentation presentation, FederationProofOptions options)
    {
        var providers = options.Choices.Select(value => value.Provider)
            .Concat(options.Recovery is { } recovery ? [recovery.Provider] : []).Distinct(StringComparer.Ordinal);
        if (FederationPresentationKeys.Any(key => !presentation.Content.ContainsKey(key)) ||
            providers.Any(provider => provider is not ("Google" or "Microsoft") || !presentation.Content.ContainsKey("stepUp.federation." + provider)))
            throw new ArgumentException("Federation operation proof requires its published method, source and recovery variants.");
    }

    private async Task<bool> FederationPresentationAvailableAsync(HttpContext http, StepUpBrowserOperation frame, FederationBrowserService native,
        string? selectedProvider = null)
    {
        var policy = assurancePolicies.RequireEffective(frame.Operation.Binding.Scope, clock.GetUtcNow(), frame.Operation.ExpectedPolicyVersion);
        var presentation = presentations.RequirePublished(policy);
        var options = await native.ReadOperationProofOptionsAsync(http, frame.Caller, frame.Operation, frame.Browser.RequestId,
            frame.Operation.Completed ? null : policy, http.RequestAborted).ConfigureAwait(false);
        if (options is null) return false;
        try
        {
            RequireFederationPresentation(presentation, options);
            return selectedProvider is null || options.Choices.Any(value => value.Provider == selectedProvider);
        }
        catch (ArgumentException) { return false; }
    }

    private static IResult FederationOwnerCompleted(StepUpBrowserOperation frame) =>
        Results.Json(new { status = "COMPLETED", operationReference = frame.Operation.Binding.CorrelationId });

    internal Task<IResult> FederationStartAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = FederationProofModule(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!await FederationPostAllowedAsync(http, native).ConfigureAwait(false)) return Problem(403, "FORBIDDEN_SCOPE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<FederationProofStartCommand>(http, 4096).ConfigureAwait(false);
        if (command.RequestId == Guid.Empty || command.LinkReference == Guid.Empty || command.Provider is not ("Google" or "Microsoft"))
            return Problem(400, "VALIDATION_ERROR");
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Failure("VERSION_CONFLICT");
        if (frame.Operation.Completed) return FederationOwnerCompleted(frame);
        if (!await FederationPresentationAvailableAsync(http, frame, native, command.Provider).ConfigureAwait(false)) return Failure("STEP_UP_REQUIRED");
        var provider = command.Provider == "Google" ? FederationProviderKind.Google : FederationProviderKind.Microsoft;
        var started = await native.StartOperationProofBrowserAsync(http, frame.Caller, frame.Operation, command.RequestId, provider,
            command.LinkReference, owner, assurancePolicies, http.RequestAborted).ConfigureAwait(false);
        if (started.Result.ErrorCode is { } error) return Failure(error);
        var recovery = FederationBrowserService.ProofRecovery(started.Result, command.RequestId);
        return Results.Json(new { requestId = command.RequestId, operationReference = frame.Operation.Binding.CorrelationId,
            federation = recovery, challengePath = started.ChallengePath });
    });

    internal Task<IResult> FederationResultAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = FederationProofModule(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!await FederationPostAllowedAsync(http, native).ConfigureAwait(false)) return Problem(403, "FORBIDDEN_SCOPE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<FederationProofRequestCommand>(http, 4096).ConfigureAwait(false);
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Failure("VERSION_CONFLICT");
        if (frame.Operation.Completed) return FederationOwnerCompleted(frame);
        var recovery = await native.ReadOperationProofResultAsync(http, frame.Caller, frame.Operation, command.RequestId, http.RequestAborted).ConfigureAwait(false);
        return recovery is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : Results.Json(new
            { requestId = command.RequestId, operationReference = frame.Operation.Binding.CorrelationId, federation = recovery });
    });

    internal Task<IResult> FederationContinueAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = FederationProofModule(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!await FederationPostAllowedAsync(http, native).ConfigureAwait(false)) return Problem(403, "FORBIDDEN_SCOPE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<FederationProofRequestCommand>(http, 4096).ConfigureAwait(false);
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Failure("VERSION_CONFLICT");
        if (frame.Operation.Completed) return FederationOwnerCompleted(frame);
        if (!await FederationPresentationAvailableAsync(http, frame, native).ConfigureAwait(false)) return Failure("STEP_UP_REQUIRED");
        var issued = await native.ContinueOperationProofBrowserAsync(http, frame.Caller, frame.Operation, command.RequestId, owner, application,
            http.RequestAborted).ConfigureAwait(false);
        if (issued.ErrorCode is { } error)
        {
            if (await CompletedAsync(frame.Actor, frame.Browser.BindingDigest, http.RequestAborted).ConfigureAwait(false)) return FederationOwnerCompleted(frame);
            return Failure(error);
        }
        if (issued.Grant is null) return Failure("STEP_UP_REQUIRED");
        return await ExecuteProofAsync(http, frame, issued.Grant.Key).ConfigureAwait(false);
    });

    internal Task<IResult> FederationCancelAsync(HttpContext http) => GuardAsync(http, async () =>
    {
        var native = FederationProofModule(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!await FederationPostAllowedAsync(http, native).ConfigureAwait(false)) return Problem(403, "FORBIDDEN_SCOPE");
        if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<FederationProofRequestCommand>(http, 4096).ConfigureAwait(false);
        var frame = await CurrentProofOperationAsync(http, command.RequestId).ConfigureAwait(false); if (frame is null) return Failure("VERSION_CONFLICT");
        var ended = await native.CancelOperationProofBrowserAsync(http, frame.Caller, frame.Operation, command.RequestId, http.RequestAborted).ConfigureAwait(false);
        if (ended is null) return Failure("VERSION_CONFLICT");
        http.Response.Cookies.Delete(StepUpBrowserEndpoints.CookieName,
            new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true });
        return Results.Json(new { status = ended.Status, requestId = command.RequestId, operationReference = frame.Operation.Binding.CorrelationId });
    });
}
