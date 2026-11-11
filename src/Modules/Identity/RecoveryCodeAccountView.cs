using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed record RecoveryCodeAccountView(RecoveryCodePolicy Policy, long ExpectedVersion, int ActiveSetCount, int RemainingCodeCount,
    bool CanGenerate, bool CanReplace, RecoveryCodePresentation Presentation, RecoveryCodeOperationRow? Recovery, bool RecoveryCanContinue);

internal sealed partial class RecoveryCodeApplication
{
    internal async Task<RecoveryCodeAccountView?> AccountViewAsync(StepUpCaller caller, Guid? selectedId, Guid? selectedRequest, CancellationToken ct)
    {
        Clean(); var current = Current(caller.Origin);
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await authentication.LockForOperationAsync(caller, "LIST_RECOVERY_CODES", ct).ConfigureAwait(false) is null) return null;
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (account.Version is <= 0 or > 9_007_199_254_740_991) throw new InvalidOperationException("Recovery account version cannot be represented by this browser contract.");
        var snapshot = await FactsAsync(caller.AccountId, current.Values, ct).ConfigureAwait(false);
        var generate = await AdmissionAsync(caller.AccountId, account.Version, "GENERATE", current, snapshot, ct).ConfigureAwait(false);
        var replace = await AdmissionAsync(caller.AccountId, account.Version, "REPLACE", current, snapshot, ct).ConfigureAwait(false);
        var selected = selectedId is { } id && selectedRequest is { } request ? await ReadAsync(caller, id, request, ct).ConfigureAwait(false) : null;
        var decision = selected?.Kind == "REPLACE" ? replace : selected?.Kind == "GENERATE" ? generate : replace.Allowed ? replace : generate;
        var kind = selected?.Kind ?? (replace.Allowed ? "REPLACE" : "GENERATE");
        var publication = Published(current.Values, Impact(kind, snapshot, decision.RetirementTargets, current.Values));
        var canContinue = selected is not null && selected.State == "PENDING_PROOF" && decision.Allowed && selected.ExpectedVersion == account.Version &&
            CurrentBinding(selected, current, clock.GetUtcNow()) && selected.AuthenticationPolicyVersion == authenticationPolicies.RequireEffective(clock.GetUtcNow()).Version &&
            selected.ImpactFactsDigest == snapshot.Digest && selected.RetirementPlanJson == PlanJson(decision.RetirementTargets) &&
            selected.AdmissionPolicyVersion == decision.PolicyVersion && selected.AdmissionEvidenceReference == decision.EvidenceReference && SameInterval(selected, decision.EffectiveInterval) &&
            selected.PublishedImpactKey == publication.ImpactKey && selected.PublicationVersion == publication.Version && selected.PublicationDigest == PublicationDigest(publication);
        return await Finish(tx, new RecoveryCodeAccountView(current.Values, account.Version, snapshot.Sets.Length, snapshot.Facts.Sum(x => x.RemainingCodeCount),
            generate.Allowed, replace.Allowed, publication, selected, canContinue), ct).ConfigureAwait(false);
    }
}
