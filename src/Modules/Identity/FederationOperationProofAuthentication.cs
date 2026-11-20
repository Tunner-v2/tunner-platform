using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class FederationOperationProofSource(FederationAttemptRow attempt, ExternalIdentityLinkRow link, bool issuance = false)
{
    internal FederationAttemptRow Attempt { get; } = attempt;
    internal ExternalIdentityLinkRow Link { get; } = link;
    internal bool Issuance { get; } = issuance;
    public override string ToString() => nameof(FederationOperationProofSource);
}

internal sealed partial class AuthenticationApplication
{
    internal bool FederatedOperationProofCurrentAt(StepUpProofRow proof, FederationOperationProofSource source,
        StepUpOperation operation, DateTimeOffset at) => FederationProofCurrentAt(proof, source, operation, at, false);
    private async Task<FederationOperationProofSource?> ReadFederationProofSourceAsync(StepUpProofRow proof, bool refresh, CancellationToken ct)
    {
        if (proof.FederationAttemptId is not { } id) return null;
        var attempt = await database.Set<FederationAttemptRow>().SingleOrDefaultAsync(value => value.AttemptId == id &&
            value.OwnerAccountId == proof.AccountId && value.OwnerSessionId == proof.SessionId &&
            value.Purpose == nameof(FederationAttemptPurpose.OperationProof), ct).ConfigureAwait(false);
        if (attempt is null) return null;
        if (refresh) await database.Entry(attempt).ReloadAsync(ct).ConfigureAwait(false);
        if (database.Entry(attempt).State == EntityState.Detached || attempt.LinkId is not { } linkId) return null;
        var link = await database.ExternalIdentityLinks.SingleOrDefaultAsync(value => value.LinkId == linkId && value.AccountId == proof.AccountId, ct).ConfigureAwait(false);
        if (link is null) return null;
        if (refresh) await database.Entry(link).ReloadAsync(ct).ConfigureAwait(false);
        return database.Entry(link).State == EntityState.Detached ? null : new(attempt, link);
    }

    private bool FederationProofCurrentAt(StepUpProofRow proof, FederationOperationProofSource? source,
        StepUpOperation operation, DateTimeOffset at, bool appliedOwnUnlink)
    {
        if (source is null || database.Database.CurrentTransaction is null) return false;
        var row = source.Attempt; var link = source.Link;
        var account = database.Accounts.Local.SingleOrDefault(value => value.AccountId == proof.AccountId);
        if (!Enum.TryParse<FederationProviderKind>(row.Provider, false, out var provider) || !Enum.IsDefined(provider) ||
            row.Provider != provider.ToString() || proof.AuthenticationMethod != "federation:" + row.Provider ||
            proof.FederationAttemptId != row.AttemptId || row.Purpose != nameof(FederationAttemptPurpose.OperationProof) ||
            row.OwnerAccountId != proof.AccountId || row.OwnerSessionId != proof.SessionId || row.Origin != proof.Origin ||
            account is null || account.Version != row.OwnerAccountVersion ||
            row.OwnerOperationId != proof.OperationId || row.OwnerContextDigest != proof.ContextDigest ||
            row.OwnerExpectedVersion != proof.ExpectedVersion || row.OwnerAssurancePolicyVersion != proof.PolicyVersion ||
            row.OwnerSecurityStampDigest != proof.SecurityStampDigest || row.AuthenticationPolicyVersion != proof.AuthenticationPolicyVersion ||
            row.LinkId != link.LinkId || row.SelectedProofLinkId != link.LinkId || row.SelectedProofLinkVersion != row.LinkVersion ||
            proof.PasskeyCredentialReference is not null || proof.PasskeyPolicyVersion is not null || proof.PasskeyAssuranceKey is not null ||
            proof.TotpAuthenticatorReference is not null || proof.TotpGeneration is not null || proof.TotpAuthenticatorVersion is not null || proof.TotpPolicyVersion is not null ||
            row.ProviderAuthenticatedAt != proof.AuthenticatedAt || row.VerificationExpiresAt is not { } verificationExpiry || proof.ExpiresAt > verificationExpiry ||
            !(row.State == "COMPLETED" && row.ResultProofId == proof.ProofId || source.Issuance && row.State == "VERIFIED" && row.ResultProofId is null)) return false;
        var profile = CurrentFederation(provider, proof.Origin, at);
        if (profile?.Values.FreshAuthentication is not { } freshness || profile.Snapshot != row.ProfileSnapshot ||
            profile.Values.StableNamespaceId != row.ProviderNamespace || profile.Values.ClientId != row.ClientId ||
            profile.Values.IssuerMappingEvidenceReference != row.MappingEvidenceReference || link.AccountId != proof.AccountId ||
            link.Provider != row.ProviderNamespace || link.QualifiedClientId != row.ClientId ||
            link.IssuerMappingEvidenceReference != row.MappingEvidenceReference ||
            link.QualificationProfileVersion != row.LinkQualificationProfileVersion ||
            link.QualificationEvidenceReference != row.LinkQualificationEvidenceReference || row.ValidatedIssuer is null ||
            row.ProviderAuthenticatedAt is not { } authenticatedAt || row.ProviderMethodsJson is null) return false;
        try
        {
            var methods = JsonSerializer.Deserialize<string[]>(row.ProviderMethodsJson);
            if (methods is null || freshness.Bind(authenticatedAt, methods, at) is not { } verified ||
                verified.Assurance != proof.Assurance || proof.ExpiresAt > verified.ExpiresAt) return false;
            _ = profile.Registration.BindExternalIdentity(row.ValidatedIssuer, row.ClientId, link.Subject);
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { return false; }
        if (link.LinkState == "ACTIVE" && link.Version == row.LinkVersion) return true;
        if (!appliedOwnUnlink || source.Issuance || operation.Scope != FederationLinkApplication.UnlinkScope ||
            operation.ResourceId != link.LinkId.ToString("N") || operation.ExpectedVersion != row.LinkVersion ||
            link.LinkState != "UNLINKED" || link.Version != operation.ExpectedVersion + 1) return false;
        // Only the exact owner mutation may retire its own proof source. The shared engine separately
        // checks the typed mutation validity and independently verifies any terminal session receipt.
        return database.ChangeTracker.Entries<FederationLinkOperationRow>().Select(value => value.Entity).Any(value =>
            value.Kind == "UNLINK" && value.State == "COMPLETED" && value.AccountId == proof.AccountId && value.SessionId == proof.SessionId &&
            value.ResultLinkId == link.LinkId && value.ResultLinkVersion == link.Version && value.ProofReference == proof.ProofId &&
            FederationLinkApplication.Operation(value).OperationId == operation.OperationId && value.ContextDigest == operation.ContextDigest);
    }
}
