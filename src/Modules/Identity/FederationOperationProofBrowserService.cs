using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Identity.Persistence;

internal sealed record FederationProofChoice(string Provider, Guid LinkReference);
internal sealed record FederationProofRecovery(Guid RequestId, Guid AttemptId, string Provider, Guid? LinkReference,
    string Status, DateTimeOffset ExpiresAt, string? ErrorCode);
internal sealed record FederationProofOptions(IReadOnlyList<FederationProofChoice> Choices, FederationProofRecovery? Recovery);

public sealed partial class FederationBrowserService
{
    internal Task RequireOperationProofPostAsync(HttpContext http) => RequirePost(http);

    private static bool SameProofActor(StepUpCaller caller, StepUpCaller actual) => caller.AccountId == actual.AccountId &&
        caller.SessionId == actual.SessionId && caller.Origin == actual.Origin;

    private FederationProfileVersion? ProofProfile(FederationProviderKind provider, AssurancePolicyVersion assurance, DateTimeOffset at)
    {
        if (!enabledTransports.Contains(provider)) return null;
        try
        {
            var profile = profiles.RequireCurrent(provider, at); profile.Require(origin, at);
            return profile.Values.Provider == provider && profile.Values.FreshAuthentication is { } fresh &&
                assurance.Metadata.EffectiveInterval.Contains(at) &&
                assurance.MethodAssurances.TryGetValue("federation:" + provider, out var mapped) && mapped == assurance.RequiredAssurance &&
                fresh.QualifiedMethods.Any(value => value.Assurance == mapped) ? profile : null;
        }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
    }

    internal async Task<FederationProofOptions?> ReadOperationProofOptionsAsync(HttpContext http, StepUpCaller caller,
        UserStepUpOperationContext context, Guid requestId, AssurancePolicyVersion? assurance, CancellationToken ct)
    {
        var actual = ManagementCurrent(http, null);
        if (!SameProofActor(caller, actual.Caller)) return null;
        var choices = new List<FederationProofChoice>();
        if (!context.Completed && assurance is not null)
        {
            var database = http.RequestServices.GetRequiredService<IdentityPersistenceDbContext>();
            foreach (var provider in enabledTransports.Order())
            {
                var profile = ProofProfile(provider, assurance, clock.GetUtcNow()); if (profile is null) continue;
                var values = profile.Values;
                var links = await database.ExternalIdentityLinks.AsNoTracking().Where(value => value.AccountId == caller.AccountId &&
                    value.LinkState == "ACTIVE" && value.Provider == values.StableNamespaceId && value.QualifiedClientId == values.ClientId &&
                    value.IssuerMappingEvidenceReference == values.IssuerMappingEvidenceReference && value.QualificationProfileVersion != null &&
                    value.QualificationProfileVersion != "" && value.QualificationEvidenceReference != null && value.QualificationEvidenceReference != "")
                    .OrderBy(value => value.LinkId).Select(value => value.LinkId).ToArrayAsync(ct).ConfigureAwait(false);
                if (ProofProfile(provider, assurance, clock.GetUtcNow())?.Snapshot != profile.Snapshot) continue;
                choices.AddRange(links.Select(value => new FederationProofChoice(provider.ToString(), value)));
            }
        }
        var original = await ReadOperationProofBrowserAsync(http, caller, context, requestId, false, ct).ConfigureAwait(false);
        var recovery = ProofRecovery(original.Result, requestId);
        return choices.Count == 0 && recovery is null ? null : new(choices, recovery);
    }

    internal static FederationProofRecovery? ProofRecovery(FederationAttemptResult result, Guid requestId) => result.Row is { } row
        ? new(requestId, row.AttemptId, row.Provider, row.SelectedProofLinkId,
            result.ProjectedState ?? (row.State == "COMPLETED" ? "PROOF_ISSUED" : row.State),
            row.ResultExpiresAt ?? row.ExpiresAt, result.ErrorCode ?? row.ErrorCode) : null;

    private async Task<bool> CanSelectProofPointerAsync(HttpContext http, ManagementPointer? prior,
        (StepUpCaller Caller, FederationCurrentBrowser Browser) actual, UserStepUpOperationContext context, Guid requestId, CancellationToken ct)
    {
        var signIn = ReadPointer(http);
        if (signIn is not null && !await application.OriginalPointerIsTerminalAsync(signIn.AttemptId, signIn.BrowserKey,
            signIn.BrowserDigest, signIn.Origin, ct).ConfigureAwait(false)) return false;
        if (prior is null) return true;
        if (prior.Purpose == FederationAttemptPurpose.OperationProof)
        {
            if (prior.OperationId == requestId && prior.RequestId == requestId) return true;
            var original = await application.ReadOperationProofOriginalAsync(actual.Browser, actual.Caller, context,
                prior.RequestId, prior.AttemptId, ct).ConfigureAwait(false);
            return original.Row is { State: "REJECTED" or "EXPIRED" };
        }
        // A new Link may itself require an existing linked identity's fresh proof. Only this exact confirmed owner may hand off.
        if (management is null || context.Binding.Scope != FederationLinkApplication.LinkScope) return false;
        var native = await application.ReadManagementOriginalAsync(actual.Browser, actual.Caller, prior.AttemptId, ct).ConfigureAwait(false);
        var owned = await management.ReadAsync(actual.Caller, prior.OperationId, prior.RequestId, ct).ConfigureAwait(false);
        if (native.ErrorCode is not null || native.Row is not { State: "VERIFIED" } attempt || owned.ErrorCode is not null ||
            owned.Row is not { Kind: "LINK", State: "PENDING_PROOF", ConfirmedAt: not null } operation || operation.AttemptId != attempt.AttemptId ||
            attempt.ExpiresAt <= clock.GetUtcNow() || operation.ExpiresAt <= clock.GetUtcNow() || operation.ConfirmedAt > clock.GetUtcNow() ||
            operation.AccountId != actual.Caller.AccountId || operation.SessionId != actual.Caller.SessionId || operation.Origin != actual.Caller.Origin) return false;
        var binding = FederationLinkApplication.Operation(operation);
        return binding.OperationId == context.Binding.OperationId && binding.CorrelationId == context.Binding.CorrelationId &&
            binding.Scope == context.Binding.Scope && binding.ResourceId == context.Binding.ResourceId &&
            binding.ExpectedVersion == context.Binding.ExpectedVersion && binding.ContextDigest == context.Binding.ContextDigest;
    }

    internal async Task<(FederationAttemptResult Result, string? ChallengePath)> StartOperationProofBrowserAsync(HttpContext http,
        StepUpCaller caller, UserStepUpOperationContext context, Guid requestId, FederationProviderKind provider, Guid linkReference,
        IUserStepUpOperationSource owner, IAssurancePolicySource assurances, CancellationToken ct)
    {
        if (!enabledTransports.Contains(provider) || requestId == Guid.Empty || linkReference == Guid.Empty || context.Completed)
            return (new(null, "VERSION_CONFLICT"), null);
        var prior = ReadManagementPointer(http);
        var actual = ManagementCurrent(http, prior is not null && prior.BrowserExpiresAt > clock.GetUtcNow() ? prior : null);
        if (!SameProofActor(caller, actual.Caller) || !await CanSelectProofPointerAsync(http, prior, actual, context, requestId, ct).ConfigureAwait(false))
            return (new(null, "VERSION_CONFLICT"), null);
        var result = await application.StartOperationProofAsync(actual.Browser, actual.Caller, context, requestId, provider, linkReference,
            owner, assurances, ct).ConfigureAwait(false);
        if (result.ErrorCode is not null || result.Row is not { } row) return (result, null);
        WriteManagementPointer(http, row, requestId, FederationAttemptPurpose.OperationProof);
        return (result, row.State == "PREPARED" ? provider == FederationProviderKind.Google
            ? "/authentication/federation/google/challenge" : "/authentication/federation/microsoft/challenge" : null);
    }

    private async Task<(FederationAttemptResult Result, FederationCurrentBrowser Browser)> ReadOperationProofBrowserAsync(HttpContext http,
        StepUpCaller caller, UserStepUpOperationContext context, Guid requestId, bool restorePointer, CancellationToken ct)
    {
        var pointer = ReadManagementPointer(http);
        var actual = ManagementCurrent(http, pointer is not null && pointer.BrowserExpiresAt > clock.GetUtcNow() ? pointer : null);
        if (!SameProofActor(caller, actual.Caller)) return (new(null, "NOT_FOUND_OR_NOT_DISCLOSABLE"), actual.Browser);
        Guid? attemptId = pointer is { Purpose: FederationAttemptPurpose.OperationProof } && pointer.RequestId == requestId && pointer.OperationId == requestId
            ? pointer.AttemptId : null;
        var result = await application.ReadOperationProofOriginalAsync(actual.Browser, actual.Caller, context, requestId, attemptId, ct).ConfigureAwait(false);
        if (result.Row is not { } row) return (result, actual.Browser);
        // Only this exact server-validated request may recover original comparison values. The actual principal/cookie remain observed now.
        var original = new FederationCurrentBrowser(actual.Browser.Caller, row.BrowserKey, row.ExpiresAt, row.AuthenticationPolicyVersion,
            row.PreviousCookieDigest, row.PreviousSessionId, actual.Browser.ActualCookieDigest);
        var alreadySelected = pointer is { Purpose: FederationAttemptPurpose.OperationProof } && pointer.RequestId == requestId &&
            pointer.OperationId == requestId && pointer.AttemptId == row.AttemptId && pointer.BrowserKey == row.BrowserKey;
        if (restorePointer && !alreadySelected && result.ErrorCode is null &&
            await CanSelectProofPointerAsync(http, pointer, actual, context, requestId, ct).ConfigureAwait(false))
            WriteManagementPointer(http, row, requestId, FederationAttemptPurpose.OperationProof);
        return (result, original);
    }

    internal async Task<FederationProofRecovery?> ReadOperationProofResultAsync(HttpContext http, StepUpCaller caller,
        UserStepUpOperationContext context, Guid requestId, CancellationToken ct) =>
        ProofRecovery((await ReadOperationProofBrowserAsync(http, caller, context, requestId, true, ct).ConfigureAwait(false)).Result, requestId);

    internal async Task<StepUpIssueResult> ContinueOperationProofBrowserAsync(HttpContext http, StepUpCaller caller,
        UserStepUpOperationContext context, Guid requestId, IUserStepUpOperationSource owner, StepUpApplication stepUp, CancellationToken ct)
    {
        var original = await ReadOperationProofBrowserAsync(http, caller, context, requestId, true, ct).ConfigureAwait(false);
        if (original.Result.ErrorCode is { } error) return new(error, null);
        if (original.Result.Row is not { } row || !enabledTransports.Contains(Enum.Parse<FederationProviderKind>(row.Provider, false))) return new("STEP_UP_REQUIRED", null);
        return await application.CompleteOperationProofAsync(original.Browser, caller, context, row.AttemptId, owner, stepUp, ct).ConfigureAwait(false);
    }

    internal async Task<FederationProofRecovery?> CancelOperationProofBrowserAsync(HttpContext http, StepUpCaller caller,
        UserStepUpOperationContext context, Guid requestId, CancellationToken ct)
    {
        var original = await ReadOperationProofBrowserAsync(http, caller, context, requestId, false, ct).ConfigureAwait(false);
        if (original.Result.Row is not { } row || row.State == "COMPLETED") return null;
        var result = await application.CancelAsync(original.Browser, row.AttemptId, ct).ConfigureAwait(false);
        if (result.Row is not { State: "REJECTED" or "EXPIRED" } ended || ended.AttemptId != row.AttemptId || ended.RequestId != requestId) return null;
        var pointer = ReadManagementPointer(http);
        if (pointer is { Purpose: FederationAttemptPurpose.OperationProof } && pointer.AttemptId == row.AttemptId && pointer.RequestId == requestId)
            ClearManagementPointer(http);
        return ProofRecovery(result, requestId);
    }
}
