using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Optional trusted-owner outcome. A terminal receipt remains private and is independently verified by Identity.</summary>
public sealed class UserStepUpOperationApplyOutcome : IDisposable
{
    private RecoveryCodeFirstDelivery? recoveryDelivery;
    private UserStepUpOperationApplyOutcome(bool applied, AuthenticationSessionTerminationReceipt? receipt, PasskeyExecutionValidity? validity = null,
        TotpExecutionValidity? totpValidity = null, RecoveryCodeExecutionValidity? recoveryValidity = null, RecoveryCodeFirstDelivery? recoveryDelivery = null)
    { Applied = applied; Receipt = receipt; PasskeyValidity = validity; TotpValidity = totpValidity; RecoveryValidity = recoveryValidity; this.recoveryDelivery = recoveryDelivery; }
    public bool Applied { get; }
    internal AuthenticationSessionTerminationReceipt? Receipt { get; }
    internal PasskeyExecutionValidity? PasskeyValidity { get; }
    internal TotpExecutionValidity? TotpValidity { get; }
    internal RecoveryCodeExecutionValidity? RecoveryValidity { get; }
    internal UserStepUpOperationApplyOutcome ForPasskey(PasskeyExecutionValidity validity) => new(Applied, Receipt, validity, TotpValidity, RecoveryValidity, TakeRecoveryDelivery());
    internal UserStepUpOperationApplyOutcome ForTotp(TotpExecutionValidity validity) => new(Applied, Receipt, PasskeyValidity, validity, RecoveryValidity, TakeRecoveryDelivery());
    internal RecoveryCodeFirstDelivery? TakeRecoveryDelivery() => Interlocked.Exchange(ref recoveryDelivery, null);
    internal bool ClosedFor(StepUpOperation operation) => RecoveryCodeApplication.Owns(operation.Scope)
        ? Applied && Receipt is null && PasskeyValidity is null && TotpValidity is null && RecoveryValidity is not null && recoveryDelivery is not null && RecoveryValidity.Matches(recoveryDelivery)
        : RecoveryValidity is null && recoveryDelivery is null && !(PasskeyValidity is not null && TotpValidity is not null);
    public static UserStepUpOperationApplyOutcome Conflict() => new(false, null);
    public static UserStepUpOperationApplyOutcome SessionContinues() => new(true, null);
    internal static UserStepUpOperationApplyOutcome SessionEnded(AuthenticationSessionTerminationReceipt receipt) => new(true, receipt);
    internal static UserStepUpOperationApplyOutcome RecoveryCodes(RecoveryCodeFirstDelivery delivery, RecoveryCodeExecutionValidity validity) => new(true, null, null, null, validity, delivery);
    public void Dispose() => TakeRecoveryDelivery()?.Dispose();
    public override string ToString() => nameof(UserStepUpOperationApplyOutcome);
}
public interface IUserStepUpOperationOutcomeSource : IUserStepUpOperationSource
{
    Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation,
        IdentityPersistenceDbContext database, CancellationToken cancellationToken);
}
/// <summary>Optional browser-owner result hook after the Identity transaction committed.</summary>
public interface IUserStepUpBrowserCompletionSource
{
    Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken cancellationToken);
}
internal sealed record PasskeyExecutionValidity(Guid CeremonyId, string AccountId, Guid SessionId, string Origin, string PolicyVersion,
    string OperationId, string ContextDigest, DateTimeOffset ExpiresAt, string? EligibilityPolicyVersion, string? EligibilityEvidenceReference, EffectivePolicyInterval? EligibilityInterval);
internal sealed record AuthenticationOperationVerification(EffectivePolicyInterval SessionUseInterval, EffectivePolicyInterval OperationInterval);
internal sealed record AuthenticationTerminalVerification(EffectivePolicyInterval AccessInterval);
internal sealed record AuthenticationSessionTerminationReceipt(Guid SessionId, string AccountId, string Origin, string OperationId,
    string CorrelationId, string ContextDigest, Guid CredentialReference, long CredentialVersion, string PasskeyPolicyVersion,
    long PriorSessionVersion, long EndedSessionVersion, string AuthenticatorType = "PASSKEY", Guid? Generation = null);
internal sealed record AuthenticatorProofSource(PasskeyCredentialRow? Passkey, TotpAuthenticatorRow? Totp);
internal sealed record TotpExecutionValidity(Guid EnrollmentId, string AccountId, Guid SessionId, string Origin, string PolicyVersion,
    string OperationId, string ContextDigest, DateTimeOffset ExpiresAt, string? EligibilityPolicyVersion, string? EligibilityEvidenceReference, EffectivePolicyInterval? EligibilityInterval);
