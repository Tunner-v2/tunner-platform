using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

internal sealed partial class TotpApplication(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    UserManager<IdentityUserAdapter> users, ITotpPolicySource policies, IAuthenticationPolicySource authenticationPolicies,
    IAssurancePolicySource assurancePolicies, ITotpMutationEligibilitySource eligibility, ITotpSeedStore seeds,
    StepUpApplication stepUp, TotpNativeVerifier verifier, TimeProvider clock)
{
    internal static readonly AssurancePolicyScope EnrollmentScope = new("ENROLL_TOTP", "USER_BFF");
    internal static readonly AssurancePolicyScope DisableScope = new("DISABLE_TOTP", "USER_BFF");
    internal static readonly AssurancePolicyScope ResetScope = new("RESET_TOTP", "USER_BFF");
    internal TotpPolicy Current(string origin)
    {
        var policy = policies.RequireEffective(clock.GetUtcNow()); policy.Validate();
        if (policy.Origin != origin) throw new InvalidOperationException("TOTP profile does not match this origin."); return policy;
    }
    internal static StepUpOperation Operation(TotpOperationRow row) => new("OP-TOTP-" + row.EnrollmentId.ToString("N"), row.EnrollmentId.ToString("N"),
        row.Kind switch { "ENROLL" => EnrollmentScope, "DISABLE" => DisableScope, _ => ResetScope },
        row.Kind == "ENROLL" ? row.EnrollmentId.ToString("N") : row.AuthenticatorReference.ToString("N"), row.ExpectedVersion,
        AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { row.EnrollmentId, row.RequestId, row.AccountId, row.SessionId, row.Origin, row.Kind,
            row.PolicyVersion, row.AuthenticatorReference, row.Generation, row.ExpectedVersion })));
    internal async Task<TotpOperationResult> StartAsync(StepUpCaller caller, string kind, TotpStartCommand command, CancellationToken ct)
    {
        Clean(); if (command.RequestId == Guid.Empty || kind is not ("ENROLL" or "DISABLE" or "RESET") ||
            (kind == "ENROLL" ? command.AuthenticatorReference is not null || command.ExpectedVersion != 0 : command.AuthenticatorReference is null || command.AuthenticatorReference == Guid.Empty || command.ExpectedVersion < 2))
            return new("VALIDATION_ERROR");
        var policy = Current(caller.Origin); var now = clock.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var scope = kind switch { "ENROLL" => EnrollmentScope, "DISABLE" => DisableScope, _ => ResetScope };
        var session = await authentication.LockForOperationAsync(caller, scope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(transaction, new TotpOperationResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var prior = await database.Set<TotpOperationRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == command.RequestId, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            return await Finish(transaction, new TotpOperationResult(Actor(prior, caller) && prior.Kind == kind && prior.ExpectedVersion == command.ExpectedVersion &&
                (kind == "ENROLL" || prior.AuthenticatorReference == command.AuthenticatorReference) ? null : "VERSION_CONFLICT", prior), ct).ConfigureAwait(false);
        }
        if (await database.Set<TotpOperationRow>().CountAsync(x => x.AccountId == caller.AccountId && x.EndedAt == null && x.ExpiresAt > now, ct).ConfigureAwait(false) >= policy.MaximumPendingOperations)
            return await Finish(transaction, new TotpOperationResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        if (kind == "ENROLL" && !await EnrollmentAllowed(caller.AccountId, policy, ct).ConfigureAwait(false))
            return await Finish(transaction, new TotpOperationResult("TOTP_LIMIT_REACHED"), ct).ConfigureAwait(false);
        var source = kind == "ENROLL" ? null : await database.Set<TotpAuthenticatorRow>().SingleOrDefaultAsync(x => x.AuthenticatorReference == command.AuthenticatorReference && x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (kind != "ENROLL" && (source?.State != "ACTIVE" || source.Version != command.ExpectedVersion)) return await Finish(transaction, new TotpOperationResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        var row = new TotpOperationRow { EnrollmentId = Guid.NewGuid(), RequestId = command.RequestId, AccountId = caller.AccountId, SessionId = caller.SessionId,
            Origin = caller.Origin, Kind = kind, AuthenticatorReference = source?.AuthenticatorReference ?? Guid.NewGuid(), Generation = source?.Generation ?? Guid.NewGuid(),
            ExpectedVersion = command.ExpectedVersion, PolicyVersion = policy.Version, AuthenticationPolicyVersion = auth.Version, SecurityStampDigest = session.SecurityStampDigest,
            AssurancePolicyVersion = kind switch { "ENROLL" => policy.EnrollmentAssurancePolicyVersion, "DISABLE" => policy.DisableAssurancePolicyVersion, _ => policy.ResetAssurancePolicyVersion },
            CreatedAt = now, ExpiresAt = Min(now + policy.EnrollmentLifetime, session.ExpiresAt), RecoveryExpiresAt = now + policy.EnrollmentLifetime + policy.ResultRecoveryLifetime,
            ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference };
        _ = assurancePolicies.RequireEffective(scope, now, row.AssurancePolicyVersion);
        database.Add(row); Event(row, "TotpOperationStarted", now); return await Finish(transaction, new TotpOperationResult(null, row), ct).ConfigureAwait(false);
    }
    internal async Task<UserStepUpOperationContext?> ResolveAsync(StepUpCaller caller, Guid id, Guid request, CancellationToken ct)
    {
        var row = await ReadAsync(caller, id, request, ct).ConfigureAwait(false); if (row is null || row.State is "EXPIRED" or "REJECTED") return null;
        if (!CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow())) return null; var operation = Operation(row);
        return new(new(caller), operation.OperationId, operation.CorrelationId, operation.Scope, operation.ResourceId, operation.ExpectedVersion, operation.ContextDigest,
            row.AssurancePolicyVersion, row.State != "PENDING_PROOF");
    }
    internal async Task<UserStepUpOperationApplyOutcome> ApplyAsync(StepUpCaller caller, UserStepUpOperationContext context, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || !Guid.TryParseExact(context.Binding.CorrelationId, "N", out var id)) return UserStepUpOperationApplyOutcome.Conflict();
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
        var row = await database.Set<TotpOperationRow>().SingleOrDefaultAsync(x => x.EnrollmentId == id, ct).ConfigureAwait(false); if (row is null) return UserStepUpOperationApplyOutcome.Conflict();
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false); var policy = Current(caller.Origin); var now = clock.GetUtcNow();
        if (!Actor(row, caller) || row.State != "PENDING_PROOF" || !CurrentBinding(row, policy, now) || Operation(row).ContextDigest != context.Binding.ContextDigest || context.ExpectedPolicyVersion != row.AssurancePolicyVersion)
            return UserStepUpOperationApplyOutcome.Conflict();
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == context.Binding.OperationId, ct).ConfigureAwait(false);
        if (proof is null || proof.State != "ACTIVE" || proof.ContextDigest != context.Binding.ContextDigest) return UserStepUpOperationApplyOutcome.Conflict();
        if (row.Kind == "ENROLL")
        {
            if (!await EnrollmentAllowed(caller.AccountId, policy, ct).ConfigureAwait(false)) return UserStepUpOperationApplyOutcome.Conflict();
            row.ProofReference = proof.ProofId; row.AuthorizedAt = proof.AuthenticatedAt; row.ExpiresAt = Min(row.ExpiresAt, proof.ExpiresAt); row.State = "AUTHORIZED"; row.Version++;
            Event(row, "TotpEnrollmentAuthorized", now); return UserStepUpOperationApplyOutcome.SessionContinues().ForTotp(Validity(row));
        }
        var factor = await Factor(row.AuthenticatorReference, caller.AccountId, ct).ConfigureAwait(false);
        if (factor is null || factor.State != "ACTIVE" || factor.Version != row.ExpectedVersion || factor.Generation != row.Generation) return UserStepUpOperationApplyOutcome.Conflict();
        var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var alternatives = await database.Set<TotpAuthenticatorRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId && x.State == "ACTIVE" && x.AuthenticatorReference != factor.AuthenticatorReference)
            .OrderBy(x => x.AuthenticatorReference).Select(x => new { x.AuthenticatorReference, x.Generation, x.Version }).ToArrayAsync(ct).ConfigureAwait(false);
        var passkeys = await database.Set<PasskeyCredentialRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId && x.State == "ACTIVE").OrderBy(x => x.CredentialReference).Select(x => new { x.CredentialReference, x.Version }).ToArrayAsync(ct).ConfigureAwait(false);
        var links = await database.ExternalIdentityLinks.FromSqlInterpolated($"SELECT * FROM identity.external_identity_links WHERE account_id={caller.AccountId} ORDER BY link_id FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        var digest = AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { alternatives, passkeys, links, password = user.PasswordHash is not null, stamp = AuthenticationIdentity.Digest(user.SecurityStamp!) }));
        var decision = await eligibility.RequireAsync(new(caller.AccountId, account.Version, factor.AuthenticatorReference, factor.Version, context.Binding.Scope.Operation, digest, now), ct).ConfigureAwait(false);
        if (decision.AccountId != caller.AccountId || decision.AccountVersion != account.Version || decision.AuthenticatorReference != factor.AuthenticatorReference || decision.AuthenticatorVersion != factor.Version ||
            decision.Operation != context.Binding.Scope.Operation || decision.AlternativeMethodsDigest != digest || string.IsNullOrWhiteSpace(decision.PolicyVersion) || string.IsNullOrWhiteSpace(decision.EvidenceReference) || !decision.EffectiveInterval.Contains(clock.GetUtcNow()))
            throw new InvalidOperationException("Current TOTP mutation eligibility is unavailable.");
        if (!decision.Allowed) return UserStepUpOperationApplyOutcome.Conflict();
        var effect = row.Kind == "DISABLE" ? policy.DisableSessionEffect : policy.ResetSessionEffect;
        factor.State = "DISABLED"; factor.DisabledAt = now; factor.Version++; factor.DisableSessionEffect = effect.ToString(); factor.DisablePolicyVersion = policy.Version;
        factor.DisableOperationId = context.Binding.OperationId; factor.DisableCorrelationId = context.Binding.CorrelationId;
        row.EligibilityPolicyVersion = decision.PolicyVersion; row.EligibilityEvidenceReference = decision.EvidenceReference; row.EligibilityFrom = decision.EffectiveInterval.From;
        row.EligibilityIncludesFrom = decision.EffectiveInterval.IncludesFrom; row.EligibilityUntil = decision.EffectiveInterval.Until; row.EligibilityIncludesUntil = decision.EffectiveInterval.IncludesUntil;
        End(row, "COMPLETED", now); Event(row, "AuthenticatorRevoked", now); Event(row, "IdentitySecurityActivityRequested", now); Event(row, "IdentitySecurityNotificationRequested", now);
        var receipt = effect == TotpSessionEffect.RevokeFactorSessions ? await authentication.EndTotpActorSessionAsync(caller, context.Binding, factor, policy, ct).ConfigureAwait(false) : null;
        if (!decision.EffectiveInterval.Contains(clock.GetUtcNow()) || !CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow())) return UserStepUpOperationApplyOutcome.Conflict();
        return (receipt is null ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.SessionEnded(receipt)).ForTotp(Validity(row));
    }
    internal async Task<TotpDisclosureResult> DiscloseAsync(StepUpCaller caller, TotpOperationCommand command, CancellationToken ct)
    {
        Clean(); var policy = Current(caller.Origin); TotpAuthenticatorRow candidate; TotpOperationRow operation; var create = false;
        // Reserve one immutable provider target before any external write. A crash/unknown write can only reconcile this target.
        await using (var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            var session = await authentication.LockForOperationAsync(caller, EnrollmentScope.Operation, ct).ConfigureAwait(false);
            if (session is null) return await Finish(transaction, new TotpDisclosureResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
            await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
            operation = (await TrackedOperation(caller, command, ct).ConfigureAwait(false))!;
            if (operation is null || operation.Kind != "ENROLL") return await Finish(transaction, new TotpDisclosureResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
            if (operation.DisclosureClaimedAt is not null) return await Finish(transaction, new TotpDisclosureResult("DISCLOSURE_UNAVAILABLE", operation), ct).ConfigureAwait(false);
            if (!await Authorized(operation, session, caller, policy, ct).ConfigureAwait(false)) return await Finish(transaction, new TotpDisclosureResult("STEP_UP_REQUIRED", operation), ct).ConfigureAwait(false);
            candidate = (await Factor(operation.AuthenticatorReference, caller.AccountId, ct).ConfigureAwait(false))!;
            if (operation.State == "AUTHORIZED")
            {
                if (!await EnrollmentAllowed(caller.AccountId, policy, ct).ConfigureAwait(false)) return await Finish(transaction, new TotpDisclosureResult("TOTP_LIMIT_REACHED", operation), ct).ConfigureAwait(false);
                if (candidate is not null) return await Finish(transaction, new TotpDisclosureResult("VERSION_CONFLICT", operation), ct).ConfigureAwait(false);
                var binding = new TotpSeedBinding(caller.AccountId, operation.AuthenticatorReference, operation.Generation, operation.EnrollmentId, policy.SecretBindingVersion);
                var reference = seeds.Allocate(binding);
                if (reference.Version != 1) throw new InvalidOperationException("An immutable first provider version is required.");
                candidate = new() { AuthenticatorReference = operation.AuthenticatorReference, AccountId = caller.AccountId, Generation = operation.Generation, EnrollmentId = operation.EnrollmentId,
                    SecretStoreReference = reference.StoreReference, SecretResourceReference = reference.ResourceReference, SecretVersion = reference.Version, SecretBindingVersion = policy.SecretBindingVersion,
                    CreatedAt = clock.GetUtcNow(), PolicyVersion = policy.Version, ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference };
                database.Add(candidate); operation.State = "PREPARING"; operation.Version++; create = true;
            }
            if (candidate is null || candidate.State != "INACTIVE" || operation.State != "PREPARING") return await Finish(transaction, new TotpDisclosureResult("VERSION_CONFLICT", operation), ct).ConfigureAwait(false);
            await Finish(transaction, true, ct).ConfigureAwait(false);
        }
        // No provider I/O occurs in the Identity owner transaction. Unknown create never causes key regeneration.
        if (create)
        {
            var seed = users.GenerateNewAuthenticatorKey();
            try { await seeds.CreateOnceAsync(candidate.Binding, candidate.Reference, seed, ct).ConfigureAwait(false); }
            catch (InvalidOperationException) { return new("PREPARATION_UNKNOWN", operation); }
        }
        using var prepared = await seeds.ReadAsync(candidate.Binding, candidate.Reference, ct).ConfigureAwait(false);
        if (prepared is null) return new("PREPARATION_UNKNOWN", operation);
        await using (var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            var session = await authentication.LockForOperationAsync(caller, EnrollmentScope.Operation, ct).ConfigureAwait(false);
            if (session is null) return await Finish(transaction, new TotpDisclosureResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
            await LockOwner(caller.AccountId, ct).ConfigureAwait(false);
            operation = (await TrackedOperation(caller, command, ct).ConfigureAwait(false))!;
            var fresh = await Factor(candidate.AuthenticatorReference, caller.AccountId, ct).ConfigureAwait(false);
            if (operation is null || fresh is null || fresh.Generation != candidate.Generation || fresh.Reference != candidate.Reference || fresh.State != "INACTIVE")
                return await Finish(transaction, new TotpDisclosureResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
            if (operation.DisclosureClaimedAt is not null) return await Finish(transaction, new TotpDisclosureResult("DISCLOSURE_UNAVAILABLE", operation), ct).ConfigureAwait(false);
            if (operation.State != "PREPARING" || !await Authorized(operation, session, caller, Current(caller.Origin), ct).ConfigureAwait(false))
                return await Finish(transaction, new TotpDisclosureResult("STEP_UP_REQUIRED", operation), ct).ConfigureAwait(false);
            operation.DisclosureClaimedAt = clock.GetUtcNow(); operation.State = "AWAITING_FIRST_CODE"; operation.Version++; Event(operation, "TotpSetupDisclosed", clock.GetUtcNow());
            await Finish(transaction, true, ct).ConfigureAwait(false);
        }
        var key = prepared.Require();
        var uri = "otpauth://totp/" + Uri.EscapeDataString(policy.Issuer + ":" + candidate.AuthenticatorReference.ToString("N")) + "?secret=" + Uri.EscapeDataString(key) +
            "&issuer=" + Uri.EscapeDataString(policy.Issuer) + "&algorithm=SHA1&digits=6&period=30";
        return new(null, operation, new(operation, key, uri));
    }
    internal async Task<TotpOperationResult> ActivateAsync(StepUpCaller caller, TotpCodeCommand command, CancellationToken ct)
    {
        Clean(); var policy = Current(caller.Origin); var code = TotpNativeVerifier.CanonicalCode(command.Code);
        if (command.AttemptId == Guid.Empty || code is null) return new("VALIDATION_ERROR");
        var snapshot = await ReadAsync(caller, command.EnrollmentId, command.RequestId, ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.Kind != "ENROLL") return new("VERSION_CONFLICT");
        var source = await database.Set<TotpAuthenticatorRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AuthenticatorReference == snapshot.AuthenticatorReference && x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (source is null) return new("VERSION_CONFLICT");
        // Exact terminal reconciliation precedes provider/native acceptance and cannot repeat disclosure.
        if (snapshot.State == "COMPLETED") return snapshot.ActivationRequestId == command.AttemptId ? new(null, snapshot) : new("VERSION_CONFLICT", snapshot);
        using var seed = await seeds.ReadAsync(source.Binding, source.Reference, ct).ConfigureAwait(false); if (seed is null) return new("DEPENDENCY_UNAVAILABLE");
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, EnrollmentScope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(transaction, new TotpOperationResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false); var row = await TrackedOperation(caller, new() { EnrollmentId = command.EnrollmentId, RequestId = command.RequestId }, ct).ConfigureAwait(false);
        var factor = await Factor(source.AuthenticatorReference, caller.AccountId, ct).ConfigureAwait(false);
        if (row?.State == "COMPLETED") return await Finish(transaction, new TotpOperationResult(row.ActivationRequestId == command.AttemptId ? null : "VERSION_CONFLICT", row), ct).ConfigureAwait(false);
        if (row is null || factor is null || factor.State != "INACTIVE" || factor.Generation != source.Generation || factor.Reference != source.Reference || row.State != "AWAITING_FIRST_CODE" ||
            row.DisclosureClaimedAt is null || !await Authorized(row, session, caller, policy, ct).ConfigureAwait(false)) return await Finish(transaction, new TotpOperationResult("STEP_UP_REQUIRED", row), ct).ConfigureAwait(false);
        if (!await EnrollmentAllowed(caller.AccountId, policy, ct, factor.AuthenticatorReference).ConfigureAwait(false)) return await Finish(transaction, new TotpOperationResult("TOTP_LIMIT_REACHED", row), ct).ConfigureAwait(false);
        var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
        if (!await Rate(caller, auth, ct).ConfigureAwait(false)) return await Finish(transaction, new TotpOperationResult("RATE_OR_ABUSE_LIMITED", row), ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CreateSavepointAsync("totp_activation", ct).ConfigureAwait(false);
        var error = await verifier.AcceptAsync(factor, policy, seed.Require(), code, ct).ConfigureAwait(false);
        if (error is not null) return await Finish(transaction, new TotpOperationResult(error, row), ct).ConfigureAwait(false);
        var authorization = await ReadAuthorizationSource(row, ct).ConfigureAwait(false);
        var authority = await authentication.VerifyContinuedOperationAsync(caller, Operation(row), session, ct).ConfigureAwait(false); var now = clock.GetUtcNow();
        var finalAuthentication = authenticationPolicies.RequireEffective(now); finalAuthentication.Validate();
        if (authority is null || !authority.SessionUseInterval.Contains(now) || !authority.OperationInterval.Contains(now) || !CurrentBinding(row, Current(caller.Origin), now) ||
            AuthenticationApplication.Expired(session, finalAuthentication, now) || !authentication.MethodFactsCurrentAt(session, finalAuthentication, now) ||
            !AuthorizationCurrent(authorization, row, session, caller, Current(caller.Origin), now))
        {
            await transaction.RollbackToSavepointAsync("totp_activation", ct).ConfigureAwait(false); database.ChangeTracker.Clear();
            return await Finish(transaction, new TotpOperationResult("STEP_UP_REQUIRED"), ct).ConfigureAwait(false);
        }
        factor.State = "ACTIVE"; factor.ActivatedAt = now; factor.Version++; row.ActivationRequestId = command.AttemptId; End(row, "COMPLETED", now);
        Event(row, "AuthenticatorAdded", now); Event(row, "IdentitySecurityActivityRequested", now); Event(row, "IdentitySecurityNotificationRequested", now);
        return await Finish(transaction, new TotpOperationResult(null, row), ct).ConfigureAwait(false);
    }
    internal async Task<TotpOperationRow?> ReadAsync(StepUpCaller caller, Guid id, Guid request, CancellationToken ct)
    {
        var row = await database.Set<TotpOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.EnrollmentId == id && x.RequestId == request, ct).ConfigureAwait(false);
        return row is not null && Actor(row, caller) ? row : null;
    }
    internal async Task<TotpOperationRow?> ReadStartResultAsync(StepUpCaller caller, TotpStartResultCommand command, CancellationToken ct)
    {
        if (command.RequestId == Guid.Empty || command.Kind is not ("ENROLL" or "DISABLE" or "RESET")) return null;
        var row = await database.Set<TotpOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == command.RequestId, ct).ConfigureAwait(false);
        return row is not null && Actor(row, caller) && row.Kind == command.Kind && row.ExpectedVersion == command.ExpectedVersion &&
            (row.Kind == "ENROLL" ? command.AuthenticatorReference is null : row.AuthenticatorReference == command.AuthenticatorReference) && row.RecoveryExpiresAt > clock.GetUtcNow() ? row : null;
    }
    internal async Task<TotpOperationResult> CancelAsync(StepUpCaller caller, TotpOperationCommand command, CancellationToken ct)
    {
        Clean(); await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await authentication.LockForOperationAsync(caller, "CANCEL_TOTP_OPERATION", ct).ConfigureAwait(false) is null) return await Finish(transaction, new TotpOperationResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockOwner(caller.AccountId, ct).ConfigureAwait(false); var row = await TrackedOperation(caller, command, ct).ConfigureAwait(false);
        if (row is null) return await Finish(transaction, new TotpOperationResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        if (row.EndedAt is null) { End(row, "REJECTED", clock.GetUtcNow()); Event(row, "TotpOperationCancelled", clock.GetUtcNow()); }
        return await Finish(transaction, new TotpOperationResult(null, row), ct).ConfigureAwait(false);
    }
    private async Task<TotpOperationRow?> TrackedOperation(StepUpCaller caller, TotpOperationCommand command, CancellationToken ct)
    {
        var row = await database.Set<TotpOperationRow>().SingleOrDefaultAsync(x => x.EnrollmentId == command.EnrollmentId && x.RequestId == command.RequestId, ct).ConfigureAwait(false);
        if (row is not null) await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false); return row is not null && Actor(row, caller) ? row : null;
    }
    private async Task<bool> Authorized(TotpOperationRow row, AuthenticationSessionRow session, StepUpCaller caller, TotpPolicy policy, CancellationToken ct)
    {
        var snapshot = await ReadAuthorizationSource(row, ct).ConfigureAwait(false); return AuthorizationCurrent(snapshot, row, session, caller, policy, clock.GetUtcNow());
    }
    private sealed record ConsumedAuthorization(StepUpProofRow Proof, AuthenticatorProofSource? Source);
    private async Task<ConsumedAuthorization?> ReadAuthorizationSource(TotpOperationRow row, CancellationToken ct)
    {
        if (row.ProofReference is null) return null;
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.ProofId == row.ProofReference, ct).ConfigureAwait(false); if (proof is null) return null;
        await database.Entry(proof).ReloadAsync(ct).ConfigureAwait(false);
        var source = await authentication.ReadProofCredentialAsync(proof, ct, refresh: true).ConfigureAwait(false); return new(proof, source);
    }
    private bool AuthorizationCurrent(ConsumedAuthorization? snapshot, TotpOperationRow row, AuthenticationSessionRow session, StepUpCaller caller, TotpPolicy policy, DateTimeOffset now)
    {
        if (snapshot is null || !CurrentBinding(row, policy, now) || row.SecurityStampDigest != session.SecurityStampDigest) return false;
        var proof = snapshot.Proof; var source = snapshot.Source;
        var operation = Operation(row); var assurance = assurancePolicies.RequireEffective(operation.Scope, now, row.AssurancePolicyVersion);
        return proof.State == "CONSUMED" && proof.AccountId == caller.AccountId && proof.SessionId == caller.SessionId && proof.Origin == caller.Origin &&
            proof.SecurityStampDigest == session.SecurityStampDigest && proof.OperationId == operation.OperationId && proof.ContextDigest == operation.ContextDigest &&
            proof.PolicyVersion == row.AssurancePolicyVersion && proof.AuthenticatedAt == row.AuthorizedAt && proof.EndedAt is not null && proof.EndedAt < proof.ExpiresAt &&
            now >= proof.AuthenticatedAt && now < proof.ExpiresAt && now - proof.AuthenticatedAt < assurance.ProofLifetime && assurance.RequireMethodAssurance(proof.AuthenticationMethod) == proof.Assurance &&
            proof.Assurance == assurance.RequiredAssurance && authentication.ProofMethodCurrentAt(proof, source, operation, now, false);
    }
    private async Task<TotpAuthenticatorRow?> Factor(Guid id, string account, CancellationToken ct)
    {
        var row = await database.Set<TotpAuthenticatorRow>().SingleOrDefaultAsync(x => x.AuthenticatorReference == id && x.AccountId == account, ct).ConfigureAwait(false);
        if (row is not null) await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false); return row;
    }
    private async Task<bool> EnrollmentAllowed(string account, TotpPolicy policy, CancellationToken ct, Guid? candidate = null)
    {
        var active = await database.Set<TotpAuthenticatorRow>().CountAsync(x => x.AccountId == account && x.State == "ACTIVE" && x.AuthenticatorReference != candidate, ct).ConfigureAwait(false);
        return active < policy.MaximumActiveAuthenticators && (policy.ReplacementOrdering == TotpReplacementOrdering.IndependentEnrollment || active == 0);
    }
    internal bool CurrentBinding(TotpOperationRow row, TotpPolicy policy, DateTimeOffset now) => row.PolicyVersion == policy.Version && row.Origin == policy.Origin &&
        row.AuthenticationPolicyVersion == authenticationPolicies.RequireEffective(now).Version && now >= row.CreatedAt && now < row.ExpiresAt;
    internal Task<int> LockOwner(string account, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"totp-owner:" + account},0))", ct);
    private async Task<bool> Rate(StepUpCaller caller, AuthenticationPolicy policy, CancellationToken ct) =>
        await authentication.AllowAsync("totp:N:" + caller.NetworkDigest, policy.NetworkAttemptLimit, policy, clock.GetUtcNow(), ct).ConfigureAwait(false) &&
        await authentication.AllowAsync("totp:A:" + caller.AccountId, policy.IdentifierAttemptLimit, policy, clock.GetUtcNow(), ct).ConfigureAwait(false);
    private void Clean() { if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("TOTP requires a clean owner transaction boundary."); }
    private static bool Actor(TotpOperationRow row, StepUpCaller caller) => row.AccountId == caller.AccountId && row.SessionId == caller.SessionId && row.Origin == caller.Origin;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static void End(TotpOperationRow row, string state, DateTimeOffset now) { row.State = state; row.EndedAt = now; row.Version++; }
    private async Task<T> Finish<T>(IDbContextTransaction transaction, T result, CancellationToken ct) { await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false); return result; }
    private static TotpExecutionValidity Validity(TotpOperationRow row) => new(row.EnrollmentId, row.AccountId, row.SessionId, row.Origin, row.PolicyVersion, Operation(row).OperationId, Operation(row).ContextDigest,
        row.ExpiresAt, row.EligibilityPolicyVersion, row.EligibilityEvidenceReference, row.EligibilityFrom is { } from && row.EligibilityIncludesFrom is { } includes && row.EligibilityIncludesUntil is { } includesUntil ? new EffectivePolicyInterval(from, includes, row.EligibilityUntil, includesUntil) : null);
    private void Event(TotpOperationRow row, string type, DateTimeOffset now) => database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", now, "tunner/identity", row.AuthenticatorReference.ToString("N"), "RESTRICTED_SECURITY",
        JsonSerializer.Serialize(new { schemaVersion = "1", reference = row.EnrollmentId, authenticatorReference = row.AuthenticatorReference, authenticatorType = "TOTP", operation = Operation(row).Scope.Operation }),
        row.Version.ToString(CultureInfo.InvariantCulture), Operation(row).OperationId, row.EnrollmentId.ToString("N")));
}
