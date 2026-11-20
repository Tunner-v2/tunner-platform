using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

internal sealed partial class RecoveryCodeApplication(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    IAuthenticationPolicySource authenticationPolicies, IAssurancePolicySource assurancePolicies, IRecoveryCodePolicySource policies,
    IRecoveryCodeMutationAdmissionSource admissions, IRecoveryCodePresentationSource presentations, RecoveryCodeNative native, TimeProvider clock)
{
    internal static readonly AssurancePolicyScope GenerateScope = new("GENERATE_RECOVERY_CODES", "USER_BFF");
    internal static readonly AssurancePolicyScope ReplaceScope = new("REPLACE_RECOVERY_CODES", "USER_BFF");
    internal static bool Owns(AssurancePolicyScope scope) => scope == GenerateScope || scope == ReplaceScope;
    internal RecoveryCodePolicyVersion Current(string origin)
    {
        var now = clock.GetUtcNow(); var current = policies.RequireEffective(now); current.Require(origin, now);
        if (!native.Supports(current.Values.Crypto)) throw new InvalidOperationException("The selected recovery-code crypto profile is not admitted by this host.");
        return current;
    }
    internal static StepUpOperation Operation(RecoveryCodeOperationRow row) => new("OP-RECOVERY-CODE-" + row.OperationId.ToString("N"), row.OperationId.ToString("N"),
        row.Kind == "GENERATE" ? GenerateScope : ReplaceScope, row.SetReference.ToString("N"), row.ExpectedVersion, row.ContextDigest);

    internal async Task<RecoveryCodeOperationResult> StartAsync(StepUpCaller caller, string kind, RecoveryCodeStartCommand command, CancellationToken ct)
    {
        Clean();
        if (command.OwnerRequestId == Guid.Empty || command.ExpectedVersion is <= 0 or > 9_007_199_254_740_991 || kind is not ("GENERATE" or "REPLACE")) return new("VALIDATION_ERROR");
        var current = Current(caller.Origin); var policy = current.Values;
        await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, kind == "GENERATE" ? GenerateScope.Operation : ReplaceScope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(tx, new RecoveryCodeOperationResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var prior = await database.Set<RecoveryCodeOperationRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == command.OwnerRequestId, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            return await Finish(tx, new RecoveryCodeOperationResult(Actor(prior, caller) && prior.Kind == kind && prior.ExpectedVersion == command.ExpectedVersion ? null : "VERSION_CONFLICT", prior), ct).ConfigureAwait(false);
        }
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (account.Version != command.ExpectedVersion) return await Finish(tx, new RecoveryCodeOperationResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); current = Current(caller.Origin); policy = current.Values;
        if (await database.Set<RecoveryCodeOperationRow>().CountAsync(x => x.AccountId == caller.AccountId && x.State == "PENDING_PROOF" && x.ExpiresAt > now, ct).ConfigureAwait(false) >= policy.MaximumPendingOperations)
            return await Finish(tx, new RecoveryCodeOperationResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        var snapshot = await FactsAsync(caller.AccountId, policy, ct).ConfigureAwait(false);
        var decision = await AdmissionAsync(caller.AccountId, account.Version, kind, current, snapshot, ct).ConfigureAwait(false);
        if (!decision.Allowed) return await Finish(tx, new RecoveryCodeOperationResult("RECOVERY_CODES_UNAVAILABLE"), ct).ConfigureAwait(false);
        var impact = Impact(kind, snapshot, decision.RetirementTargets, policy);
        var publication = await PublishedAsync(policy, impact, ct).ConfigureAwait(false); var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
        now = clock.GetUtcNow(); current.Require(caller.Origin, now);
        if (!decision.EffectiveInterval.Contains(now)) return await Finish(tx, new RecoveryCodeOperationResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var expires = Min(now + policy.OperationLifetime, session.ExpiresAt);
        if (current.Metadata.EffectiveInterval.Until is { } until) expires = Min(expires, until);
        if (expires <= now) return await Finish(tx, new RecoveryCodeOperationResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var row = new RecoveryCodeOperationRow
        {
            OperationId = Guid.NewGuid(), RequestId = command.OwnerRequestId, AccountId = caller.AccountId, SessionId = caller.SessionId, Origin = caller.Origin,
            Kind = kind, State = "PENDING_PROOF", SetReference = Guid.NewGuid(), ExpectedVersion = account.Version, PolicyVersion = policy.Version, PolicyDigest = current.Digest(),
            AuthenticationPolicyVersion = auth.Version, SecurityStampDigest = session.SecurityStampDigest, CryptoProfileVersion = policy.Crypto.Version,
            FormatProfileVersion = policy.FormatProfileVersion, DeliveryProfileVersion = policy.DeliveryProfileVersion, CodesPerSet = policy.Crypto.CodesPerSet, MaximumCodesPerSet = policy.Crypto.MaximumCodesPerSet,
            RetirementPlanJson = PlanJson(decision.RetirementTargets), PriorSetPlanDigest = AuthenticationIdentity.Digest(PlanJson(decision.RetirementTargets)),
            ImpactFactsDigest = snapshot.Digest, PriorSetCount = impact.PriorSetCount, PriorRemainingCodeCount = impact.RemainingCodeCount,
            PublishedImpactKey = publication.ImpactKey, PublicationDigest = PublicationDigest(publication), PublicationVersion = publication.Version, AdmissionPolicyVersion = decision.PolicyVersion,
            PublicationFrom = IdentityPersistenceTime.Normalize(publication.EffectiveInterval.From), PublicationIncludesFrom = publication.EffectiveInterval.IncludesFrom,
            PublicationUntil = publication.EffectiveInterval.Until is { } publicationUntil ? IdentityPersistenceTime.Normalize(publicationUntil) : null, PublicationIncludesUntil = publication.EffectiveInterval.IncludesUntil,
            AdmissionEvidenceReference = decision.EvidenceReference, AdmissionFrom = IdentityPersistenceTime.Normalize(decision.EffectiveInterval.From), AdmissionIncludesFrom = decision.EffectiveInterval.IncludesFrom,
            AdmissionUntil = decision.EffectiveInterval.Until is { } admissionUntil ? IdentityPersistenceTime.Normalize(admissionUntil) : null, AdmissionIncludesUntil = decision.EffectiveInterval.IncludesUntil,
            AssurancePolicyVersion = kind == "GENERATE" ? policy.GenerateAssurancePolicyVersion : policy.ReplaceAssurancePolicyVersion,
            CreatedAt = now, ExpiresAt = expires, ResultRecoveryExpiresAt = expires + policy.ResultRecoveryLifetime,
            ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference
        };
        row.ContextDigest = ContextDigest(row); _ = assurancePolicies.RequireEffective(Operation(row).Scope, now, row.AssurancePolicyVersion);
        database.Add(row); Event(row, "RecoveryCodeOperationStarted", now);
        return await Finish(tx, new RecoveryCodeOperationResult(null, row), ct).ConfigureAwait(false);
    }

    internal async Task<UserStepUpOperationContext?> ResolveAsync(StepUpCaller caller, Guid id, Guid request, CancellationToken ct)
    {
        var row = await ReadAsync(caller, id, request, ct).ConfigureAwait(false);
        if (row is null || row.State is not ("PENDING_PROOF" or "COMPLETED") || row.ResultRecoveryExpiresAt <= clock.GetUtcNow()) return null;
        if (row.State == "PENDING_PROOF" && (!CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow()) || row.AuthenticationPolicyVersion != authenticationPolicies.RequireEffective(clock.GetUtcNow()).Version)) return null;
        var operation = Operation(row);
        return new(new(caller), operation.OperationId, operation.CorrelationId, operation.Scope, operation.ResourceId, operation.ExpectedVersion,
            operation.ContextDigest, row.AssurancePolicyVersion, row.State == "COMPLETED");
    }

    internal async Task<UserStepUpOperationApplyOutcome> ApplyAsync(StepUpCaller caller, UserStepUpOperationContext context, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || !Owns(context.Binding.Scope) || !Guid.TryParseExact(context.Binding.CorrelationId, "N", out var id)) return UserStepUpOperationApplyOutcome.Conflict();
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var row = await database.Set<RecoveryCodeOperationRow>().SingleOrDefaultAsync(x => x.OperationId == id, ct).ConfigureAwait(false);
        if (row is null) return UserStepUpOperationApplyOutcome.Conflict();
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false); var current = Current(caller.Origin); var policy = current.Values;
        var session = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == caller.SessionId, ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (!Actor(row, caller) || row.State != "PENDING_PROOF" || !CurrentBinding(row, current, clock.GetUtcNow()) || row.ExpectedVersion != account.Version ||
            row.AuthenticationPolicyVersion != authenticationPolicies.RequireEffective(clock.GetUtcNow()).Version ||
            row.SecurityStampDigest != session.SecurityStampDigest || Operation(row).OperationId != context.Binding.OperationId || row.ContextDigest != context.Binding.ContextDigest ||
            row.AssurancePolicyVersion != context.ExpectedPolicyVersion) return UserStepUpOperationApplyOutcome.Conflict();
        var snapshot = await FactsAsync(caller.AccountId, policy, ct).ConfigureAwait(false);
        var admissionRequest = new RecoveryCodeMutationAdmissionRequest(caller.AccountId, account.Version, row.Kind,
            current.Values.Version, snapshot.Digest, snapshot.Facts, clock.GetUtcNow());
        var decision = await ResolveAdmissionAsync(admissionRequest, current, snapshot, ct).ConfigureAwait(false);
        var plan = PlanJson(decision.RetirementTargets);
        if (!decision.Allowed || snapshot.Digest != row.ImpactFactsDigest || plan != row.RetirementPlanJson || AuthenticationIdentity.Digest(plan) != row.PriorSetPlanDigest ||
            decision.PolicyVersion != row.AdmissionPolicyVersion || decision.EvidenceReference != row.AdmissionEvidenceReference || !SameInterval(row, decision.EffectiveInterval)) return UserStepUpOperationApplyOutcome.Conflict();
        var publication = await PublishedAsync(policy, Impact(row.Kind, snapshot, decision.RetirementTargets, policy), ct).ConfigureAwait(false);
        if (publication.ImpactKey != row.PublishedImpactKey || PublicationDigest(publication) != row.PublicationDigest) return UserStepUpOperationApplyOutcome.Conflict();
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == context.Binding.OperationId, ct).ConfigureAwait(false);
        if (proof is null || proof.State != "ACTIVE" || proof.ContextDigest != row.ContextDigest || proof.AccountId != row.AccountId || proof.Origin != row.Origin) return UserStepUpOperationApplyOutcome.Conflict();
        using var prepared = native.Generate(row.AccountId, row.SetReference, ct);
        RecoveryCodeFirstDelivery? delivery = new(row, prepared); // Local owner until the complete outcome has accepted ownership.
        try
        {
            var now = clock.GetUtcNow();
            var retired = snapshot.Sets.Where(x => decision.RetirementTargets.Any(t => t.SetReference == x.SetReference)).ToArray();
            foreach (var prior in retired) { prior.State = "REPLACED"; prior.RetiredAt = now; prior.RetiredByOperationId = row.OperationId; prior.LifecycleVersion++; }
            var issued = new RecoveryCodeSetRow { SetReference = row.SetReference, AccountId = row.AccountId, IssuanceOperationId = row.OperationId, State = "ACTIVE",
                CreatedAt = now, PolicyVersion = policy.Version, CryptoProfileVersion = policy.Crypto.Version, ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference };
            database.Add(issued);
            var entries = prepared.Entries.Select(x => new RecoveryCodeEntryRow { EntryReference = x.Reference, AccountId = row.AccountId, SetReference = row.SetReference,
                CryptoProfileVersion = policy.Crypto.Version, Verifier = x.Verifier }).ToArray(); database.AddRange(entries);
            row.State = "COMPLETED"; row.CompletedAt = now; row.FirstResponseCommittedAt = now; row.ProofReference = proof.ProofId; row.Version++;
            Event(row, row.Kind == "GENERATE" ? "RecoveryCodeSetGenerated" : "RecoveryCodeSetReplaced", now);
            var validity = new RecoveryCodeExecutionValidity(database, database.Database.CurrentTransaction!, caller, context.Binding, row, current, session, account, proof,
                snapshot.Facts, snapshot.Sets, issued, entries, admissions, admissionRequest, decision, presentations, Impact(row.Kind, snapshot, decision.RetirementTargets, policy));
            var outcome = UserStepUpOperationApplyOutcome.RecoveryCodes(delivery, validity); delivery = null; return outcome;
        }
        finally { delivery?.Dispose(); }
    }

    internal async Task<RecoveryCodeOperationRow?> ReadAsync(StepUpCaller caller, Guid id, Guid request, CancellationToken ct)
    {
        var row = await database.Set<RecoveryCodeOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == id && x.RequestId == request, ct).ConfigureAwait(false);
        return row is not null && Actor(row, caller) && row.ResultRecoveryExpiresAt > clock.GetUtcNow() ? row : null;
    }
    internal async Task<RecoveryCodeOperationRow?> ReadStartAsync(StepUpCaller caller, RecoveryCodeStartResultCommand command, CancellationToken ct)
    {
        var row = await database.Set<RecoveryCodeOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == command.OwnerRequestId, ct).ConfigureAwait(false);
        return row is not null && Actor(row, caller) && row.Kind == command.Kind && row.ExpectedVersion == command.ExpectedVersion && row.ResultRecoveryExpiresAt > clock.GetUtcNow() ? row : null;
    }
    internal async Task<RecoveryCodeOperationResult> CancelAsync(StepUpCaller caller, RecoveryCodeOperationCommand command, CancellationToken ct)
    {
        Clean(); await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await authentication.LockForOperationAsync(caller, "CANCEL_RECOVERY_CODE_OPERATION", ct).ConfigureAwait(false) is null) return await Finish(tx, new RecoveryCodeOperationResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var row = await database.Set<RecoveryCodeOperationRow>().SingleOrDefaultAsync(x => x.OperationId == command.OperationId && x.RequestId == command.OwnerRequestId, ct).ConfigureAwait(false);
        if (row is null || !Actor(row, caller)) return await Finish(tx, new RecoveryCodeOperationResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (row.State == "PENDING_PROOF") { row.State = row.ExpiresAt <= clock.GetUtcNow() ? "EXPIRED" : "REJECTED"; row.Version++; Event(row, "RecoveryCodeOperationCancelled", clock.GetUtcNow()); }
        return await Finish(tx, new RecoveryCodeOperationResult(null, row), ct).ConfigureAwait(false);
    }

    internal sealed record Snapshot(RecoveryCodeSetRow[] Sets, RecoveryCodeSetFacts[] Facts, string Digest);
    internal async Task<Snapshot> FactsAsync(string account, RecoveryCodePolicy policy, CancellationToken ct)
    {
        var sets = await database.Set<RecoveryCodeSetRow>().Where(x => x.AccountId == account && x.State == "ACTIVE").OrderBy(x => x.SetReference)
            .Take(policy.MaximumActiveSets).ToArrayAsync(ct).ConfigureAwait(false);
        if (await database.Set<RecoveryCodeSetRow>().CountAsync(x => x.AccountId == account && x.State == "ACTIVE", ct).ConfigureAwait(false) > policy.MaximumActiveSets)
            throw new InvalidOperationException("Recovery-code active set capacity is not admitted.");
        var facts = new List<RecoveryCodeSetFacts>();
        foreach (var set in sets)
        {
            await database.Entry(set).ReloadAsync(ct).ConfigureAwait(false);
            var remaining = await database.Set<RecoveryCodeEntryRow>().CountAsync(x => x.AccountId == account && x.SetReference == set.SetReference && x.ConsumedAt == null, ct).ConfigureAwait(false);
            if (remaining > policy.Crypto.MaximumCodesPerSet) throw new InvalidOperationException("Recovery-code candidate capacity is not admitted.");
            facts.Add(new(set.SetReference, set.LifecycleVersion, set.UsageVersion, remaining));
        }
        var ordered = facts.OrderBy(x => x.SetReference).ToArray();
        return new(sets, ordered, AuthenticationIdentity.Digest(JsonSerializer.Serialize(ordered)));
    }
    internal Task<RecoveryCodeMutationAdmission> AdmissionAsync(string account, long version, string kind, RecoveryCodePolicyVersion current, Snapshot snapshot, CancellationToken ct) =>
        ResolveAdmissionAsync(new(account, version, kind, current.Values.Version, snapshot.Digest, snapshot.Facts, clock.GetUtcNow()), current, snapshot, ct);
    private async Task<RecoveryCodeMutationAdmission> ResolveAdmissionAsync(RecoveryCodeMutationAdmissionRequest request, RecoveryCodePolicyVersion current, Snapshot snapshot, CancellationToken ct)
    {
        var decision = await admissions.RequireAsync(request, ct).ConfigureAwait(false);
        if (decision.AccountId != request.AccountId || decision.AccountVersion != request.AccountVersion || decision.Kind != request.Kind || decision.FactsDigest != snapshot.Digest ||
            string.IsNullOrWhiteSpace(decision.PolicyVersion) || string.IsNullOrWhiteSpace(decision.EvidenceReference) || decision.EffectiveInterval is null || !decision.EffectiveInterval.Contains(clock.GetUtcNow()) ||
            decision.RetirementTargets.Count > snapshot.Sets.Length || decision.RetirementTargets.Select(x => x.SetReference).Distinct().Count() != decision.RetirementTargets.Count ||
            decision.RetirementTargets.Any(x => !snapshot.Facts.Any(f => f.SetReference == x.SetReference && f.LifecycleVersion == x.LifecycleVersion)))
            throw new InvalidOperationException("Current recovery-code owner admission is unavailable.");
        if (decision.Allowed && (request.Kind == "GENERATE" && decision.RetirementTargets.Count != 0 || request.Kind == "REPLACE" && decision.RetirementTargets.Count == 0 ||
            (long)snapshot.Sets.Length - decision.RetirementTargets.Count + 1 > current.Values.MaximumActiveSets))
            throw new InvalidOperationException("The recovery-code owner plan is not supported or exceeds approved capacity.");
        return decision;
    }
    internal static RecoveryCodeImpactFacts Impact(string kind, Snapshot snapshot, IReadOnlyCollection<RecoveryCodeRetirementTarget> targets, RecoveryCodePolicy policy) =>
        new(kind, targets.Count, snapshot.Facts.Where(x => targets.Any(t => t.SetReference == x.SetReference)).Sum(x => x.RemainingCodeCount), snapshot.Digest, policy.ReplacementMechanism, policy.SessionEffect);
    internal async ValueTask<RecoveryCodePresentation> PublishedAsync(RecoveryCodePolicy policy, RecoveryCodeImpactFacts impact, CancellationToken ct)
    {
        var at = clock.GetUtcNow(); RecoveryCodePresentation published;
        if (presentations is IRecoveryCodeAsyncPresentationSource asynchronous)
        {
            var prepared = await asynchronous.PreparePublishedAsync(policy, impact, at, ct).ConfigureAwait(false);
            if (prepared is null || !prepared.CurrentAt(database, policy, impact, clock.GetUtcNow()))
                throw new InvalidOperationException("Current transaction-bound recovery publication is unavailable.");
            published = prepared.Presentation;
        }
        else published = presentations.RequirePublished(policy, impact, at);
        if (published.ImpactFactsDigest != impact.FactsDigest || !published.EffectiveInterval.Contains(clock.GetUtcNow())) throw new InvalidOperationException("Recovery-code impact publication does not match current effective facts.");
        ct.ThrowIfCancellationRequested(); return published;
    }
    internal static string PublicationDigest(RecoveryCodePresentation value) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { value.Locale, value.ImpactKey, value.ImpactFactsDigest, value.Version, value.EffectiveInterval,
        Content = value.Content.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray() }));
    internal static string PlanJson(IEnumerable<RecoveryCodeRetirementTarget> targets) => JsonSerializer.Serialize(targets.OrderBy(x => x.SetReference));
    internal static string ContextDigest(RecoveryCodeOperationRow row) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { row.OperationId, row.RequestId, row.AccountId, row.SessionId, row.Origin, row.Kind, row.SetReference,
        row.ExpectedVersion, row.PolicyVersion, row.PolicyDigest, row.AuthenticationPolicyVersion, row.SecurityStampDigest, row.CryptoProfileVersion, row.FormatProfileVersion, row.DeliveryProfileVersion,
        row.CodesPerSet, row.MaximumCodesPerSet, row.PriorSetPlanDigest, row.ImpactFactsDigest, row.PublicationDigest, row.AdmissionPolicyVersion, row.AdmissionEvidenceReference,
        row.AdmissionFrom, row.AdmissionIncludesFrom, row.AdmissionUntil, row.AdmissionIncludesUntil, row.AssurancePolicyVersion }));
    internal static bool CurrentBinding(RecoveryCodeOperationRow row, RecoveryCodePolicyVersion current, DateTimeOffset at) => row.PolicyVersion == current.Values.Version && row.PolicyDigest == current.Digest() &&
        row.Origin == current.Values.Origin && current.Metadata.EffectiveInterval.Contains(at) && row.CreatedAt <= at && at < row.ExpiresAt && row.ContextDigest == ContextDigest(row);
    internal static bool SameInterval(RecoveryCodeOperationRow row, EffectivePolicyInterval interval) => row.AdmissionFrom == IdentityPersistenceTime.Normalize(interval.From) && row.AdmissionIncludesFrom == interval.IncludesFrom &&
        row.AdmissionUntil == (interval.Until is { } until ? IdentityPersistenceTime.Normalize(until) : (DateTimeOffset?)null) && row.AdmissionIncludesUntil == interval.IncludesUntil;
    internal Task<int> LockOwner(string account, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"recovery-code-owner:" + account},0))", ct);
    private void Clean() { if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("Recovery codes require a clean owner transaction boundary."); }
    private static bool Actor(RecoveryCodeOperationRow row, StepUpCaller caller) => row.AccountId == caller.AccountId && row.SessionId == caller.SessionId && row.Origin == caller.Origin;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private async Task<T> Finish<T>(IDbContextTransaction tx, T result, CancellationToken ct) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return result; }
    private void Event(RecoveryCodeOperationRow row, string type, DateTimeOffset at) => database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", at, "tunner/identity", row.SetReference.ToString("N"), "RESTRICTED_SECURITY",
        JsonSerializer.Serialize(new { reference = row.OperationId.ToString("N"), setReference = row.SetReference, operation = Operation(row).Scope.Operation }), row.Version.ToString(CultureInfo.InvariantCulture), Operation(row).OperationId, row.OperationId.ToString("N")));
}
