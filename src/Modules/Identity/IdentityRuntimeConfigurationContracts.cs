using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public enum IdentityConfigurationFamily { Authentication, Assurance, AuthenticationContent, Registration, Totp, RegistrationContent, Passkey, TotpContent, PasskeyContent, StepUpContent, RecoveryCodes, SessionListing, SessionListingContent }
public enum IdentityConfigurationOperation { CreateDraft, UpdateDraft, Validate, Preview, Approve, Publish, History, Rollback, Browse }
public enum IdentityConfigurationActorKind { Human, Service, Model }
public sealed record IdentityConfigurationScope(IdentityConfigurationFamily Family, AssurancePolicyScope? AssuranceScope = null, string? AuthenticationPolicyVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TotpPolicyVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PasskeyPolicyVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssurancePolicyVersion = null)
{
    internal string Key => JsonSerializer.Serialize(this);
    internal void Require()
    {
        if (!Enum.IsDefined(Family) || Family != IdentityConfigurationFamily.RegistrationContent && RegistrationPolicyVersion is not null ||
            Family != IdentityConfigurationFamily.TotpContent && TotpPolicyVersion is not null ||
            Family != IdentityConfigurationFamily.PasskeyContent && PasskeyPolicyVersion is not null ||
            Family != IdentityConfigurationFamily.StepUpContent && AssurancePolicyVersion is not null ||
            Family == IdentityConfigurationFamily.StepUpContent && (AssuranceScope is null || AuthenticationPolicyVersion is not null || string.IsNullOrWhiteSpace(AssurancePolicyVersion)) ||
            Family == IdentityConfigurationFamily.TotpContent && (AssuranceScope is not null || AuthenticationPolicyVersion is not null || string.IsNullOrWhiteSpace(TotpPolicyVersion)) ||
            Family == IdentityConfigurationFamily.PasskeyContent && (AssuranceScope is not null || AuthenticationPolicyVersion is not null || string.IsNullOrWhiteSpace(PasskeyPolicyVersion)) ||
            Family == IdentityConfigurationFamily.RegistrationContent && (AssuranceScope is not null || AuthenticationPolicyVersion is not null || string.IsNullOrWhiteSpace(RegistrationPolicyVersion)) ||
            (Family is IdentityConfigurationFamily.Authentication or IdentityConfigurationFamily.Registration or IdentityConfigurationFamily.Totp or IdentityConfigurationFamily.Passkey or IdentityConfigurationFamily.RecoveryCodes or IdentityConfigurationFamily.SessionListing) && (AssuranceScope is not null || AuthenticationPolicyVersion is not null) ||
            Family == IdentityConfigurationFamily.Assurance && (AssuranceScope is null || AuthenticationPolicyVersion is not null) ||
            (Family is IdentityConfigurationFamily.AuthenticationContent or IdentityConfigurationFamily.SessionListingContent) && (AssuranceScope is not null || string.IsNullOrWhiteSpace(AuthenticationPolicyVersion)))
            throw new IdentityConfigurationUnavailableException();
    }
}
public sealed record IdentityConfigurationEffect(string AuditAction, string AuditResult, string Source, string EnvelopeVersion, string? EventType, string? EventSchemaVersion);
/// <summary>Identity security authority only. No Content editor, browser claim or candidate approval string is authority.</summary>
public sealed record IdentityConfigurationGovernance(PolicyVersionCandidate<IdentityConfigurationScope> Metadata, string EvidenceReference,
    string ProcessingReference, string RetentionReference, string AuditRetentionClassReference, string Classification,
    int MaximumPayloadBytes, bool SeparateAuthorApprover, bool SeparateAuthorPublisher, bool SeparateApproverPublisher,
    IReadOnlyDictionary<IdentityConfigurationOperation, IdentityConfigurationEffect> Effects)
{
    internal string Snapshot => JsonSerializer.Serialize(this);
    internal void Require(IdentityConfigurationScope scope, DateTimeOffset at)
    {
        scope.Require();
        if (Metadata is null || Metadata.Scope != scope || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) ||
            new[] { EvidenceReference, ProcessingReference, RetentionReference, AuditRetentionClassReference }.Any(string.IsNullOrWhiteSpace) ||
            Classification is not ("INTERNAL" or "CONFIDENTIAL" or "RESTRICTED_PERSONAL" or "RESTRICTED_SECURITY") || MaximumPayloadBytes < 1 || Effects is null)
            throw new IdentityConfigurationUnavailableException();
        foreach (var (operation, effect) in Effects)
            if (!Enum.IsDefined(operation) || effect is null || new[] { effect.AuditAction, effect.AuditResult, effect.Source, effect.EnvelopeVersion }.Any(string.IsNullOrWhiteSpace) ||
                (effect.EventType is null) != (effect.EventSchemaVersion is null) ||
                (effect.EventType is not null && (string.IsNullOrWhiteSpace(effect.EventType) || string.IsNullOrWhiteSpace(effect.EventSchemaVersion))) ||
                operation is not (IdentityConfigurationOperation.History or IdentityConfigurationOperation.Preview or IdentityConfigurationOperation.Browse) && effect.EventType is null)
                throw new IdentityConfigurationUnavailableException();
    }
}
public interface IIdentityConfigurationGovernanceSource
{ IdentityConfigurationGovernance RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt); }
public sealed record IdentityConfigurationAuthorizationRequest(IdentityConfigurationOperation Operation, IdentityConfigurationScope Scope,
    string TargetReference, long ExpectedRevision, string CandidateDigest, string GovernanceVersion, string GovernanceDigest, string OperationId, string CorrelationId)
{ public bool IsCapabilityProbe { get; init; } [JsonIgnore] public string Digest => IdentityConfigurationDigest.Of(this); }
public sealed record IdentityConfigurationAuthorizationDecision(string RequestDigest, bool Allowed, string ActorReference, IdentityConfigurationActorKind ActorKind,
    string AuditActorType, string AuthorizationPolicyVersion, string EvidenceReference, EffectivePolicyInterval EffectiveInterval);
public interface IIdentityConfigurationAuthorizationSource
{
    /// <summary>Evaluate the actual authenticated server caller and exact Identity-owned capability/scope; never copy client permission assertions.</summary>
    ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken);
}
public sealed class IdentityConfigurationUnavailableException() : InvalidOperationException("Qualified Identity runtime configuration is unavailable.");
public sealed class IdentityConfigurationConflictException() : InvalidOperationException("The Identity configuration revision or publication changed.");
public sealed class IdentityConfigurationForbiddenException() : InvalidOperationException("The Identity configuration operation is not authorized.");
public sealed record IdentityConfigurationCommand(Guid ProposalReference, long ExpectedRevision, string OperationId, string CorrelationId);
public sealed record PublishIdentityConfiguration(Guid ProposalReference, long ExpectedRevision, EffectivePolicyInterval EffectiveInterval,
    Guid? SupersedesPublicationReference, string OperationId, string CorrelationId);
public sealed record RollbackIdentityConfiguration(Guid ApprovedVersionReference, Guid SupersedesPublicationReference, EffectivePolicyInterval EffectiveInterval,
    string OperationId, string CorrelationId);
public sealed record IdentityConfigurationProposal(Guid ProposalReference, long Revision, string State, IdentityConfigurationScope Scope,
    string PolicyVersionReference, Guid? VersionReference, string CandidateDigest, bool ModelOrigin);
public sealed record IdentityConfigurationPreview(IdentityConfigurationProposal Proposal, AuthenticationPolicyVersion? Authentication,
    AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RegistrationPolicyVersion? Registration { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TotpPolicyVersion? Totp { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RegistrationContentBinding? RegistrationContent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PasskeyConfigurationVersion? Passkey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TotpContentBinding? TotpContent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PasskeyContentBinding? PasskeyContent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StepUpContentBinding? StepUpContent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RecoveryCodeConfigurationVersion? RecoveryCodes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionListingHttpBudget? SessionListing { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionListingContentBinding? SessionListingContent { get; init; }
}
public sealed record IdentityConfigurationHistory(Guid PublicationReference, Guid VersionReference, IdentityConfigurationScope Scope, string PolicyVersionReference,
    string CandidateDigest, EffectivePolicyInterval EffectiveInterval, Guid? SupersedesPublicationReference, string ApproverReference,
    string ApprovalEvidenceReference, string PublisherReference, string GovernanceVersion);
internal static class IdentityConfigurationDigest
{
    internal static string Of<T>(T value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

/// <summary>Internal discriminated persistence format, never a raw-JSON administration API. The supported shapes contain no credential/key/seed fields.</summary>
internal sealed record IdentityConfigurationDocument(AuthenticationPolicyVersion? Authentication = null, AssurancePolicyVersion? Assurance = null,
    AuthenticationContentBinding? ContentBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationPolicyVersion? Registration = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpPolicyVersion? Totp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationContentBinding? RegistrationContent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyConfigurationVersion? Passkey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpContentBinding? TotpContent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyContentBinding? PasskeyContent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StepUpContentBinding? StepUpContent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecoveryCodeConfigurationVersion? RecoveryCodes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionListingHttpBudget? SessionListing = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionListingContentBinding? SessionListingContent = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new PolicyMetadataConverter<string>(), new PolicyMetadataConverter<AssurancePolicyScope>() }
    };
    // The shared immutable contract intentionally accepts IEnumerable while exposing IReadOnlyList.
    // Keep persistence constructor binding local rather than changing that public policy contract.
    private sealed record PolicyMetadata<TScope>(string VersionId, string Status, TScope Scope,
        EffectivePolicyInterval EffectiveInterval, string? SupersedesVersionId, IReadOnlyList<string> ApprovalEvidenceReferences) where TScope : notnull;
    private sealed class PolicyMetadataConverter<TScope> : JsonConverter<PolicyVersionCandidate<TScope>> where TScope : notnull
    {
        public override PolicyVersionCandidate<TScope> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var metadata = JsonSerializer.Deserialize<PolicyMetadata<TScope>>(ref reader, options) ?? throw new JsonException();
            return new(metadata.VersionId, metadata.Status, metadata.Scope, metadata.EffectiveInterval,
                metadata.SupersedesVersionId, metadata.ApprovalEvidenceReferences);
        }
        public override void Write(Utf8JsonWriter writer, PolicyVersionCandidate<TScope> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new PolicyMetadata<TScope>(value.VersionId, value.Status, value.Scope, value.EffectiveInterval,
                value.SupersedesVersionId, value.ApprovalEvidenceReferences), options);
    }
    internal string Snapshot => JsonSerializer.Serialize(this);
    internal string Digest => IdentityConfigurationDigest.Of(this);
    internal IdentityConfigurationScope Scope => Authentication is not null ? new(IdentityConfigurationFamily.Authentication) : Assurance is not null
        ? new(IdentityConfigurationFamily.Assurance, Assurance.Metadata.Scope) : Registration is not null ? new(IdentityConfigurationFamily.Registration)
        : Totp is not null ? new(IdentityConfigurationFamily.Totp) : Passkey is not null ? new(IdentityConfigurationFamily.Passkey) : RegistrationContent is not null ? new(IdentityConfigurationFamily.RegistrationContent, RegistrationPolicyVersion: RegistrationContent.RegistrationPolicyVersion)
        : TotpContent is not null ? new(IdentityConfigurationFamily.TotpContent, TotpPolicyVersion: TotpContent.TotpPolicyVersion)
        : PasskeyContent is not null ? new(IdentityConfigurationFamily.PasskeyContent, PasskeyPolicyVersion: PasskeyContent.PasskeyPolicyVersion)
        : StepUpContent is not null ? new(IdentityConfigurationFamily.StepUpContent, StepUpContent.AssuranceScope, AssurancePolicyVersion: StepUpContent.AssurancePolicyVersion)
        : RecoveryCodes is not null ? new(IdentityConfigurationFamily.RecoveryCodes)
        : SessionListing is not null ? new(IdentityConfigurationFamily.SessionListing)
        : SessionListingContent is not null ? new(IdentityConfigurationFamily.SessionListingContent, AuthenticationPolicyVersion: SessionListingContent.AuthenticationPolicyVersion)
        : new(IdentityConfigurationFamily.AuthenticationContent, AuthenticationPolicyVersion: ContentBinding!.AuthenticationPolicyVersion);
    internal string Version => Authentication?.Values.Version ?? Assurance?.Metadata.VersionId ?? Registration?.Values.Version ?? Totp?.Values.Version ?? Passkey?.Values.Version ?? RegistrationContent?.ConfigurationVersionReference ?? TotpContent?.ConfigurationVersionReference ?? PasskeyContent?.ConfigurationVersionReference ?? StepUpContent?.ConfigurationVersionReference ?? RecoveryCodes?.Values.Version ?? SessionListing?.Metadata.VersionId ?? SessionListingContent?.ConfigurationVersionReference ?? ContentBinding!.ConfigurationVersionReference;
    internal EffectivePolicyInterval Interval => Authentication?.Metadata.EffectiveInterval ?? Assurance?.Metadata.EffectiveInterval ?? Registration?.Metadata.EffectiveInterval ?? Totp?.Metadata.EffectiveInterval ?? Passkey?.Metadata.EffectiveInterval ?? RegistrationContent?.EffectiveInterval ?? TotpContent?.EffectiveInterval ?? PasskeyContent?.EffectiveInterval ?? StepUpContent?.EffectiveInterval ?? RecoveryCodes?.Metadata.EffectiveInterval ?? SessionListing?.Metadata.EffectiveInterval ?? SessionListingContent?.EffectiveInterval ?? ContentBinding!.EffectiveInterval;
    internal void Require()
    {
        if ((Authentication is null ? 0 : 1) + (Assurance is null ? 0 : 1) + (ContentBinding is null ? 0 : 1) + (Registration is null ? 0 : 1) + (Totp is null ? 0 : 1) + (RegistrationContent is null ? 0 : 1) + (Passkey is null ? 0 : 1) + (TotpContent is null ? 0 : 1) + (PasskeyContent is null ? 0 : 1) + (StepUpContent is null ? 0 : 1) + (RecoveryCodes is null ? 0 : 1) + (SessionListing is null ? 0 : 1) + (SessionListingContent is null ? 0 : 1) != 1) throw new IdentityConfigurationUnavailableException();
        if (Authentication is { } authentication)
        {
            if (authentication.Metadata is null || authentication.Values is null) throw new IdentityConfigurationUnavailableException();
            _ = new AuthenticationPolicyCatalog([authentication]);
            if (authentication.Metadata.Scope != "AUTHENTICATION" || authentication.Metadata.Status != "ACTIVE") throw new IdentityConfigurationUnavailableException();
        }
        if (Assurance is { } assurance)
        {
            if (assurance.Metadata is null || assurance.Metadata.Scope is null) throw new IdentityConfigurationUnavailableException();
            _ = new AssurancePolicyVersion(assurance.Metadata, assurance.RequiredAssurance, assurance.ProofLifetime, assurance.MethodAssurances);
            if (assurance.Metadata.Status != "ACTIVE") throw new IdentityConfigurationUnavailableException();
        }
        if (ContentBinding is { } binding)
        {
            if (new[] { binding.AuthenticationPolicyVersion, binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference }.Any(string.IsNullOrWhiteSpace) ||
                binding.EffectiveInterval is null || binding.TermsUri is null || binding.PrivacyUri is null) throw new IdentityConfigurationUnavailableException();
            foreach (var uri in new[] { binding.TermsUri, binding.PrivacyUri, binding.SupportUri }.OfType<Uri>())
                if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0) throw new IdentityConfigurationUnavailableException();
        }
        if (Registration is { } registration)
        {
            if (registration.Metadata is null || registration.Values is null) throw new IdentityConfigurationUnavailableException();
            _ = new RegistrationPolicyCatalog([registration]);
            if (registration.Metadata.Scope != "REGISTRATION" || registration.Metadata.Status != "ACTIVE") throw new IdentityConfigurationUnavailableException();
        }
        if (Totp is { } totp)
        {
            if (totp.Metadata is null || totp.Values is null) throw new IdentityConfigurationUnavailableException();
            totp.Values.Validate(); _ = new TotpPolicyCatalog([totp]);
            if (totp.Metadata.VersionId != totp.Values.Version || totp.Metadata.Scope != "TOTP" || totp.Metadata.Status != "ACTIVE") throw new IdentityConfigurationUnavailableException();
        }
        if (RegistrationContent is { } registrationContent)
        {
            if (new[] { registrationContent.RegistrationPolicyVersion, registrationContent.TermsVersion, registrationContent.PrivacyVersion,
                registrationContent.ConfigurationVersionReference, registrationContent.QualificationReference, registrationContent.ContentScope, registrationContent.VariantReference }.Any(string.IsNullOrWhiteSpace) ||
                registrationContent.EffectiveInterval is null || registrationContent.TermsUri is null || registrationContent.PrivacyUri is null ||
                new[] { registrationContent.TermsUri, registrationContent.PrivacyUri, registrationContent.SupportUri }.OfType<Uri>().Any(uri =>
                    !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)) throw new IdentityConfigurationUnavailableException();
        }
        if (Passkey is { } passkey)
        {
            if (passkey.Metadata is null || passkey.Values is null || passkey.Metadata.Scope != "PASSKEY" || passkey.Metadata.Status != "ACTIVE") throw new IdentityConfigurationUnavailableException();
            _ = new PasskeyPolicyCatalog([new(passkey.Metadata, passkey.Values.ToPolicy())]);
        }
        if (TotpContent is { } totpContent)
            RequireFactorBinding(totpContent.TotpPolicyVersion, totpContent.ConfigurationVersionReference, totpContent.QualificationReference, totpContent.ContentScope, totpContent.VariantReference, totpContent.EffectiveInterval);
        if (PasskeyContent is { } passkeyContent)
            RequireFactorBinding(passkeyContent.PasskeyPolicyVersion, passkeyContent.ConfigurationVersionReference, passkeyContent.QualificationReference, passkeyContent.ContentScope, passkeyContent.VariantReference, passkeyContent.EffectiveInterval);
        StepUpContent?.Require();
        if (RecoveryCodes is { } recoveryCodes)
        {
            if (recoveryCodes.Metadata is null || recoveryCodes.Values is null || recoveryCodes.Metadata.Scope != "RECOVERY_CODES" || recoveryCodes.Metadata.Status != "ACTIVE")
                throw new IdentityConfigurationUnavailableException();
            var policy = recoveryCodes.Values.ToPolicy();
            if (recoveryCodes.Metadata.VersionId != policy.Version) throw new IdentityConfigurationUnavailableException();
        }
        if (SessionListing is { } listing)
        {
            _ = new SessionListingHttpBudget(listing.Metadata, listing.MaximumRows, listing.MaximumRequestBytes);
            if (listing.Metadata.Scope != SessionListingHttpBudget.Scope || listing.Metadata.Status != "ACTIVE") throw new IdentityConfigurationUnavailableException();
        }
        if (SessionListingContent is { } listingContent)
            RequireFactorBinding(listingContent.AuthenticationPolicyVersion, listingContent.ConfigurationVersionReference, listingContent.QualificationReference,
                listingContent.ContentScope, listingContent.VariantReference, listingContent.EffectiveInterval);
        Scope.Require();
    }
    private static void RequireFactorBinding(string policy, string configuration, string qualification, string scope, string variant, EffectivePolicyInterval interval)
    {
        if (new[] { policy, configuration, qualification, scope, variant }.Any(string.IsNullOrWhiteSpace) || interval is null)
            throw new IdentityConfigurationUnavailableException();
    }
    internal static IdentityConfigurationDocument Read(string snapshot)
    { var value = JsonSerializer.Deserialize<IdentityConfigurationDocument>(snapshot, Json) ?? throw new IdentityConfigurationUnavailableException(); value.Require(); return value; }
}
