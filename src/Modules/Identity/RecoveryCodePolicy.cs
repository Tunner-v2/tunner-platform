using System.Collections.ObjectModel;
using System.Text.Json;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public enum RecoveryCodeGenerationAdmission { WithinApprovedActiveSetCapacity = 1 }
public enum RecoveryCodeReplacementMechanism { RetireExactActiveSetsAtIssueCommit = 1 }
public enum RecoveryCodeSessionEffect { NoSessionMutation = 1 }
public enum RecoveryCodeDeliveryMechanism { OneVolatileViewWithOptionalSingleLocalDownload = 1 }

/// <summary>Complete explicit owner inputs for the supported mechanism; no production policy is selected.</summary>
public sealed record RecoveryCodePolicy(string Version, string Origin, RecoveryCodeCryptoProfile Crypto,
    string FormatProfileVersion, string DeliveryProfileVersion, TimeSpan OperationLifetime, TimeSpan ResultRecoveryLifetime,
    int MaximumActiveSets, int MaximumPendingOperations, RecoveryCodeGenerationAdmission GenerationAdmission,
    RecoveryCodeReplacementMechanism ReplacementMechanism, RecoveryCodeSessionEffect SessionEffect,
    RecoveryCodeDeliveryMechanism DeliveryMechanism, string GenerateAssurancePolicyVersion, string ReplaceAssurancePolicyVersion,
    int ConsumptionAccountAttemptLimit, int ConsumptionNetworkAttemptLimit, string ProcessingReference, string RetentionReference)
{
    public void Validate()
    {
        if (new[] { Version, Origin, DeliveryProfileVersion, GenerateAssurancePolicyVersion, ReplaceAssurancePolicyVersion, ProcessingReference, RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            Crypto is null || FormatProfileVersion != "NATIVE_HEX32_V1" || !Uri.TryCreate(Origin, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.UserInfo.Length != 0 || Origin != origin.GetLeftPart(UriPartial.Authority) || OperationLifetime <= TimeSpan.Zero || ResultRecoveryLifetime <= TimeSpan.Zero ||
            MaximumActiveSets <= 0 || MaximumPendingOperations <= 0 || ConsumptionAccountAttemptLimit <= 0 || ConsumptionNetworkAttemptLimit <= 0 ||
            GenerationAdmission != RecoveryCodeGenerationAdmission.WithinApprovedActiveSetCapacity || ReplacementMechanism != RecoveryCodeReplacementMechanism.RetireExactActiveSetsAtIssueCommit ||
            SessionEffect != RecoveryCodeSessionEffect.NoSessionMutation || DeliveryMechanism != RecoveryCodeDeliveryMechanism.OneVolatileViewWithOptionalSingleLocalDownload)
            throw new InvalidOperationException("A complete supported approved recovery-code policy is required.");
    }
    public override string ToString() => nameof(RecoveryCodePolicy);
}

public sealed record RecoveryCodePolicyVersion(PolicyVersionCandidate<string> Metadata, RecoveryCodePolicy Values)
{
    internal void Require(string origin, DateTimeOffset at)
    {
        Values.Validate();
        if (Metadata.VersionId != Values.Version || Metadata.Scope != "RECOVERY_CODES" || Metadata.Status != "ACTIVE" ||
            !Metadata.EffectiveInterval.Contains(at) || Values.Origin != origin)
            throw new InvalidOperationException("Current recovery-code policy authority is unavailable.");
    }
    internal string Digest() => AuthenticationIdentity.Digest(JsonSerializer.Serialize(new { Metadata, Values }));
    public override string ToString() => nameof(RecoveryCodePolicyVersion);
}
public interface IRecoveryCodePolicySource { RecoveryCodePolicyVersion RequireEffective(DateTimeOffset effectiveAt); }
public sealed class RecoveryCodePolicyCatalog(IEnumerable<RecoveryCodePolicyVersion> versions) : IRecoveryCodePolicySource
{
    private readonly RecoveryCodePolicyVersion[] versions = versions?.ToArray() ?? throw new ArgumentNullException(nameof(versions));
    public RecoveryCodePolicyVersion RequireEffective(DateTimeOffset effectiveAt)
    {
        foreach (var version in versions)
        {
            version.Values.Validate();
            if (version.Values.Version != version.Metadata.VersionId) throw new InvalidOperationException("Recovery-code policy identity mismatch.");
        }
        var selected = EffectivePolicyVersionResolver.Resolve(effectiveAt, "RECOVERY_CODES", versions.Select(x => x.Metadata),
            static (a, b) => a == b, static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => ReferenceEquals(x.Metadata, selected));
    }
}

public sealed record RecoveryCodeImpactFacts(string Kind, int PriorSetCount, int RemainingCodeCount, string FactsDigest,
    RecoveryCodeReplacementMechanism ReplacementMechanism, RecoveryCodeSessionEffect SessionEffect);

/// <summary>Publication must be selected for the exact current owner facts, including replacement impact.</summary>
public sealed class RecoveryCodePresentation
{
    public RecoveryCodePresentation(string locale, IEnumerable<RegistrationPublishedText> content, string impactKey, string impactFactsDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(impactKey) || impactFactsDigest is not { Length: 64 } || !impactFactsDigest.All(Uri.IsHexDigit) ||
            !values.ContainsKey(impactKey) || RequiredKeys.Any(x => !values.ContainsKey(x)) || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
                new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Recovery codes require complete current published impact and content provenance.", nameof(content));
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(values); ImpactKey = impactKey; ImpactFactsDigest = impactFactsDigest;
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
    public string ImpactKey { get; }
    public string ImpactFactsDigest { get; }
    public static IReadOnlyList<string> RequiredKeys { get; } = Array.AsReadOnly<string>([
        "recoveryCodes.heading", "recoveryCodes.card.description", "recoveryCodes.card.unavailable", "recoveryCodes.action.generate", "recoveryCodes.action.replace",
        "recoveryCodes.action.cancel", "recoveryCodes.action.continue", "recoveryCodes.action.close", "recoveryCodes.action.check", "recoveryCodes.action.download", "recoveryCodes.action.done",
        "recoveryCodes.review.heading", "recoveryCodes.review.description", "recoveryCodes.review.details", "recoveryCodes.review.unavailable", "recoveryCodes.reveal.heading",
        "recoveryCodes.reveal.description", "recoveryCodes.reveal.once", "recoveryCodes.reveal.warning", "recoveryCodes.download.requested", "recoveryCodes.download.description",
        "recoveryCodes.unavailable.heading", "recoveryCodes.unavailable.description", "recoveryCodes.presented.heading", "recoveryCodes.presented.description", "recoveryCodes.unknown.heading", "recoveryCodes.unknown.description"]);
}
public interface IRecoveryCodePresentationSource
{
    RecoveryCodePresentation RequirePublished(RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts);
}
