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
            NativeVerifierVersion is not ("10.0.13" or "10.0.15") || Algorithm != "SHA1" || Digits != 6 || PeriodSeconds != 30 || NativeWindowSteps != 2 ||
            EnrollmentLifetime <= TimeSpan.Zero || ResultRecoveryLifetime <= TimeSpan.Zero || MaximumClockDeviation < TimeSpan.Zero ||
            MaximumVerificationDuration <= TimeSpan.Zero || MaximumVerificationDuration > TimeSpan.FromMinutes(1) ||
            ReplayRetention < TimeSpan.FromSeconds(150) + MaximumClockDeviation * 2 + MaximumVerificationDuration ||
            ReplayHashIterations is < 10_000 or > 1_000_000 || MaximumLiveReplayClaims is < 1 or > 1024 || (long)MaximumLiveReplayClaims * ReplayHashIterations > 10_000_000 ||
            MaximumActiveAuthenticators <= 0 || MaximumPendingOperations <= 0 || !Enum.IsDefined(DisableSessionEffect) ||
            !Enum.IsDefined(ResetSessionEffect) || !Enum.IsDefined(ReplacementOrdering) || !Enum.IsDefined(ResetBehavior) || MfaAssurances is null || MfaAssurances.Count == 0 ||
            MfaAssurances.Any(x => x.Key is not ("pwd" or "passkey" or "federation") || string.IsNullOrWhiteSpace(x.Value) || x.Value.Equals("AAL3", StringComparison.OrdinalIgnoreCase)) ||
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
    public static IReadOnlyList<string> RequiredKeys { get; } = Array.AsReadOnly<string>(["brand.tunner", "passkeys.header.accountCenter", "passkeys.header.account", "passkeys.header.search", "passkeys.header.notifications", "passkeys.header.menu", "passkeys.navigation.account", "passkeys.navigation.overview", "passkeys.navigation.profile", "passkeys.navigation.security", "passkeys.navigation.sessions", "passkeys.navigation.billing", "passkeys.navigation.notifications", "passkeys.navigation.products", "passkeys.navigation.activity", "passkeys.navigation.help", "passkeys.navigation.helpDescription", "totp.heading", "totp.description", "totp.eyebrow", "totp.status.label", "totp.status.activeHeading", "totp.status.activeDescription", "totp.status.inactiveHeading", "totp.status.inactiveDescription", "totp.status.unavailableHeading", "totp.status.unavailableDescription", "totp.status.active", "totp.status.inactive", "totp.status.disabled", "totp.manage.heading", "totp.manage.description", "totp.manage.method", "totp.manage.added", "totp.manage.lastUsed", "totp.manage.notUsed", "totp.manage.guidanceHeading", "totp.manage.guidanceDescription", "totp.manage.guidanceEffects", "totp.manage.removeHeading", "totp.methods.label", "totp.methods.description", "totp.methods.inactiveDescription", "totp.methods.unavailable", "totp.methods.manage", "totp.setup.heading", "totp.setup.description", "totp.setup.scanHeading", "totp.setup.scanDescription", "totp.setup.manualHeading", "totp.setup.manualDescription", "totp.setup.warning", "totp.setup.codeHeading", "totp.setup.codeDescription", "totp.setup.consumedHeading", "totp.setup.consumedDescription", "totp.setup.alreadyShownHeading", "totp.setup.alreadyShownDescription", "totp.setup.guidanceHeading", "totp.setup.guidanceDescription", "totp.setup.compatibleApps", "totp.setup.preparingHeading", "totp.setup.preparingDescription", "totp.setup.verifyingHeading", "totp.setup.verifyingDescription", "totp.setup.expiredHeading", "totp.setup.expiredDescription", "totp.setup.unknownHeading", "totp.setup.unknownDescription", "totp.setup.codeUnavailable", "totp.setup.qrUnavailable", "totp.ready.heading", "totp.ready.description", "totp.disable.heading", "totp.disable.description", "totp.disable.preserve", "totp.disable.affectedSessions", "totp.disable.currentSessionEnds", "totp.disable.confirmDescription", "totp.reset.heading", "totp.reset.description", "totp.reset.separateEnrollment", "totp.reset.preserve", "totp.reset.affectedSessions", "totp.reset.currentSessionEnds", "totp.terminal.disableHeading", "totp.terminal.resetHeading", "totp.terminal.preserveDescription", "totp.terminal.endedHeading", "totp.terminal.endedDescription", "totp.terminal.resetDescription", "totp.terminal.unknown", "totp.code.label", "totp.code.placeholder", "totp.action.setup", "totp.action.reviewReset", "totp.action.remove", "totp.action.verify", "totp.action.cancel", "totp.action.back", "totp.action.done", "totp.action.close", "totp.action.check", "totp.action.signIn", "totp.action.continue", "totp.error.required", "totp.error.code", "totp.error.highlighted", "totp.error.credentials", "totp.error.replayed", "totp.error.limited", "totp.error.changed", "totp.error.unavailable", "totp.error.eligibility", "totp.error.limit", "totp.error.validation", "totp.error.qr", "totp.error.policy", "totp.mfa.heading", "totp.mfa.description", "totp.mfa.pending", "totp.mfa.unknown", "totp.mfa.expiredHeading", "totp.mfa.expiredDescription", "totp.mfa.unavailable", "totp.selection.heading", "totp.selection.proofDescription", "totp.selection.loginDescription", "totp.selection.optionLabel", "totp.selection.added", "totp.selection.numberExplanation", "totp.selection.selectedDescription", "totp.selection.change"]);
    public TotpPresentation(string locale, IEnumerable<RegistrationPublishedText> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (RequiredKeys.Any(x => !values.ContainsKey(x)) || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TotpStartResultCommand
{
    public required Guid RequestId { get; init; }
    public required string Kind { get; init; }
    public Guid? AuthenticatorReference { get; init; }
    public long ExpectedVersion { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TotpProofContinueCommand
{
    public required Guid RequestId { get; init; }
    public required Guid AttemptId { get; init; }
    public required Guid AuthenticatorReference { get; init; }
}
internal sealed record TotpFactorView(Guid AuthenticatorReference, long Version, string State, DateTimeOffset CreatedAt, DateTimeOffset? ActivatedAt, int DisplayPosition);
internal sealed record TotpProofRecovery(Guid RequestId, Guid AttemptId, Guid AuthenticatorReference, string Status, DateTimeOffset ExpiresAt);
internal sealed record TotpOperationResult(string? ErrorCode, TotpOperationRow? Operation = null);
internal sealed class TotpDisclosure(TotpOperationRow operation, string key, string uri)
{
    internal TotpOperationRow Operation { get; } = operation;
    internal string Key { get; } = key;
    internal string Uri { get; } = uri;
    public override string ToString() => nameof(TotpDisclosure);
}
internal sealed record TotpDisclosureResult(string? ErrorCode, TotpOperationRow? Operation = null, TotpDisclosure? Disclosure = null);
