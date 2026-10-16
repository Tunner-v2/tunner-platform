using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Trusted server-resolved policy; none of these values are browser authority or defaults.</summary>
public sealed record RegistrationPolicy(
    string Version, string AgePolicyVersion, string ProcessingReference, string RetentionReference,
    string TermsVersion, string PrivacyVersion, string VerificationTemplateVersion, string ExistingAccountTemplateVersion, string ActivationProfile,
    int PasswordMinimumLength, int PasswordMaximumLength, int PasswordHashIterations,
    int ProofDigits, TimeSpan ProofLifetime, int ProofAttempts, TimeSpan ResendCooldown, int ResendLimit,
    TimeSpan AbuseWindow, int RegisterLimit, int VerifyLimit, int ResendRequestLimit, int LookupLimit,
    TimeSpan BrowserContextLifetime, TimeSpan PendingMaterialRetention)
{
    public void Validate()
    {
        string[] refs = [Version, AgePolicyVersion, ProcessingReference, RetentionReference, TermsVersion, PrivacyVersion,
            VerificationTemplateVersion, ExistingAccountTemplateVersion];
        if (refs.Any(string.IsNullOrWhiteSpace) || ActivationProfile != "PASSWORD_AND_VERIFIED_EMAIL" || PasswordMinimumLength < 15 || PasswordMaximumLength < 64 ||
            PasswordMaximumLength < PasswordMinimumLength || PasswordHashIterations <= 0 || ProofDigits is < 6 or > 9 ||
            ProofLifetime <= TimeSpan.Zero || ProofAttempts <= 0 || ResendCooldown < TimeSpan.Zero || ResendLimit < 0 ||
            AbuseWindow <= TimeSpan.Zero || RegisterLimit <= 0 || VerifyLimit <= 0 || ResendRequestLimit <= 0 || LookupLimit <= 0 ||
            BrowserContextLifetime <= TimeSpan.Zero || PendingMaterialRetention < ProofLifetime)
            throw new InvalidOperationException("Registration requires a complete, supported approved policy.");
    }
}

public interface IRegistrationPolicySource { RegistrationPolicy RequireEffective(DateTimeOffset effectiveAt); }
public sealed record RegistrationPolicyVersion(PolicyVersionCandidate<string> Metadata, RegistrationPolicy Values);

/// <summary>Approval evidence comes from the trusted owner registry, never registration request JSON.</summary>
public sealed class RegistrationPolicyCatalog : IRegistrationPolicySource
{
    private readonly RegistrationPolicyVersion[] versions;
    public RegistrationPolicyCatalog(IEnumerable<RegistrationPolicyVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        this.versions = versions.ToArray();
        foreach (var v in this.versions) { v.Values.Validate(); if (v.Metadata.VersionId != v.Values.Version) throw new ArgumentException("Policy identity mismatch.", nameof(versions)); }
    }
    public RegistrationPolicy RequireEffective(DateTimeOffset effectiveAt)
    {
        var resolution = EffectivePolicyVersionResolver.Resolve(effectiveAt, "REGISTRATION", versions.Select(v => v.Metadata),
            static (a,b) => StringComparer.Ordinal.Equals(a,b), static v => v.Status == "ACTIVE");
        var id = resolution.RequireResolvedVersion().VersionId;
        return versions.Single(v => v.Values.Version == id).Values;
    }
}

public interface IRegistrationPasswordBlocklist
{
    ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken);
}

/// <summary>Digests are derived by the trusted BFF from its protected browser context and actual network peer.</summary>
public sealed record RegistrationCaller(string BrowserDigest, string NetworkDigest)
{
    public void Validate()
    {
        if (BrowserDigest.Length != 64 || NetworkDigest.Length != 64 ||
            !BrowserDigest.All(Uri.IsHexDigit) || !NetworkDigest.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid trusted registration context.");
    }
}

/// <summary>Not a record: generated diagnostics must not print DOB, password or email.</summary>
public sealed class RegisterAccountCommand
{
    public required Guid IdempotencyKey { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required string TermsVersion { get; init; }
    public required string PrivacyVersion { get; init; }
    public required bool AcknowledgedNotices { get; init; }
    public override string ToString() => nameof(RegisterAccountCommand);
}

public sealed record VerifyRegistrationCommand(Guid OperationId, Guid IdempotencyKey, long ExpectedVersion, string Code)
{
    public override string ToString() => nameof(VerifyRegistrationCommand);
}
public sealed record ResendRegistrationCommand(Guid OperationId, Guid IdempotencyKey, long ExpectedVersion);
public sealed record RegistrationResult(Guid OperationId, string CorrelationId, string Status, long Version,
    string? ErrorCode, DateTimeOffset? ExpiresAt, DateTimeOffset? ResendAfter, string? AccountId = null);

/// <summary>Post-commit notification adapter contract. Stable DeliveryId is its idempotency identity.</summary>
public interface IRegistrationNotificationDispatcher
{
    Task QueueAsync(RegistrationNotification notification, CancellationToken cancellationToken);
}
public sealed class RegistrationNotification(Guid deliveryId, string address, string? code, string templateVersion)
{
    public Guid DeliveryId { get; } = deliveryId;
    [JsonIgnore] public string Address { get; } = address;
    [JsonIgnore] public string? Code { get; } = code;
    public string TemplateVersion { get; } = templateVersion;
    public override string ToString() => nameof(RegistrationNotification);
}

public interface IRegistrationApplication
{
    Task<RegistrationResult> FindAsync(RegistrationCaller caller, Guid idempotencyKey, CancellationToken cancellationToken = default);
    Task<RegistrationResult> RegisterAsync(RegistrationCaller caller, RegisterAccountCommand command, CancellationToken cancellationToken = default);
    Task<RegistrationResult> VerifyAsync(RegistrationCaller caller, VerifyRegistrationCommand command, CancellationToken cancellationToken = default);
    Task<RegistrationResult> ResendAsync(RegistrationCaller caller, ResendRegistrationCommand command, CancellationToken cancellationToken = default);
}
