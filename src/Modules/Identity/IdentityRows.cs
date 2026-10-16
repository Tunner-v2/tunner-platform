using Microsoft.AspNetCore.Identity;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

/// <summary>Framework adapter key is private to Identity; AccountId is Tunner authority.</summary>
public sealed class IdentityUserAdapter : IdentityUser
{
    public string AccountId { get; set; } = string.Empty;
}

internal sealed class AccountRow
{
    public string AccountId { get; set; } = string.Empty;
    public AccountState State { get; set; }
    public long Version { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string? AgeEligibilityStatus { get; set; }
    public DateTimeOffset? AgeEvaluatedAtUtc { get; set; }
    public string? AgePolicyVersion { get; set; }
    public string? AgeEvidenceReference { get; set; }
}

internal sealed class AccountStateRow
{
    public string AccountId { get; set; } = string.Empty;
    public long Version { get; set; }
    public AccountState State { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
}

internal sealed class VerifiedContactRow
{
    public Guid ContactId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string? NormalizedEmail { get; set; }
    public ContactVerificationState State { get; set; }
    public long Version { get; set; }
    public DateTimeOffset? AnonymizedAtUtc { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
}

internal sealed class ExternalIdentityLinkRow
{
    public Guid LinkId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public long Version { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
}
