using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public enum TotpSessionEffect { PreserveExistingSessions = 1, RevokeFactorSessions = 2 }
public enum TotpReplacementOrdering { IndependentEnrollment = 1, DisableBeforeEnrollment = 2 }
public enum TotpResetBehavior { RetireAffectedFactorThenSeparateEnrollment = 1 }

/// <summary>Explicit supported owner input. This type supplies no production policy values.</summary>
public sealed record TotpPolicy(string Version, string Origin, string Issuer, string NativeVerifierVersion,
    string Algorithm, int Digits, int PeriodSeconds, int NativeWindowSteps, TimeSpan EnrollmentLifetime,
    TimeSpan ResultRecoveryLifetime, TimeSpan MaximumClockDeviation, TimeSpan MaximumVerificationDuration,
    TimeSpan ReplayRetention, int ReplayHashIterations, int MaximumLiveReplayClaims, int MaximumActiveAuthenticators,
    int MaximumPendingOperations, TotpSessionEffect DisableSessionEffect, TotpSessionEffect ResetSessionEffect,
    TotpReplacementOrdering ReplacementOrdering, TotpResetBehavior ResetBehavior, string EnrollmentAssurancePolicyVersion, string DisableAssurancePolicyVersion,
    string ResetAssurancePolicyVersion, string OperationProofAssurance, IReadOnlyDictionary<string, string> MfaAssurances,
    string SecretBindingVersion, string ProcessingReference, string RetentionReference)
{
    public void Validate()
    {
        if (new[] { Version, Origin, Issuer, EnrollmentAssurancePolicyVersion, DisableAssurancePolicyVersion,
                ResetAssurancePolicyVersion, OperationProofAssurance, SecretBindingVersion, ProcessingReference, RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            !Uri.TryCreate(Origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            Origin != uri.GetLeftPart(UriPartial.Authority) || Issuer.Any(char.IsControl) ||
            NativeVerifierVersion != "10.0.13" || Algorithm != "SHA1" || Digits != 6 || PeriodSeconds != 30 || NativeWindowSteps != 2 ||
            EnrollmentLifetime <= TimeSpan.Zero || ResultRecoveryLifetime <= TimeSpan.Zero || MaximumClockDeviation < TimeSpan.Zero ||
            MaximumVerificationDuration <= TimeSpan.Zero || MaximumVerificationDuration > TimeSpan.FromMinutes(1) ||
            ReplayRetention < TimeSpan.FromSeconds(150) + MaximumClockDeviation * 2 + MaximumVerificationDuration ||
            ReplayHashIterations is < 10_000 or > 1_000_000 || MaximumLiveReplayClaims is < 1 or > 1024 || (long)MaximumLiveReplayClaims * ReplayHashIterations > 10_000_000 ||
            MaximumActiveAuthenticators <= 0 || MaximumPendingOperations <= 0 || !Enum.IsDefined(DisableSessionEffect) ||
            !Enum.IsDefined(ResetSessionEffect) || !Enum.IsDefined(ReplacementOrdering) || !Enum.IsDefined(ResetBehavior) || MfaAssurances is null || MfaAssurances.Count == 0 ||
            MfaAssurances.Any(x => x.Key is not ("pwd" or "passkey") || string.IsNullOrWhiteSpace(x.Value) || x.Value.Equals("AAL3", StringComparison.OrdinalIgnoreCase)) ||
            OperationProofAssurance.Equals("AAL3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A complete supported approved TOTP profile is required.");
    }
    public override string ToString() => nameof(TotpPolicy);
}
public interface ITotpPolicySource { TotpPolicy RequireEffective(DateTimeOffset effectiveAt); }
public sealed record TotpPolicyVersion(PolicyVersionCandidate<string> Metadata, TotpPolicy Values);
public sealed class TotpPolicyCatalog(IEnumerable<TotpPolicyVersion> versions) : ITotpPolicySource
{
    private readonly TotpPolicyVersion[] versions = versions?.ToArray() ?? throw new ArgumentNullException(nameof(versions));
    public TotpPolicy RequireEffective(DateTimeOffset effectiveAt)
    {
        foreach (var item in versions) { item.Values.Validate(); if (item.Metadata.VersionId != item.Values.Version) throw new InvalidOperationException("TOTP policy identity mismatch."); }
        var selected = EffectivePolicyVersionResolver.Resolve(effectiveAt, "TOTP", versions.Select(x => x.Metadata),
            static (a, b) => StringComparer.Ordinal.Equals(a, b), static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => x.Values.Version == selected.VersionId).Values;
    }
}
public sealed record TotpMutationEligibilityRequest(string AccountId, long AccountVersion, Guid AuthenticatorReference,
    long AuthenticatorVersion, string Operation, string AlternativeMethodsDigest, DateTimeOffset EffectiveAt);
public sealed record TotpMutationEligibilityDecision(string AccountId, long AccountVersion, Guid AuthenticatorReference,
    long AuthenticatorVersion, string Operation, string AlternativeMethodsDigest, bool Allowed, string PolicyVersion,
    string EvidenceReference, EffectivePolicyInterval EffectiveInterval);
public interface ITotpMutationEligibilitySource
{
    ValueTask<TotpMutationEligibilityDecision> RequireAsync(TotpMutationEligibilityRequest request, CancellationToken cancellationToken);
}
public sealed record TotpMfaRequirementRequest(string AccountId, long AccountVersion, string PrimaryMethod, string PrimaryAssurance, DateTimeOffset EffectiveAt);
public sealed record TotpMfaRequirementDecision(string AccountId, long AccountVersion, string PrimaryMethod, string PrimaryAssurance,
    bool Required, string PolicyVersion, string EvidenceReference, EffectivePolicyInterval EffectiveInterval, TimeSpan PrimaryEvidenceLifetime);
public interface ITotpMfaRequirementSource
{
    ValueTask<TotpMfaRequirementDecision> RequireAsync(TotpMfaRequirementRequest request, CancellationToken cancellationToken);
}

/// <summary>Private owner binding. No reference/path supplied by a browser is accepted by the provider.</summary>
public sealed record TotpSeedBinding(string AccountId, Guid AuthenticatorReference, Guid Generation, Guid EnrollmentId, string BindingVersion)
{
    public override string ToString() => nameof(TotpSeedBinding);
}
public sealed record TotpSeedReference(string StoreReference, string ResourceReference, long Version)
{
    public override string ToString() => nameof(TotpSeedReference);
}
public sealed class TotpSeedValue(string value) : IDisposable
{
    private string? value = value;
    internal string Require() => value ?? throw new ObjectDisposedException(nameof(TotpSeedValue));
    public void Dispose() => value = null;
    public override string ToString() => "[TOTP seed redacted]";
}
public interface ITotpSeedStore
{
    TotpSeedReference Allocate(TotpSeedBinding binding);
    Task CreateOnceAsync(TotpSeedBinding binding, TotpSeedReference reference, string seed, CancellationToken cancellationToken);
    Task<TotpSeedValue?> ReadAsync(TotpSeedBinding binding, TotpSeedReference reference, CancellationToken cancellationToken);
}
public sealed class TotpPresentation
{
    public TotpPresentation(string locale, IEnumerable<RegistrationPublishedText> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (values.Count == 0 || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
            new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("TOTP requires published content with provenance.", nameof(content));
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(values);
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
}
public interface ITotpPresentationSource { TotpPresentation RequirePublished(TotpPolicy policy); }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TotpStartCommand
{
    public required Guid RequestId { get; init; }
    public Guid? AuthenticatorReference { get; init; }
    public long ExpectedVersion { get; init; }
    public override string ToString() => nameof(TotpStartCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TotpOperationCommand
{
    public required Guid RequestId { get; init; }
    public required Guid EnrollmentId { get; init; }
    public string? ResultHandle { get; init; }
    public override string ToString() => nameof(TotpOperationCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TotpCodeCommand
{
    public required Guid RequestId { get; init; }
    public Guid EnrollmentId { get; init; }
    public Guid AttemptId { get; init; }
    public Guid AuthenticatorReference { get; init; }
    public required string Code { get; init; }
    public override string ToString() => nameof(TotpCodeCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TotpMfaResultCommand
{
    public required Guid RequestId { get; init; }
}
internal sealed record TotpOperationResult(string? ErrorCode, TotpOperationRow? Operation = null);
internal sealed class TotpDisclosure(TotpOperationRow operation, string key, string uri)
{
    internal TotpOperationRow Operation { get; } = operation;
    internal string Key { get; } = key;
    internal string Uri { get; } = uri;
    public override string ToString() => nameof(TotpDisclosure);
}
internal sealed record TotpDisclosureResult(string? ErrorCode, TotpOperationRow? Operation = null, TotpDisclosure? Disclosure = null);
