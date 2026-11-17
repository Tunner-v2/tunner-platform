using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Accounts;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryCodeStartCommand
{
    public required Guid OwnerRequestId { get; init; }
    public required long ExpectedVersion { get; init; }
    public override string ToString() => nameof(RecoveryCodeStartCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryCodeStartResultCommand
{
    public required Guid OwnerRequestId { get; init; }
    public required long ExpectedVersion { get; init; }
    public required string Kind { get; init; }
    public override string ToString() => nameof(RecoveryCodeStartResultCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryCodeOperationCommand
{
    public required Guid OwnerRequestId { get; init; }
    public required string OperationReference { get; init; }
    [JsonIgnore] internal Guid OperationId => OperationReference is { Length: 32 } && OperationReference.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        Guid.TryParseExact(OperationReference, "N", out var id) ? id : Guid.Empty;
    public override string ToString() => nameof(RecoveryCodeOperationCommand);
}
internal sealed record RecoveryCodeOperationResult(string? ErrorCode, RecoveryCodeOperationRow? Operation = null);

/// <summary>Closed request-owned value. No public getter, record printing, generic JSON DTO or later read grant.</summary>
internal sealed class RecoveryCodeFirstDelivery : IDisposable
{
    private readonly Guid ownerRequestId;
    private readonly Guid operationId;
    private readonly Guid sessionId;
    private readonly Guid setReference;
    private readonly string accountId;
    private readonly string origin;
    private readonly string contextDigest;
    private readonly string deliveryProfileVersion;
    private readonly string formatProfileVersion;
    private readonly string kind;
    private readonly long expectedVersion;
    private readonly int count;
    private RecoveryCodeValue[]? values;
    private int emitted;
    internal RecoveryCodeFirstDelivery(RecoveryCodeOperationRow row, RecoveryCodePreparedSet prepared)
    {
        if (prepared.Entries.Count != row.CodesPerSet || row.CodesPerSet <= 0 || row.CodesPerSet > row.MaximumCodesPerSet || row.FormatProfileVersion != "NATIVE_HEX32_V1")
            throw new InvalidOperationException("Recovery first delivery does not match its bound profile.");
        ownerRequestId = row.RequestId; operationId = row.OperationId; sessionId = row.SessionId; setReference = row.SetReference;
        accountId = row.AccountId; origin = row.Origin; contextDigest = row.ContextDigest; deliveryProfileVersion = row.DeliveryProfileVersion;
        formatProfileVersion = row.FormatProfileVersion; kind = row.Kind; expectedVersion = row.ExpectedVersion; count = row.CodesPerSet;
        var owned = new List<RecoveryCodeValue>();
        try
        {
            foreach (var entry in prepared.Entries) owned.Add(new(entry.Code.Span.ToArray()));
            values = owned.ToArray(); owned.Clear();
        }
        finally { foreach (var value in owned) value.Dispose(); }
    }
    internal bool Matches(StepUpCaller actor, StepUpOperation operation) => values is not null && emitted == 0 &&
        actor.AccountId == accountId && actor.SessionId == sessionId && actor.Origin == origin && operation.OperationId == "OP-RECOVERY-CODE-" + operationId.ToString("N") &&
        operation.CorrelationId == operationId.ToString("N") && operation.ResourceId == setReference.ToString("N") && operation.ContextDigest == contextDigest &&
        operation.ExpectedVersion == expectedVersion && operation.Scope == (kind == "GENERATE" ? RecoveryCodeApplication.GenerateScope : RecoveryCodeApplication.ReplaceScope);
    internal bool Matches(RecoveryCodeOperationRow row) => values is not null && emitted == 0 && ownerRequestId == row.RequestId && operationId == row.OperationId &&
        accountId == row.AccountId && sessionId == row.SessionId && origin == row.Origin && setReference == row.SetReference && contextDigest == row.ContextDigest &&
        deliveryProfileVersion == row.DeliveryProfileVersion && formatProfileVersion == row.FormatProfileVersion && count == row.CodesPerSet && kind == row.Kind && expectedVersion == row.ExpectedVersion;
    internal void Write(Utf8JsonWriter writer, Guid stepUpRequestId)
    {
        var held = values ?? throw new ObjectDisposedException(nameof(RecoveryCodeFirstDelivery));
        if (stepUpRequestId == Guid.Empty || Interlocked.Exchange(ref emitted, 1) != 0) throw new InvalidOperationException("Recovery first delivery is one-use.");
        writer.WriteStartObject(); writer.WriteString("kind", "RECOVERY_CODES_FIRST_DELIVERY_V1"); writer.WriteString("ownerRequestId", ownerRequestId);
        writer.WriteString("stepUpRequestId", stepUpRequestId); writer.WriteString("operationReference", operationId.ToString("N")); writer.WriteString("setReference", setReference);
        writer.WriteString("ownerContextDigest", contextDigest); writer.WriteString("deliveryProfileVersion", deliveryProfileVersion); writer.WriteString("formatProfileVersion", formatProfileVersion);
        writer.WriteStartArray("codes"); foreach (var value in held) writer.WriteStringValue(value.Span); writer.WriteEndArray(); writer.WriteEndObject();
    }
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref values, null); if (owned is not null) foreach (var value in owned) value.Dispose();
    }
    public override string ToString() => nameof(RecoveryCodeFirstDelivery);
}

/// <summary>Exact transaction-pinned recovery witness checked synchronously at the shared last decision instant.</summary>
internal sealed class RecoveryCodeExecutionValidity
{
    private readonly IdentityPersistenceDbContext database;
    private readonly IDbContextTransaction transaction;
    private readonly string accountId;
    private readonly Guid sessionId;
    private readonly string origin;
    private readonly string operationId;
    private readonly string contextDigest;
    private readonly RecoveryCodeOperationRow operation;
    private readonly string policyDigest;
    private readonly RecoveryCodePolicy originalPolicy;
    private readonly EffectivePolicyInterval originalPolicyInterval;
    private readonly AuthenticationSessionRow session;
    private readonly long sessionVersion;
    private readonly AccountRow account;
    private readonly AccountState accountState;
    private readonly StepUpProofRow proof;
    private readonly RecoveryCodeSetFacts[] priorFacts;
    private readonly RecoveryCodeSetRow[] priorSets;
    private readonly RecoveryCodeSetRow issued;
    private readonly RecoveryCodeEntryRow[] entries;
    private readonly (Guid Reference, string Verifier, string Profile)[] expectedEntries;
    private readonly IRecoveryCodeMutationAdmissionSource admissions;
    private readonly RecoveryCodeMutationAdmissionRequest originalAdmissionRequest;
    private readonly RecoveryCodeMutationAdmission originalAdmission;
    private readonly IRecoveryCodePresentationSource publications;
    private readonly RecoveryCodeImpactFacts impact;
    internal RecoveryCodeExecutionValidity(IdentityPersistenceDbContext database, IDbContextTransaction transaction, StepUpCaller actor, StepUpOperation binding,
        RecoveryCodeOperationRow operation, RecoveryCodePolicyVersion policy, AuthenticationSessionRow session, AccountRow account, StepUpProofRow proof,
        RecoveryCodeSetFacts[] priorFacts, RecoveryCodeSetRow[] priorSets, RecoveryCodeSetRow issued, RecoveryCodeEntryRow[] entries, IRecoveryCodeMutationAdmissionSource admissions,
        RecoveryCodeMutationAdmissionRequest originalAdmissionRequest, RecoveryCodeMutationAdmission originalAdmission,
        IRecoveryCodePresentationSource publications, RecoveryCodeImpactFacts impact)
    {
        this.database = database; this.transaction = transaction; accountId = actor.AccountId; sessionId = actor.SessionId; origin = actor.Origin;
        operationId = binding.OperationId; contextDigest = binding.ContextDigest; this.operation = operation; policyDigest = policy.Digest();
        originalPolicy = policy.Values; originalPolicyInterval = policy.Metadata.EffectiveInterval;
        this.session = session; sessionVersion = session.Version; this.account = account; accountState = account.State; this.proof = proof;
        this.priorFacts = priorFacts.ToArray(); this.priorSets = priorSets.ToArray(); this.issued = issued; this.entries = entries.ToArray();
        expectedEntries = entries.Select(x => (x.EntryReference, x.Verifier, x.CryptoProfileVersion)).ToArray();
        this.admissions = admissions; this.originalAdmissionRequest = originalAdmissionRequest; this.originalAdmission = originalAdmission;
        this.publications = publications; this.impact = impact;
    }
    internal bool Matches(RecoveryCodeFirstDelivery delivery) => delivery.Matches(operation);
    internal async ValueTask<RecoveryCodePreparedPresentation?> PreparePublicationAsync(IRecoveryCodePolicySource? policies, DateTimeOffset at, CancellationToken ct)
    {
        if (publications is not IRecoveryCodeAsyncPresentationSource asynchronous) return null;
        if (policies is null) throw new InvalidOperationException("Current recovery policy is required.");
        var current = policies.RequireEffective(at); current.Require(origin, at);
        return await asynchronous.PreparePublishedAsync(current.Values, impact, at, ct).ConfigureAwait(false);
    }
    // After SaveChanges only local immutable bounds and the exact transaction are checked; pre-save tracking states no longer apply.
    internal bool CommittedBoundsCurrentAt(RecoveryCodePreparedPresentation prepared, AuthenticationPolicy authenticationPolicy,
        AssurancePolicyVersion assurancePolicy, EffectivePolicyInterval sessionUseInterval, EffectivePolicyInterval operationInterval, DateTimeOffset at) =>
        sessionUseInterval.Contains(at) && operationInterval.Contains(at) && prepared.CurrentAt(database, originalPolicy, impact, at) && originalPolicyInterval.Contains(at) && originalAdmission.EffectiveInterval.Contains(at) &&
        assurancePolicy.Metadata.EffectiveInterval.Contains(at) && at >= operation.CreatedAt && at < operation.ExpiresAt && at < proof.ExpiresAt &&
        !AuthenticationApplication.Expired(session, authenticationPolicy, at) && operation.State == "COMPLETED" && proof.State == "CONSUMED";

    internal bool CurrentAt(IdentityPersistenceDbContext db, StepUpCaller actor, StepUpOperation binding, IRecoveryCodePolicySource? policies,
        AuthenticationPolicy authenticationPolicy, DateTimeOffset at, RecoveryCodePreparedPresentation? prepared = null)
    {
        if (policies is null || !ReferenceEquals(db, database) || !ReferenceEquals(db.Database.CurrentTransaction, transaction) ||
            actor.AccountId != accountId || actor.SessionId != sessionId || actor.Origin != origin || binding.OperationId != operationId || binding.ContextDigest != contextDigest ||
            !RecoveryCodeApplication.Owns(binding.Scope)) return false;
        var current = policies.RequireEffective(at); current.Require(origin, at);
        RecoveryCodePresentation published;
        if (publications is IRecoveryCodeAsyncPresentationSource)
        {
            if (prepared is null || !prepared.CurrentAt(db, current.Values, impact, at)) return false;
            published = prepared.Presentation;
        }
        else published = publications.RequirePublished(current.Values, impact, at);
        if (!published.EffectiveInterval.Contains(at) || published.Version != operation.PublicationVersion || published.ImpactFactsDigest != impact.FactsDigest ||
            published.ImpactKey != operation.PublishedImpactKey || RecoveryCodeApplication.PublicationDigest(published) != operation.PublicationDigest) return false;
        // Resolve again only from the immutable request observed before this owner mutated its tracked rows.
        var request = new RecoveryCodeMutationAdmissionRequest(originalAdmissionRequest.AccountId, originalAdmissionRequest.AccountVersion,
            originalAdmissionRequest.Kind, originalAdmissionRequest.PolicyVersion, originalAdmissionRequest.FactsDigest, originalAdmissionRequest.ActiveSets, at);
        var admission = admissions.RequireCurrent(request);
        if (admission is null || !originalAdmission.Allowed || !admission.Allowed || admission.AccountId != originalAdmissionRequest.AccountId ||
            admission.AccountVersion != originalAdmissionRequest.AccountVersion || admission.Kind != originalAdmissionRequest.Kind || admission.FactsDigest != originalAdmissionRequest.FactsDigest ||
            originalAdmissionRequest.AccountId != accountId || originalAdmissionRequest.AccountVersion != operation.ExpectedVersion || originalAdmissionRequest.Kind != operation.Kind ||
            originalAdmissionRequest.PolicyVersion != current.Values.Version || originalAdmissionRequest.FactsDigest != operation.ImpactFactsDigest ||
            admission.PolicyVersion != originalAdmission.PolicyVersion || admission.PolicyVersion != operation.AdmissionPolicyVersion ||
            admission.EvidenceReference != originalAdmission.EvidenceReference || admission.EvidenceReference != operation.AdmissionEvidenceReference ||
            admission.RetirementTargets.Count != originalAdmission.RetirementTargets.Count ||
            RecoveryCodeApplication.PlanJson(admission.RetirementTargets) != RecoveryCodeApplication.PlanJson(originalAdmission.RetirementTargets) ||
            RecoveryCodeApplication.PlanJson(admission.RetirementTargets) != operation.RetirementPlanJson || admission.EffectiveInterval is null ||
            !originalAdmission.EffectiveInterval.Contains(at) || !admission.EffectiveInterval.Contains(at) ||
            !RecoveryCodeApplication.SameInterval(operation, originalAdmission.EffectiveInterval) || !RecoveryCodeApplication.SameInterval(operation, admission.EffectiveInterval)) return false;
        if (current.Digest() != policyDigest || !RecoveryCodeApplication.CurrentBinding(operation, current, at) || operation.State != "COMPLETED" || operation.Version != 2 ||
            operation.ProofReference != proof.ProofId || operation.CompletedAt is null || operation.CompletedAt > at || operation.FirstResponseCommittedAt != operation.CompletedAt ||
            operation.AuthenticationPolicyVersion != authenticationPolicy.Version || session.State != "ACTIVE" || session.AccountId != accountId || session.SessionId != sessionId ||
            session.Origin != origin || session.Version != sessionVersion || session.SecurityStampDigest != operation.SecurityStampDigest ||
            account.AccountId != accountId || account.Version != operation.ExpectedVersion || account.State != accountState ||
            proof.State != "ACTIVE" || proof.AccountId != accountId || proof.SessionId != sessionId || proof.OperationId != operationId || proof.ContextDigest != contextDigest) return false;
        db.ChangeTracker.DetectChanges();
        if (db.Entry(operation).State != EntityState.Modified || db.Entry(issued).State != EntityState.Added || issued.AccountId != accountId || issued.SetReference != operation.SetReference ||
            issued.IssuanceOperationId != operation.OperationId || issued.State != "ACTIVE" || issued.LifecycleVersion != 1 || issued.UsageVersion != 0 ||
            issued.PolicyVersion != current.Values.Version || issued.CryptoProfileVersion != current.Values.Crypto.Version || issued.CreatedAt != operation.CompletedAt || issued.RetiredAt is not null || issued.RetiredByOperationId is not null ||
            entries.Length != current.Values.Crypto.CodesPerSet || entries.Select(x => x.EntryReference).Distinct().Count() != entries.Length) return false;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i]; var expected = expectedEntries[i];
            if (db.Entry(entry).State != EntityState.Added || entry.AccountId != accountId || entry.SetReference != issued.SetReference || entry.EntryReference != expected.Reference ||
                entry.Verifier != expected.Verifier || entry.CryptoProfileVersion != expected.Profile || entry.ConsumedAt is not null || entry.ConsumedByOperationId is not null || entry.ConsumptionCorrelationId is not null ||
                entry.ConsumptionContextDigest is not null || entry.ConsumptionPolicyVersion is not null || entry.ConsumptionOwnerPolicyVersion is not null || entry.ConsumptionOutcomeReference is not null || entry.ConsumptionSetLifecycleVersion is not null || entry.Version != 1) return false;
        }
        var targets = JsonSerializer.Deserialize<RecoveryCodeRetirementTarget[]>(operation.RetirementPlanJson);
        if (targets is null || priorSets.Length != priorFacts.Length) return false;
        foreach (var set in priorSets)
        {
            var fact = priorFacts.SingleOrDefault(x => x.SetReference == set.SetReference); if (fact is null || set.AccountId != accountId || set.UsageVersion != fact.UsageVersion) return false;
            var retire = targets.Any(x => x.SetReference == set.SetReference && x.LifecycleVersion == fact.LifecycleVersion);
            if (retire ? set.State != "REPLACED" || set.LifecycleVersion != fact.LifecycleVersion + 1 || set.RetiredByOperationId != operation.OperationId || set.RetiredAt is null || db.Entry(set).State != EntityState.Modified
                : set.State != "ACTIVE" || set.LifecycleVersion != fact.LifecycleVersion || set.RetiredAt is not null || set.RetiredByOperationId is not null || db.Entry(set).State != EntityState.Unchanged) return false;
        }
        return targets.Length == priorSets.Count(x => x.State == "REPLACED") && (long)priorSets.Length - targets.Length + 1 <= current.Values.MaximumActiveSets;
    }
    public override string ToString() => nameof(RecoveryCodeExecutionValidity);
}
