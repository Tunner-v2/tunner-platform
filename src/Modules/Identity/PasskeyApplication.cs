using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Identity-owned native ceremonies. All credential/state transitions share the Tunner transaction and outbox.</summary>
internal sealed class PasskeyApplication(IdentityPersistenceDbContext database, UserManager<IdentityUserAdapter> users,
    AuthenticationApplication authentication, IPasskeyPolicySource policies, IAuthenticationPolicySource authenticationPolicies,
    IAssurancePolicySource assurancePolicies, IPasskeyRevocationEligibilitySource revocationEligibility,
    IDataProtectionProvider protection, TimeProvider clock) : IPasskeyCeremonyMaintenance
{
    private readonly IDataProtector stateProtection = protection.CreateProtector("Tunner.Identity.Passkey.State.v1");
    internal static readonly AssurancePolicyScope EnrollmentScope = new("REGISTER_PASSKEY", "USER_BFF");
    internal static readonly AssurancePolicyScope RevocationScope = new("REVOKE_PASSKEY", "USER_BFF");
    internal PasskeyPolicy Current(string origin)
    {
        var policy = policies.RequireEffective(clock.GetUtcNow());
        if (policy.Origin != origin) throw new InvalidOperationException("The passkey policy is not approved for this origin.");
        return policy;
    }
    private PasskeyHandler<IdentityUserAdapter> Handler(PasskeyPolicy policy) => new(users, Options.Create(new IdentityPasskeyOptions
    {
        ServerDomain = policy.RelyingPartyId, ChallengeSize = policy.ChallengeBytes, AuthenticatorTimeout = policy.AuthenticatorTimeout,
        UserVerificationRequirement = "required", ResidentKeyRequirement = "required", AttestationConveyancePreference = "none",
        IsAllowedAlgorithm = value => policy.AllowedAlgorithms.Contains(value),
        ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && context.TopOrigin is null && context.Origin == policy.Origin &&
            context.HttpContext.Request.Scheme == "https" && context.HttpContext.Request.Host.ToUriComponent() == new Uri(policy.Origin).Authority)
    }));
    internal static StepUpOperation Operation(PasskeyCeremonyRow row) => new("OP-PASSKEY-" + row.CeremonyId.ToString("N"), row.CeremonyId.ToString("N"),
        row.Kind == "ENROLL" ? EnrollmentScope : RevocationScope, row.Kind == "REVOKE" ? row.CredentialReference!.Value.ToString("N") : row.CeremonyId.ToString("N"),
        row.ExpectedVersion, AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { row.CeremonyId, row.RequestId, row.AccountId, row.SessionId,
            row.Origin, row.PolicyVersion, row.Kind, target = row.Kind == "REVOKE" ? row.CredentialReference : null, row.ExpectedVersion })));
    internal async Task<PasskeyResult> StartMutationAsync(StepUpCaller caller, Guid requestId, Guid? credentialReference, long expectedVersion, CancellationToken ct)
    {
        Clean(); if (requestId == Guid.Empty || expectedVersion < 0 || credentialReference == Guid.Empty) return new("VALIDATION_ERROR");
        var policy = Current(caller.Origin); var kind = credentialReference is null ? "ENROLL" : "REVOKE";
        var browser = MutationBrowser(caller); var now = clock.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await authentication.LockForOperationAsync(caller, kind == "ENROLL" ? EnrollmentScope.Operation : RevocationScope.Operation, ct).ConfigureAwait(false) is not { } session)
            return await Finish(transaction, new PasskeyResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        var prior = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.BrowserDigest == browser && x.RequestId == requestId && x.Kind == kind, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            if (!Actor(prior, caller) || (prior.Kind == "REVOKE" && prior.CredentialReference != credentialReference) || prior.ExpectedVersion != expectedVersion || prior.PolicyVersion != policy.Version)
                return await Finish(transaction, new PasskeyResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
            return await Finish(transaction, new PasskeyResult(null, prior), ct).ConfigureAwait(false);
        }
        if (await database.Set<PasskeyCeremonyRow>().CountAsync(x => x.SessionId == caller.SessionId &&
            (x.State == "PENDING" || x.State == "AUTHORIZED") && x.ExpiresAt > now, ct).ConfigureAwait(false) >= policy.MaximumPendingCeremonies)
            return await Finish(transaction, new PasskeyResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        if (kind == "ENROLL" && await database.Set<PasskeyCredentialRow>().CountAsync(x => x.AccountId == caller.AccountId && x.State == "ACTIVE", ct).ConfigureAwait(false) >= policy.MaximumActiveCredentials)
            return await Finish(transaction, new PasskeyResult("PASSKEY_LIMIT_REACHED"), ct).ConfigureAwait(false);
        if (credentialReference is { } reference && !await database.Set<PasskeyCredentialRow>().AnyAsync(x => x.CredentialReference == reference &&
            x.AccountId == caller.AccountId && x.Version == expectedVersion && x.State == "ACTIVE", ct).ConfigureAwait(false))
            return await Finish(transaction, new PasskeyResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        var row = new PasskeyCeremonyRow { CeremonyId = Guid.NewGuid(), RequestId = requestId, Kind = kind, AccountId = caller.AccountId,
            SessionId = caller.SessionId, BrowserDigest = browser, Origin = caller.Origin, PolicyVersion = policy.Version,
            AuthenticationPolicyVersion = auth.Version, SecurityStampDigest = session.SecurityStampDigest, CreatedAt = now,
            ExpiresAt = Min(now + policy.CeremonyLifetime, session.ExpiresAt), RecoveryExpiresAt = now + policy.CeremonyLifetime + policy.ResultRecoveryLifetime, CredentialReference = credentialReference, ExpectedVersion = expectedVersion,
            AssurancePolicyVersion = kind == "ENROLL" ? policy.EnrollmentAssurancePolicyVersion : policy.RevocationAssurancePolicyVersion,
            ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference };
        _ = assurancePolicies.RequireEffective(Operation(row).Scope, now, row.AssurancePolicyVersion);
        database.Add(row); Event(row, "PasskeyOperationStarted", now);
        return await Finish(transaction, new PasskeyResult(null, row), ct).ConfigureAwait(false);
    }
    internal async Task<UserStepUpOperationContext?> ResolveMutationAsync(StepUpCaller caller, Guid ceremonyId, Guid requestId, CancellationToken ct)
    {
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == ceremonyId && x.RequestId == requestId, ct).ConfigureAwait(false);
        if (row is null || !Actor(row, caller) || row.Kind == "SIGN_IN") return null;
        var policy = Current(caller.Origin);
        if (row.PolicyVersion != policy.Version || row.AuthenticationPolicyVersion != authenticationPolicies.RequireEffective(clock.GetUtcNow()).Version ||
            row.ExpiresAt <= clock.GetUtcNow() || row.State is "REJECTED" or "EXPIRED") return null;
        var operation = Operation(row);
        return new(new UserStepUpActor(caller), operation.OperationId, operation.CorrelationId, operation.Scope, operation.ResourceId,
            operation.ExpectedVersion, operation.ContextDigest, row.AssurancePolicyVersion, row.State is "AUTHORIZED" or "COMPLETED");
    }
    // Invoked exclusively by shared StepUpApplication, with its locked current actor and exact proof.
    internal async Task<UserStepUpOperationApplyOutcome> ApplyMutationAsync(StepUpCaller caller, UserStepUpOperationContext context, HttpContext http, CancellationToken ct)
    {
        if (!Guid.TryParseExact(context.Binding.CorrelationId, "N", out var id)) return UserStepUpOperationApplyOutcome.Conflict();
        var row = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == id, ct).ConfigureAwait(false);
        if (row is null) return UserStepUpOperationApplyOutcome.Conflict();
        await LockCeremony(row.CeremonyId, ct).ConfigureAwait(false); await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        var policy = Current(caller.Origin); var now = clock.GetUtcNow();
        if (!Actor(row, caller) || row.State != "PENDING" || row.ExpiresAt <= now || row.PolicyVersion != policy.Version ||
            Operation(row).ContextDigest != context.Binding.ContextDigest || row.AssurancePolicyVersion != context.ExpectedPolicyVersion) return UserStepUpOperationApplyOutcome.Conflict();
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.OperationId == context.Binding.OperationId, ct).ConfigureAwait(false);
        if (proof is null || proof.State != "ACTIVE" || proof.ContextDigest != context.Binding.ContextDigest) return UserStepUpOperationApplyOutcome.Conflict();
        if (row.Kind == "REVOKE")
        {
            await LockCredentialOwner(caller.AccountId, ct).ConfigureAwait(false);
            var credential = await database.Set<PasskeyCredentialRow>().SingleOrDefaultAsync(x => x.CredentialReference == row.CredentialReference && x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
            if (credential is null) return UserStepUpOperationApplyOutcome.Conflict();
            await database.Entry(credential).ReloadAsync(ct).ConfigureAwait(false);
            if (credential.State != "ACTIVE" || credential.Version != row.ExpectedVersion) return UserStepUpOperationApplyOutcome.Conflict();
            var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
            var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
            // The digest captures every current adapter-owned method reference/version. Additional owner paths are evaluated by the source.
            var alternatives = await database.Set<PasskeyCredentialRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId && x.State == "ACTIVE" && x.CredentialReference != credential.CredentialReference)
                .OrderBy(x => x.CredentialReference).Select(x => new { x.CredentialReference, x.Version }).ToArrayAsync(ct).ConfigureAwait(false);
            var links = await database.ExternalIdentityLinks.FromSqlInterpolated($"SELECT * FROM identity.external_identity_links WHERE account_id={caller.AccountId} ORDER BY link_id FOR SHARE").AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
            var digest = AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { alternatives, password = user.PasswordHash is not null,
                passwordStamp = user.ConcurrencyStamp, links }));
            var decision = await revocationEligibility.RequireAsync(new(caller.AccountId, account.Version, credential.CredentialReference, credential.Version,
                alternatives.Length, user.PasswordHash is not null, digest, now), ct).ConfigureAwait(false);
            if (decision.AccountId != caller.AccountId || decision.AccountVersion != account.Version || decision.CredentialReference != credential.CredentialReference ||
                decision.CredentialVersion != credential.Version || decision.AlternativeMethodsDigest != digest || string.IsNullOrWhiteSpace(decision.PolicyVersion) ||
                string.IsNullOrWhiteSpace(decision.EvidenceReference) || !decision.EffectiveInterval.Contains(clock.GetUtcNow()))
                throw new InvalidOperationException("Current required authentication/recovery path authority is unavailable.");
            if (!decision.Allowed) return UserStepUpOperationApplyOutcome.Conflict();
            var removed = await users.RemovePasskeyAsync(user, credential.CredentialId).ConfigureAwait(false); if (!removed.Succeeded) return UserStepUpOperationApplyOutcome.Conflict();
            if (!CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow()) || !decision.EffectiveInterval.Contains(clock.GetUtcNow())) return UserStepUpOperationApplyOutcome.Conflict();
            credential.State = "REVOKED"; credential.RevokedAt = now; credential.Version++;
            credential.RevocationSessionEffect = policy.RevocationSessionEffect.ToString(); credential.RevocationPolicyVersion = policy.Version;
            credential.RevocationOperationId = context.Binding.OperationId; credential.RevocationCorrelationId = context.Binding.CorrelationId;
            row.EligibilityPolicyVersion = decision.PolicyVersion; row.EligibilityEvidenceReference = decision.EvidenceReference;
            row.EligibilityFrom = decision.EffectiveInterval.From; row.EligibilityIncludesFrom = decision.EffectiveInterval.IncludesFrom;
            row.EligibilityUntil = decision.EffectiveInterval.Until; row.EligibilityIncludesUntil = decision.EffectiveInterval.IncludesUntil;
            End(row, "COMPLETED", now); Event(row, "AuthenticatorRevoked", now); Event(row, "IdentitySecurityActivityRequested", now); Event(row, "IdentitySecurityNotificationRequested", now);
            var receipt = policy.RevocationSessionEffect == PasskeyRevocationSessionEffect.RevokeCredentialSessions
                ? await authentication.EndCredentialActorSessionAsync(caller, context.Binding, credential, policy, ct).ConfigureAwait(false) : null;
            if (!CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow()) || !decision.EffectiveInterval.Contains(clock.GetUtcNow()))
                return UserStepUpOperationApplyOutcome.Conflict();
            return (receipt is null ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.SessionEnded(receipt)).ForPasskey(ExecutionValidity(row));
        }
        if (row.Kind != "ENROLL") return UserStepUpOperationApplyOutcome.Conflict();
        await LockCredentialOwner(caller.AccountId, ct).ConfigureAwait(false);
        if (await database.Set<PasskeyCredentialRow>().CountAsync(x => x.AccountId == caller.AccountId && x.State == "ACTIVE", ct).ConfigureAwait(false) >= policy.MaximumActiveCredentials) return UserStepUpOperationApplyOutcome.Conflict();
        var owner = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        var options = await Handler(policy).MakeCreationOptionsAsync(new PasskeyUserEntity { Id = owner.Id, Name = owner.AccountId, DisplayName = owner.AccountId }, http).ConfigureAwait(false);
        row.ProtectedState = stateProtection.Protect(options.AttestationState ?? throw new InvalidOperationException("Native ceremony state is unavailable.")); row.OptionsJson = options.CreationOptionsJson;
        row.ProofReference = proof.ProofId; row.AuthorizedAt = proof.AuthenticatedAt; row.ExpiresAt = Min(row.ExpiresAt, proof.ExpiresAt);
        row.State = "AUTHORIZED"; row.Version++; Event(row, "PasskeyEnrollmentAuthorized", now); return UserStepUpOperationApplyOutcome.SessionContinues().ForPasskey(ExecutionValidity(row));
    }
    internal async Task<PasskeyResult> CompleteEnrollmentAsync(StepUpCaller caller, PasskeyCompleteCommand command, HttpContext http, CancellationToken ct)
    {
        Clean(); var policy = Current(caller.Origin); var json = CredentialJson(command.Credential, policy); var digest = AuthenticationIdentity.Digest(json);
        var snapshot = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == command.CeremonyId && x.RequestId == command.RequestId, ct).ConfigureAwait(false);
        if (snapshot is null || !Actor(snapshot, caller) || snapshot.Kind != "ENROLL") return new("VERSION_CONFLICT");
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var session = await authentication.LockForOperationAsync(caller, EnrollmentScope.Operation, ct).ConfigureAwait(false);
        if (session is null) return await Finish(transaction, new PasskeyResult("AUTHENTICATION_FAILED"), ct).ConfigureAwait(false);
        await LockCredentialOwner(caller.AccountId, ct).ConfigureAwait(false); await LockCeremony(command.CeremonyId, ct).ConfigureAwait(false);
        var row = await database.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == command.CeremonyId, ct).ConfigureAwait(false); await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (row.State == "COMPLETED") return await Finish(transaction, new PasskeyResult(row.RequestDigest == digest ? null : "VERSION_CONFLICT", row), ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var proof = await database.Set<StepUpProofRow>().SingleOrDefaultAsync(x => x.ProofId == row.ProofReference, ct).ConfigureAwait(false);
        var operation = Operation(row); var assurance = assurancePolicies.RequireEffective(operation.Scope, now, row.AssurancePolicyVersion);
        if (row.State != "AUTHORIZED" || !CurrentBinding(row, policy, now) || session.SecurityStampDigest != row.SecurityStampDigest || proof is null ||
            proof.State != "CONSUMED" || proof.AccountId != caller.AccountId || proof.SessionId != caller.SessionId || proof.Origin != caller.Origin ||
            proof.SecurityStampDigest != session.SecurityStampDigest || proof.OperationId != operation.OperationId || proof.ContextDigest != operation.ContextDigest ||
            proof.PolicyVersion != row.AssurancePolicyVersion || proof.AuthenticatedAt != row.AuthorizedAt || proof.EndedAt is null || proof.EndedAt >= proof.ExpiresAt ||
            now >= proof.ExpiresAt || now - proof.AuthenticatedAt >= assurance.ProofLifetime || assurance.RequireMethodAssurance(proof.AuthenticationMethod) != proof.Assurance || proof.Assurance != assurance.RequiredAssurance)
            return await FailCeremony(transaction, row, "STEP_UP_REQUIRED", now, ct).ConfigureAwait(false);
        if (await database.Set<PasskeyCredentialRow>().CountAsync(x => x.AccountId == caller.AccountId && x.State == "ACTIVE", ct).ConfigureAwait(false) >= policy.MaximumActiveCredentials)
            return await FailCeremony(transaction, row, "PASSKEY_LIMIT_REACHED", now, ct).ConfigureAwait(false);
        var result = await Handler(policy).PerformAttestationAsync(new PasskeyAttestationContext { HttpContext = http, CredentialJson = json,
            AttestationState = stateProtection.Unprotect(row.ProtectedState) }).ConfigureAwait(false);
        var user = await database.Users.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.UserEntity.Id != user.Id || !result.Passkey.IsUserVerified)
            return await FailCeremony(transaction, row, "AUTHENTICATION_FAILED", clock.GetUtcNow(), ct).ConfigureAwait(false);
        var verified = result.Passkey;
        await LockCredential(verified.CredentialId, ct).ConfigureAwait(false);
        if (await database.Set<PasskeyCredentialRow>().AnyAsync(x => x.CredentialDigest == CredentialDigest(verified.CredentialId), ct).ConfigureAwait(false))
            return await FailCeremony(transaction, row, "VERSION_CONFLICT", clock.GetUtcNow(), ct).ConfigureAwait(false);
        // Persist only required public-key metadata, never raw ceremony/attestation payloads or asserted attestation trust.
        var stored = new UserPasskeyInfo(verified.CredentialId, verified.PublicKey, now, verified.SignCount, verified.Transports,
            verified.IsUserVerified, verified.IsBackupEligible, verified.IsBackedUp, [], []);
        var added = await users.AddOrUpdatePasskeyAsync(user, stored).ConfigureAwait(false);
        if (!added.Succeeded) { await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return new("VERSION_CONFLICT"); }
        if (!CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow()) ||
            await authentication.LockForOperationAsync(caller, EnrollmentScope.Operation, ct).ConfigureAwait(false) is null)
        { await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return new("STEP_UP_REQUIRED"); }
        now = clock.GetUtcNow(); assurance = assurancePolicies.RequireEffective(operation.Scope, now, row.AssurancePolicyVersion);
        if (now >= proof.ExpiresAt || now - proof.AuthenticatedAt >= assurance.ProofLifetime || assurance.RequireMethodAssurance(proof.AuthenticationMethod) != proof.Assurance || proof.Assurance != assurance.RequiredAssurance)
        { await transaction.RollbackAsync(ct).ConfigureAwait(false); database.ChangeTracker.Clear(); return new("STEP_UP_REQUIRED"); }
        var credential = new PasskeyCredentialRow { CredentialReference = Guid.NewGuid(), AccountId = caller.AccountId,
            CredentialId = stored.CredentialId, CredentialDigest = CredentialDigest(stored.CredentialId), CreatedAt = now,
            PolicyVersion = policy.Version, ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference };
        database.Add(credential); row.CredentialReference = credential.CredentialReference; row.RequestDigest = digest;
        End(row, "COMPLETED", clock.GetUtcNow()); Event(row, "AuthenticatorAdded", clock.GetUtcNow()); Event(row, "IdentitySecurityActivityRequested", now); Event(row, "IdentitySecurityNotificationRequested", now);
        return await Finish(transaction, new PasskeyResult(null, row), ct).ConfigureAwait(false);
    }
    internal async Task<PasskeyResult> StartSignInAsync(AuthenticationCaller caller, Guid requestId, bool rememberMe, HttpContext http, CancellationToken ct)
    {
        Clean(); caller.Validate(); if (requestId == Guid.Empty) return new("VALIDATION_ERROR");
        var policy = Current(caller.Origin); var now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await authentication.AllowAsync("passkey:N:" + caller.NetworkDigest, auth.NetworkAttemptLimit, auth, now, ct).ConfigureAwait(false))
            return await Finish(transaction, new PasskeyResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        await LockBrowser(caller.BrowserDigest, ct).ConfigureAwait(false);
        var prior = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.RequestId == requestId && x.Kind == "SIGN_IN", ct).ConfigureAwait(false);
        if (prior is not null) return await Finish(transaction, new PasskeyResult(prior.RememberMe == rememberMe && prior.Origin == caller.Origin && prior.PreviousSessionId == caller.PreviousSessionId ? null : "VERSION_CONFLICT", prior), ct).ConfigureAwait(false);
        if (await database.Set<PasskeyCeremonyRow>().CountAsync(x => x.BrowserDigest == caller.BrowserDigest && x.State == "PENDING" && x.ExpiresAt > now, ct).ConfigureAwait(false) >= policy.MaximumPendingCeremonies)
            return await Finish(transaction, new PasskeyResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        var options = await Handler(policy).MakeRequestOptionsAsync(null, http).ConfigureAwait(false);
        var row = new PasskeyCeremonyRow { CeremonyId = Guid.NewGuid(), RequestId = requestId, Kind = "SIGN_IN", BrowserDigest = caller.BrowserDigest,
            Origin = caller.Origin, PolicyVersion = policy.Version, AuthenticationPolicyVersion = auth.Version, CreatedAt = now,
            ExpiresAt = now + policy.CeremonyLifetime, RecoveryExpiresAt = now + policy.CeremonyLifetime + policy.ResultRecoveryLifetime, RememberMe = rememberMe, PreviousSessionId = caller.PreviousSessionId,
            OptionsJson = options.RequestOptionsJson, ProtectedState = stateProtection.Protect(options.AssertionState ?? throw new InvalidOperationException("Native ceremony state is unavailable.")),
            ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference };
        database.Add(row); return await Finish(transaction, new PasskeyResult(null, row), ct).ConfigureAwait(false);
    }
    internal async Task<PasskeyResult> CompleteSignInAsync(AuthenticationCaller caller, PasskeyCompleteCommand command, HttpContext http, CancellationToken ct)
    {
        Clean(); var policy = Current(caller.Origin); var json = CredentialJson(command.Credential, policy); var requestDigest = AuthenticationIdentity.Digest(json);
        var credentialId = ReadCredentialId(command.Credential);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
        if (!await authentication.AllowAsync("passkey:N:" + caller.NetworkDigest, auth.NetworkAttemptLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false))
            return await Finish(transaction, new PasskeyResult("RATE_OR_ABUSE_LIMITED"), ct).ConfigureAwait(false);
        await LockBrowser(caller.BrowserDigest, ct).ConfigureAwait(false); await LockCeremony(command.CeremonyId, ct).ConfigureAwait(false);
        var row = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == command.CeremonyId && x.RequestId == command.RequestId && x.Kind == "SIGN_IN", ct).ConfigureAwait(false);
        if (row is null || row.BrowserDigest != caller.BrowserDigest || row.Origin != caller.Origin || row.PreviousSessionId != caller.PreviousSessionId)
            return await Finish(transaction, new PasskeyResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (row.State == "COMPLETED")
        {
            if (row.RequestDigest != requestDigest) return await Finish(transaction, new PasskeyResult("VERSION_CONFLICT"), ct).ConfigureAwait(false);
            var recovered = await authentication.RecoverPasskeyGrantAsync(row, caller, ct).ConfigureAwait(false);
            return await Finish(transaction, new PasskeyResult(recovered is null ? "AUTHENTICATION_FAILED" : null, row, recovered), ct).ConfigureAwait(false);
        }
        if (row.State != "PENDING" || !CurrentBinding(row, policy, clock.GetUtcNow()))
            return await FailCeremony(transaction, row, "AUTHENTICATION_FAILED", clock.GetUtcNow(), ct).ConfigureAwait(false);
        var credentialDigest = CredentialDigest(credentialId);
        var credential = await database.Set<PasskeyCredentialRow>().SingleOrDefaultAsync(x => x.CredentialDigest == credentialDigest && x.State == "ACTIVE", ct).ConfigureAwait(false);
        if (credential is null) return await FailCeremony(transaction, row, "AUTHENTICATION_FAILED", clock.GetUtcNow(), ct).ConfigureAwait(false);
        // Match mutation order: previous session, Account, framework user, owner, credential. No owner->Account inversion.
        if (caller.PreviousSessionId is { } previous) await authentication.LockSessionAsync(previous, ct).ConfigureAwait(false);
        _ = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={credential.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
        _ = await database.Users.FromSqlInterpolated($"SELECT * FROM identity.\"AspNetUsers\" WHERE \"AccountId\"={credential.AccountId} FOR NO KEY UPDATE").SingleAsync(ct).ConfigureAwait(false);
        await LockCredentialOwner(credential.AccountId, ct).ConfigureAwait(false); await LockCredential(credentialId, ct).ConfigureAwait(false);
        await database.Entry(credential).ReloadAsync(ct).ConfigureAwait(false);
        if (credential.State != "ACTIVE") return await FailCeremony(transaction, row, "AUTHENTICATION_FAILED", clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (!await authentication.AllowAsync("passkey:C:" + credentialDigest, auth.IdentifierAttemptLimit, auth, clock.GetUtcNow(), ct).ConfigureAwait(false))
            return await FailCeremony(transaction, row, "RATE_OR_ABUSE_LIMITED", clock.GetUtcNow(), ct).ConfigureAwait(false);
        var result = await Handler(policy).PerformAssertionAsync(new PasskeyAssertionContext { HttpContext = http, CredentialJson = json,
            AssertionState = stateProtection.Unprotect(row.ProtectedState) }).ConfigureAwait(false);
        if (!result.Succeeded || result.User.AccountId != credential.AccountId)
            return await FailCeremony(transaction, row, "AUTHENTICATION_FAILED", clock.GetUtcNow(), ct).ConfigureAwait(false);
        // Required UV has just been verified by the native handler against this signed assertion. BE is matched; BS is refreshed by it.
        var key = PasskeyPolicy.AssuranceKey(result.Passkey.IsBackupEligible, result.Passkey.IsBackedUp); _ = policy.RequireAssurance(key);
        // Rejection accounting must survive rollback of a later native counter/session write.
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CreateSavepointAsync("verified_passkey", ct).ConfigureAwait(false);
        var added = await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey).ConfigureAwait(false);
        if (!added.Succeeded) return await RollbackRejectedSignIn(transaction, command.CeremonyId, "VERSION_CONFLICT", ct).ConfigureAwait(false);
        if (!CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow()))
            return await RollbackRejectedSignIn(transaction, command.CeremonyId, "AUTHENTICATION_FAILED", ct).ConfigureAwait(false);
        var issued = await authentication.IssuePasskeySessionAsync(caller, row.RequestId, row.RememberMe, result.User, credential, policy, key, ct).ConfigureAwait(false);
        if (issued.ErrorCode is not null || !CurrentBinding(row, Current(caller.Origin), clock.GetUtcNow()))
        {
            return await RollbackRejectedSignIn(transaction, command.CeremonyId, issued.ErrorCode ?? "AUTHENTICATION_FAILED", ct).ConfigureAwait(false);
        }
        row.ResultSessionId = AuthenticationIdentity.SessionId(issued.Grant!.Ticket.Principal); row.AccountId = credential.AccountId;
        row.CredentialReference = credential.CredentialReference; row.RequestDigest = requestDigest;
        credential.LastUsedAt = clock.GetUtcNow(); credential.Version++;
        End(row, "COMPLETED", clock.GetUtcNow()); Event(row, "PasskeyAuthenticated", clock.GetUtcNow());
        return await Finish(transaction, new PasskeyResult(null, row, issued.Grant), ct).ConfigureAwait(false);
    }
    private async Task<PasskeyResult> RollbackRejectedSignIn(IDbContextTransaction transaction, Guid ceremonyId, string error, CancellationToken ct)
    {
        await transaction.RollbackToSavepointAsync("verified_passkey", ct).ConfigureAwait(false); database.ChangeTracker.Clear();
        var row = await database.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == ceremonyId, ct).ConfigureAwait(false);
        return await FailCeremony(transaction, row, error, clock.GetUtcNow(), ct).ConfigureAwait(false);
    }
    public async Task<int> ExpireCeremoniesAsync(CancellationToken cancellationToken = default)
    {
        Clean(); var now = clock.GetUtcNow();
        // Bounded technical worker batch, never a production retention/lifetime default.
        var ids = await database.Set<PasskeyCeremonyRow>().AsNoTracking().Where(x => (x.State == "PENDING" || x.State == "AUTHORIZED") && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt).Take(100).Select(x => x.CeremonyId).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var ended = 0;
        foreach (var id in ids)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var session = await database.Set<PasskeyCeremonyRow>().AsNoTracking().Where(x => x.CeremonyId == id).Select(x => x.SessionId).SingleAsync(cancellationToken).ConfigureAwait(false);
            if (session is { } actorSession) await authentication.LockSessionAsync(actorSession, cancellationToken).ConfigureAwait(false);
            await LockCeremony(id, cancellationToken).ConfigureAwait(false);
            var row = await database.Set<PasskeyCeremonyRow>().SingleAsync(x => x.CeremonyId == id, cancellationToken).ConfigureAwait(false);
            await database.Entry(row).ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (row.State is "PENDING" or "AUTHORIZED" && row.ExpiresAt <= now)
            { End(row, "EXPIRED", now); Event(row, "PasskeyCeremonyExpired", now); ended++; }
            await Finish(transaction, true, cancellationToken).ConfigureAwait(false);
        }
        return ended;
    }
    internal async Task<PasskeyCeremonyRow?> CancelMutationAsync(StepUpCaller caller, Guid ceremonyId, Guid requestId, CancellationToken ct)
    {
        Clean(); await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await authentication.LockForOperationAsync(caller, "CANCEL_PASSKEY_OPERATION", ct).ConfigureAwait(false) is null) return null;
        await LockCeremony(ceremonyId, ct).ConfigureAwait(false);
        var row = await database.Set<PasskeyCeremonyRow>().SingleOrDefaultAsync(x => x.CeremonyId == ceremonyId && x.RequestId == requestId && x.Kind != "SIGN_IN", ct).ConfigureAwait(false);
        if (row is null || !Actor(row, caller)) return null;
        await database.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (row.State is "PENDING" or "AUTHORIZED") { End(row, "REJECTED", clock.GetUtcNow()); Event(row, "PasskeyCeremonyCancelled", clock.GetUtcNow()); }
        return await Finish(transaction, row, ct).ConfigureAwait(false);
    }
    internal async Task<PasskeyCeremonyRow?> ReadMutationResultAsync(StepUpCaller caller, Guid ceremonyId, Guid requestId, CancellationToken ct)
    {
        var row = await database.Set<PasskeyCeremonyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CeremonyId == ceremonyId && x.RequestId == requestId && x.Kind != "SIGN_IN", ct).ConfigureAwait(false);
        return row is not null && Actor(row, caller) ? row : null;
    }
    private static PasskeyExecutionValidity ExecutionValidity(PasskeyCeremonyRow row) => new(row.CeremonyId, row.AccountId!, row.SessionId!.Value, row.Origin, row.PolicyVersion,
        Operation(row).OperationId, Operation(row).ContextDigest, row.ExpiresAt, row.EligibilityPolicyVersion, row.EligibilityEvidenceReference,
        row.EligibilityFrom is { } from && row.EligibilityIncludesFrom is { } includesFrom && row.EligibilityIncludesUntil is { } includesUntil
            ? new EffectivePolicyInterval(from, includesFrom, row.EligibilityUntil, includesUntil) : null);
    internal bool CurrentBinding(PasskeyCeremonyRow row, PasskeyPolicy policy, DateTimeOffset now) => row.PolicyVersion == policy.Version && row.Origin == policy.Origin &&
        row.AuthenticationPolicyVersion == authenticationPolicies.RequireEffective(now).Version && now >= row.CreatedAt && now < row.ExpiresAt;
    internal static string MutationBrowser(StepUpCaller caller) => AuthenticationIdentity.Digest(caller.SessionId.ToString("N") + "|" + caller.AccountId + "|" + caller.Origin);
    private static bool Actor(PasskeyCeremonyRow row, StepUpCaller caller) => row.AccountId == caller.AccountId && row.SessionId == caller.SessionId && row.Origin == caller.Origin;
    private static string CredentialJson(JsonElement credential, PasskeyPolicy policy)
    {
        if (credential.ValueKind != JsonValueKind.Object) throw new ArgumentException("A public credential response is required.");
        var json = credential.GetRawText(); if (System.Text.Encoding.UTF8.GetByteCount(json) > policy.MaximumCredentialJsonBytes) throw new ArgumentException("Credential response is too large.");
        return json;
    }
    private static byte[] ReadCredentialId(JsonElement credential)
    {
        if (!credential.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 and <= 1364 } text)
            throw new ArgumentException("Invalid credential identifier.");
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(text); } catch (FormatException) { throw new ArgumentException("Invalid credential identifier."); }
        if (bytes.Length is < 1 or > 1023) throw new ArgumentException("Invalid credential identifier."); return bytes;
    }
    private static string CredentialDigest(byte[] credential) => Convert.ToHexString(SHA256.HashData(credential));
    private void Clean() { if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new InvalidOperationException("A clean Identity transaction boundary is required."); }
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private Task<int> LockCeremony(Guid id, CancellationToken ct) => Lock("passkey-ceremony:" + id.ToString("N"), ct);
    private Task<int> LockBrowser(string digest, CancellationToken ct) => Lock("auth-browser:" + digest, ct);
    private Task<int> LockCredentialOwner(string accountId, CancellationToken ct) => Lock("passkey-owner:" + accountId, ct);
    private Task<int> LockCredential(byte[] credentialId, CancellationToken ct) => Lock("passkey-credential:" + CredentialDigest(credentialId), ct);
    private Task<int> Lock(string value, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({value},0))", ct);
    private static void End(PasskeyCeremonyRow row, string state, DateTimeOffset now)
    { row.State = state; row.EndedAt = now; row.ProtectedState = string.Empty; row.OptionsJson = string.Empty; row.Version++; }
    private async Task<PasskeyResult> FailCeremony(IDbContextTransaction transaction, PasskeyCeremonyRow row, string code, DateTimeOffset now, CancellationToken ct)
    {
        if (row.State is "PENDING" or "AUTHORIZED") { End(row, now >= row.ExpiresAt ? "EXPIRED" : "REJECTED", now); Event(row, "PasskeyCeremonyRejected", now); }
        return await Finish(transaction, new PasskeyResult(code, row), ct).ConfigureAwait(false);
    }
    private async Task<T> Finish<T>(IDbContextTransaction transaction, T result, CancellationToken ct)
    { await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false); return result; }
    private void Event(PasskeyCeremonyRow row, string type, DateTimeOffset now) => database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", now,
        "tunner/identity", row.Kind == "REVOKE" ? row.CredentialReference!.Value.ToString("N") : row.CeremonyId.ToString("N"), "RESTRICTED_SECURITY",
        JsonSerializer.Serialize(new PasskeySecurityIntent("1", row.CeremonyId, row.CredentialReference, "PASSKEY", row.Kind switch { "ENROLL" => "REGISTER_PASSKEY", "REVOKE" => "REVOKE_PASSKEY", _ => "AUTHENTICATE_PASSKEY" }, type)), row.Version.ToString(CultureInfo.InvariantCulture), "OP-PASSKEY-" + row.CeremonyId.ToString("N"), row.CeremonyId.ToString("N")));
}
