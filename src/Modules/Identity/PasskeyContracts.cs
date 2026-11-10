using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit approved owner configuration. This adapter supports required UV and untrusted/none attestation only.</summary>
public enum PasskeyRevocationSessionEffect { PreserveExistingSessions = 1, RevokeCredentialSessions = 2 }
public sealed class PasskeyPolicy
{
    public PasskeyPolicy(string version, string relyingPartyId, string origin, TimeSpan ceremonyLifetime,
        TimeSpan authenticatorTimeout, TimeSpan resultRecoveryLifetime, PasskeyRevocationSessionEffect revocationSessionEffect, string residentKeyRequirement, IReadOnlyCollection<int> allowedAlgorithms, int challengeBytes, int maximumCredentialJsonBytes, int maximumActiveCredentials,
        int maximumPendingCeremonies, string enrollmentAssurancePolicyVersion, string revocationAssurancePolicyVersion,
        string processingReference, string retentionReference, string userVerificationRequirement, string attestationConveyancePreference,
        IReadOnlyDictionary<string, string> assuranceMappings)
    {
        ArgumentNullException.ThrowIfNull(assuranceMappings); ArgumentNullException.ThrowIfNull(allowedAlgorithms);
        if (new[] { version, relyingPartyId, origin, enrollmentAssurancePolicyVersion, revocationAssurancePolicyVersion,
                processingReference, retentionReference }.Any(string.IsNullOrWhiteSpace) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || origin != uri.GetLeftPart(UriPartial.Authority) ||
            Uri.CheckHostName(relyingPartyId) != UriHostNameType.Dns ||
            !(uri.Host == relyingPartyId || uri.Host.EndsWith("." + relyingPartyId, StringComparison.Ordinal)) ||
            ceremonyLifetime <= TimeSpan.Zero || authenticatorTimeout <= TimeSpan.Zero || authenticatorTimeout > ceremonyLifetime ||
            authenticatorTimeout.TotalMilliseconds > uint.MaxValue || challengeBytes < 16 || challengeBytes > 1024 ||
            resultRecoveryLifetime <= TimeSpan.Zero || !Enum.IsDefined(revocationSessionEffect) || residentKeyRequirement != "required" ||
            allowedAlgorithms.Count == 0 || allowedAlgorithms.Count > 32 ||
            userVerificationRequirement != "required" || attestationConveyancePreference != "none" ||
            maximumCredentialJsonBytes < 1024 || maximumCredentialJsonBytes > 1_048_576 || maximumActiveCredentials <= 0 || maximumPendingCeremonies <= 0)
            throw new ArgumentException("A complete supported approved passkey policy is required.");
        var mappings = assuranceMappings.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var supported = new[] { AssuranceKey(false, false), AssuranceKey(true, false), AssuranceKey(true, true) };
        if (mappings.Count == 0 || mappings.Any(x => !supported.Contains(x.Key, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(x.Value)))
            throw new ArgumentException("Explicit verified-fact assurance mappings are required.", nameof(assuranceMappings));
        // This adapter performs no attestation trust evaluation and must never represent a hardware/AAL3 assertion.
        if (mappings.Values.Any(x => x.Equals("AAL3", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Hardware assurance requires a separately approved supported attestation verifier.", nameof(assuranceMappings));
        Version = version; RelyingPartyId = relyingPartyId; Origin = origin; CeremonyLifetime = ceremonyLifetime;
        AuthenticatorTimeout = authenticatorTimeout; ResultRecoveryLifetime = resultRecoveryLifetime; RevocationSessionEffect = revocationSessionEffect;
        AllowedAlgorithms = Array.AsReadOnly(allowedAlgorithms.ToArray()); ChallengeBytes = challengeBytes; MaximumCredentialJsonBytes = maximumCredentialJsonBytes;
        MaximumActiveCredentials = maximumActiveCredentials; MaximumPendingCeremonies = maximumPendingCeremonies;
        EnrollmentAssurancePolicyVersion = enrollmentAssurancePolicyVersion; RevocationAssurancePolicyVersion = revocationAssurancePolicyVersion;
        ProcessingReference = processingReference; RetentionReference = retentionReference;
        AssuranceMappings = new ReadOnlyDictionary<string, string>(mappings);
    }
    public string Version { get; }
    public string RelyingPartyId { get; }
    public string Origin { get; }
    public TimeSpan CeremonyLifetime { get; }
    public TimeSpan AuthenticatorTimeout { get; }
    public TimeSpan ResultRecoveryLifetime { get; }
    public PasskeyRevocationSessionEffect RevocationSessionEffect { get; }
    public IReadOnlyCollection<int> AllowedAlgorithms { get; }
    public int ChallengeBytes { get; }
    public int MaximumCredentialJsonBytes { get; }
    public int MaximumActiveCredentials { get; }
    public int MaximumPendingCeremonies { get; }
    public string EnrollmentAssurancePolicyVersion { get; }
    public string RevocationAssurancePolicyVersion { get; }
    public string ProcessingReference { get; }
    public string RetentionReference { get; }
    public IReadOnlyDictionary<string, string> AssuranceMappings { get; }
    public static string MethodKey(bool backupEligible, bool backedUp) => "passkey:" + AssuranceKey(backupEligible, backedUp);
    public static string AssuranceKey(bool backupEligible, bool backedUp) => $"uv:required;backupEligible:{backupEligible};backedUp:{backedUp};attestation:unverified";
    internal string RequireAssurance(string key) => AssuranceMappings.TryGetValue(key, out var value) ? value : throw new InvalidOperationException("Verified passkey facts have no approved assurance mapping.");
    public override string ToString() => nameof(PasskeyPolicy);
}
/// <summary>Trusted worker entry; expires protected native state without inventing metadata-retention deletion rules.</summary>
public interface IPasskeyCeremonyMaintenance { Task<int> ExpireCeremoniesAsync(CancellationToken cancellationToken = default); }
public interface IPasskeyPolicySource { PasskeyPolicy RequireEffective(DateTimeOffset effectiveAt); }
public sealed record PasskeyPolicyVersion(PolicyVersionCandidate<string> Metadata, PasskeyPolicy Values);
public sealed class PasskeyPolicyCatalog : IPasskeyPolicySource
{
    private readonly PasskeyPolicyVersion[] versions;
    public PasskeyPolicyCatalog(IEnumerable<PasskeyPolicyVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions); this.versions = versions.ToArray();
        if (this.versions.Any(x => x.Metadata.VersionId != x.Values.Version)) throw new ArgumentException("Policy identity mismatch.", nameof(versions));
    }
    public PasskeyPolicy RequireEffective(DateTimeOffset effectiveAt)
    {
        var selected = EffectivePolicyVersionResolver.Resolve(effectiveAt, "PASSKEY", versions.Select(x => x.Metadata),
            static (a, b) => StringComparer.Ordinal.Equals(a, b), static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => x.Values.Version == selected.VersionId).Values;
    }
}
public sealed record PasskeyRevocationEligibilityRequest(string AccountId, long AccountVersion, Guid CredentialReference,
    long CredentialVersion, int RemainingActivePasskeys, bool HasPassword, string AlternativeMethodsDigest, DateTimeOffset EffectiveAt);
public sealed record PasskeyRevocationEligibilityDecision(string AccountId, long AccountVersion, Guid CredentialReference,
    long CredentialVersion, string AlternativeMethodsDigest, bool Allowed, string PolicyVersion, string EvidenceReference, EffectivePolicyInterval EffectiveInterval);
/// <summary>Trusted security owner checks required authentication/recovery paths; no browser or adapter fallback rules.</summary>
public interface IPasskeyRevocationEligibilitySource
{
    ValueTask<PasskeyRevocationEligibilityDecision> RequireAsync(PasskeyRevocationEligibilityRequest request, CancellationToken cancellationToken);
}
public sealed class PasskeyPresentation
{
    private static readonly string[] RequiredKeys = [
        "brand.tunner",
        "passkeys.heading",
        "passkeys.description",
        "passkeys.eyebrow",
        "passkeys.header.accountCenter",
        "passkeys.header.account",
        "passkeys.header.search",
        "passkeys.header.notifications",
        "passkeys.header.menu",
        "passkeys.navigation.account",
        "passkeys.navigation.overview",
        "passkeys.navigation.profile",
        "passkeys.navigation.security",
        "passkeys.navigation.sessions",
        "passkeys.navigation.billing",
        "passkeys.navigation.notifications",
        "passkeys.navigation.products",
        "passkeys.navigation.activity",
        "passkeys.navigation.help",
        "passkeys.navigation.helpDescription",
        "passkeys.security.heading",
        "passkeys.security.description",
        "passkeys.security.eyebrow",
        "passkeys.summary.label",
        "passkeys.summary.empty",
        "passkeys.summary.active",
        "passkeys.summary.emptyDescription",
        "passkeys.summary.activeDescription",
        "passkeys.methods.heading",
        "passkeys.methods.description",
        "passkeys.methods.password",
        "passkeys.methods.passwordAvailable",
        "passkeys.methods.label",
        "passkeys.methods.plural",
        "passkeys.methods.empty",
        "passkeys.methods.added",
        "passkeys.methods.lastUsed",
        "passkeys.methods.revokedAt",
        "passkeys.methods.notUsed",
        "passkeys.methods.manage",
        "passkeys.methods.review",
        "passkeys.methods.otherUnavailable",
        "passkeys.status.active",
        "passkeys.status.revoked",
        "passkeys.guidance.heading",
        "passkeys.guidance.description",
        "passkeys.guidance.control",
        "passkeys.guidance.confirm",
        "passkeys.guidance.native",
        "passkeys.activity.heading",
        "passkeys.activity.unavailable",
        "passkeys.other.heading",
        "passkeys.other.unavailable",
        "passkeys.setup.heading",
        "passkeys.setup.description",
        "passkeys.setup.deviceHeading",
        "passkeys.setup.device",
        "passkeys.setup.deviceDescription",
        "passkeys.setup.cancel",
        "passkeys.setup.submit",
        "passkeys.setup.pending",
        "passkeys.setup.cancelled",
        "passkeys.setup.expired",
        "passkeys.setup.unknown",
        "passkeys.revoke.heading",
        "passkeys.revoke.description",
        "passkeys.revoke.preserve",
        "passkeys.revoke.endSessions",
        "passkeys.revoke.cancel",
        "passkeys.revoke.submit",
        "passkeys.revoke.unknown",
        "passkeys.revoke.signInRequired",
        "passkeys.revoke.notCompleted",
        "passkeys.session.ended",
        "passkeys.success.heading",
        "passkeys.success.description",
        "passkeys.operation.close",
        "passkeys.operation.cancel",
        "passkeys.operation.wait",
        "passkeys.operation.check",
        "passkeys.operation.resume",
        "passkeys.operation.pending",
        "passkeys.operation.signIn",
        "passkeys.operation.retry",
        "passkeys.operation.refresh",
        "passkeys.error.unsupported",
        "passkeys.error.unavailable",
        "passkeys.error.changed",
        "passkeys.error.eligibility",
        "passkeys.error.limited",
        "passkeys.error.limit",
        "passkeys.error.credential",
        "passkeys.error.cancelled",
        "passkeys.error.validation",
        "passkeys.error.policy"];
    public PasskeyPresentation(string locale, IEnumerable<RegistrationPublishedText> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (RequiredKeys.Any(key => !values.ContainsKey(key)) || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
            new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Passkeys require published presentation with provenance.", nameof(content));
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(values);
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
}
public interface IPasskeyPresentationSource { PasskeyPresentation RequirePublished(PasskeyPolicy policy); }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyEnrollmentCommand
{
    public required Guid RequestId { get; init; }
    public override string ToString() => nameof(PasskeyEnrollmentCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyAssertionOptionsCommand
{
    public required Guid RequestId { get; init; }
    public required bool RememberMe { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyCompleteCommand
{
    public required Guid RequestId { get; init; }
    public required Guid CeremonyId { get; init; }
    public required JsonElement Credential { get; init; }
    public override string ToString() => nameof(PasskeyCompleteCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyRevokeCommand
{
    public required Guid RequestId { get; init; }
    public required Guid CredentialReference { get; init; }
    public required long ExpectedVersion { get; init; }
    public override string ToString() => nameof(PasskeyRevokeCommand);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyResultCommand
{
    public required Guid RequestId { get; init; }
    public required Guid CeremonyId { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasskeyTerminalResultCommand
{
    public required Guid RequestId { get; init; }
    public required Guid CeremonyId { get; init; }
    public string? ResultHandle { get; init; }
    public override string ToString() => nameof(PasskeyTerminalResultCommand);
}
internal sealed record PasskeyCapability(bool Available, int? MaximumCredentialJsonBytes);
internal sealed record PasskeyRecoveryView(Guid RequestId, Guid CeremonyId, string Kind, string Status, Guid? CredentialReference,
    long ExpectedVersion, DateTimeOffset ExpiresAt, string ResultHandle, bool SignInRequired, JsonElement? PublicKey = null, bool? RememberMe = null);
internal sealed record PasskeySecurityIntent(string SchemaVersion, Guid OperationReference, Guid? AuthenticatorReference, string AuthenticatorType, string Operation, string EventType);
internal sealed record PasskeyResult(string? ErrorCode, PasskeyCeremonyRow? Ceremony = null, AuthenticationGrant? Grant = null);
