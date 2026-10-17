namespace Tunner.Accounts;

public enum AccountState
{
    VerificationPending,
    Active,
    Restricted,
    Closing,
    Closed
}

public enum ContactVerificationState
{
    Unverified,
    Verified,
    Superseded,
    Revoked
}

/// <summary>Account-owned projection; framework identity objects never cross this boundary.</summary>
public sealed record AccountSnapshot(
    string AccountId,
    AccountState State,
    long Version,
    AgeEligibilityStatus? AgeEligibilityStatus,
    DateTimeOffset? AgeEvaluatedAt,
    string? AgePolicyVersion,
    string? AgeEvidenceReference);
