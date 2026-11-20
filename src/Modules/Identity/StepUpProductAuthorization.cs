using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed partial class StepUpApplication
{
    // All methods below enlist in the already-owned native/continuation transaction; none save or commit.
    internal async Task<ProductAuthorizationPreparedProof?> PrepareProductAuthorizationProofAsync(StepUpCaller caller, ProductAuthorizationProofBinding binding,
        ProductAuthorizationPolicy currentProductPolicy, string verifiedProofKey, CancellationToken ct)
    {
        RequireProductProofTransaction();
        if (verifiedProofKey is not { Length: 64 } || !verifiedProofKey.All(Uri.IsHexDigit)) return null;
        var digest = AuthenticationIdentity.Digest(verifiedProofKey);
        var id = await database.Set<StepUpProofRow>().Where(row => row.KeyDigest == digest)
            .Select(row => (Guid?)row.ProofId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return id is { } proofId
            ? await ReadPreparedProductAuthorizationProofAsync(caller, binding, currentProductPolicy, proofId, ct).ConfigureAwait(false) : null;
    }

    internal async Task<ProductAuthorizationPreparedProof?> ReadPreparedProductAuthorizationProofAsync(StepUpCaller caller,
        ProductAuthorizationProofBinding binding, ProductAuthorizationPolicy currentProductPolicy, Guid protectedProofId, CancellationToken ct)
    {
        var current = await ReadProductProofAsync(caller, binding, currentProductPolicy, protectedProofId, false, ct).ConfigureAwait(false);
        return current is null ? null : new(current.Row.ProofId, binding.Digest, current.Mapping, current.Row.AuthenticatedAt, current.Row.ExpiresAt);
    }

    internal async Task<ProductAuthorizationConsumedProof?> ConsumeProductAuthorizationProofAsync(StepUpCaller caller,
        ProductAuthorizationProofBinding binding, ProductAuthorizationPolicy currentProductPolicy, Guid proofId, CancellationToken ct)
    {
        var checkedProof = await ReadProductProofAsync(caller, binding, currentProductPolicy, proofId, false, ct).ConfigureAwait(false);
        if (checkedProof is null) return null;
        var row = checkedProof.Row; var at = IdentityPersistenceTime.Normalize(checkedProof.At);
        End(row, "CONSUMED", at); Event(row, "StepUpProofConsumed", at);
        return new(row.ProofId, binding.Digest, row.Version, at, row.AuthenticatedAt, row.ExpiresAt, checkedProof.Mapping);
    }

    internal async Task<bool> ProductAuthorizationProofCurrentAsync(StepUpCaller caller, ProductAuthorizationProofBinding binding,
        ProductAuthorizationPolicy currentProductPolicy, ProductAuthorizationConsumedProof receipt, CancellationToken ct)
    {
        if (receipt.BindingDigest != binding.Digest) return false;
        var checkedProof = await ReadProductProofAsync(caller, binding, currentProductPolicy, receipt.ProofId, true, ct).ConfigureAwait(false);
        return checkedProof is not null && checkedProof.Row.Version == receipt.ConsumedVersion &&
            checkedProof.Row.EndedAt == receipt.ConsumedAt && checkedProof.Row.AuthenticatedAt == receipt.ProofAuthenticatedAt &&
            checkedProof.Row.ExpiresAt == receipt.ExpiresAt && JsonSerializer.Serialize(checkedProof.Mapping) == JsonSerializer.Serialize(receipt.Mapping);
    }

    internal static string ProductAuthorizationAssuranceDigest(AssurancePolicyVersion policy) =>
        AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { policy.Metadata, policy.RequiredAssurance, policy.ProofLifetime,
            MethodAssurances = policy.MethodAssurances.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() }));

    private sealed record ProductProofRead(StepUpProofRow Row, ProductAuthorizationOperationProofMapping Mapping, DateTimeOffset At);
    private async Task<ProductProofRead?> ReadProductProofAsync(StepUpCaller caller, ProductAuthorizationProofBinding binding,
        ProductAuthorizationPolicy currentProductPolicy, Guid proofId, bool consumed, CancellationToken ct)
    {
        RequireProductProofTransaction(); currentProductPolicy.Require(caller.Origin, clock.GetUtcNow()); binding.Require(caller, currentProductPolicy);
        var operation = binding.Operation;
        var session = await authentication.LockForOperationAsync(caller, operation.Scope.Operation, ct).ConfigureAwait(false);
        if (session is null || session.AuthenticatedAt != binding.SessionAuthenticatedAt ||
            session.AuthenticationMethods != binding.Mappings[0].SessionAuthenticationMethods || session.Assurance != binding.Mappings[0].SessionAssurance)
            return null;
        var account = await database.Accounts.SingleAsync(value => value.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (account.Version != binding.AccountVersion) return null;
        var row = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(value => value.ProofId == proofId, ct).ConfigureAwait(false);
        if (row is null) return null;
        // Preserve this transaction's unflushed consumption for both active and consumed reads.
        if (database.Entry(row).State != EntityState.Modified)
            await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (!Matches(row, caller, operation)) return null;
        // The exact verified method chooses its pre-bound mapping; no method or assurance is preferred by this owner.
        var mapping = binding.Mappings.SingleOrDefault(value => value.ProofAuthenticationMethod == row.AuthenticationMethod);
        if (mapping is null || row.Assurance != mapping.ProofAssurance) return null;
        var source = await authentication.ReadProofCredentialAsync(row, ct, refresh: true).ConfigureAwait(false);
        var observedAt = clock.GetUtcNow();
        var auth = authenticationPolicies.RequireEffective(observedAt); auth.Validate();
        var methodCurrent = await authentication.MethodIsCurrentAsync(session, auth, observedAt, ct).ConfigureAwait(false);
        var continuation = await authentication.VerifyContinuedOperationAsync(caller, operation, session, ct).ConfigureAwait(false);
        // A rounded-down decision can precede a session just touched at full clock precision.
        // Normalize only persisted consumption facts, not the authoritative decision instant.
        var at = clock.GetUtcNow();
        var policy = assurancePolicies.RequireEffective(operation.Scope, at, mapping.AssurancePolicyVersion);
        var currentAuth = authenticationPolicies.RequireEffective(at); currentAuth.Validate();
        currentProductPolicy.Require(caller.Origin, at);
        if (auth != currentAuth || row.AuthenticationPolicyVersion != currentAuth.Version || !methodCurrent ||
            ProductAuthorizationAssuranceDigest(policy) != binding.AssurancePolicyDigest ||
            policy.RequiredAssurance != mapping.ProofAssurance || !Valid(row, session, policy, at, consumed) ||
            session.State != "ACTIVE" || AuthenticationApplication.Expired(session, currentAuth, at) ||
            !authentication.MethodFactsCurrentAt(session, currentAuth, at) ||
            !authentication.ProofMethodCurrentAt(row, source, operation, at, false) || continuation is null ||
            !continuation.SessionUseInterval.Contains(at) || !continuation.OperationInterval.Contains(at)) return null;
        return new(row, mapping, at);
    }

    private void RequireProductProofTransaction()
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Product proof handoff/consumption requires the caller-owned Identity transaction.");
    }
}
