using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public enum IdentityConfigurationFamily { Authentication, Assurance, AuthenticationContent }
public enum IdentityConfigurationOperation { CreateDraft, UpdateDraft, Validate, Preview, Approve, Publish, History, Rollback }
public enum IdentityConfigurationActorKind { Human, Service, Model }
public sealed record IdentityConfigurationScope(IdentityConfigurationFamily Family, AssurancePolicyScope? AssuranceScope = null, string? AuthenticationPolicyVersion = null)
{
    internal string Key => JsonSerializer.Serialize(this);
    internal void Require()
    {
        if (!Enum.IsDefined(Family) || Family == IdentityConfigurationFamily.Authentication && (AssuranceScope is not null || AuthenticationPolicyVersion is not null) ||
            Family == IdentityConfigurationFamily.Assurance && (AssuranceScope is null || AuthenticationPolicyVersion is not null) ||
            Family == IdentityConfigurationFamily.AuthenticationContent && (AssuranceScope is not null || string.IsNullOrWhiteSpace(AuthenticationPolicyVersion)))
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
                operation is not (IdentityConfigurationOperation.History or IdentityConfigurationOperation.Preview) && effect.EventType is null)
                throw new IdentityConfigurationUnavailableException();
    }
}
public interface IIdentityConfigurationGovernanceSource
{ IdentityConfigurationGovernance RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt); }
public sealed record IdentityConfigurationAuthorizationRequest(IdentityConfigurationOperation Operation, IdentityConfigurationScope Scope,
    string TargetReference, long ExpectedRevision, string CandidateDigest, string GovernanceVersion, string GovernanceDigest, string OperationId, string CorrelationId)
{ [JsonIgnore] public string Digest => IdentityConfigurationDigest.Of(this); }
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
    AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding);
public sealed record IdentityConfigurationHistory(Guid PublicationReference, Guid VersionReference, IdentityConfigurationScope Scope, string PolicyVersionReference,
    string CandidateDigest, EffectivePolicyInterval EffectiveInterval, Guid? SupersedesPublicationReference, string ApproverReference,
    string ApprovalEvidenceReference, string PublisherReference, string GovernanceVersion);
internal static class IdentityConfigurationDigest
{
    internal static string Of<T>(T value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

/// <summary>Internal discriminated persistence format, never a raw-JSON administration API. The supported shapes contain no credential/key/seed fields.</summary>
internal sealed record IdentityConfigurationDocument(AuthenticationPolicyVersion? Authentication = null, AssurancePolicyVersion? Assurance = null,
    AuthenticationContentBinding? ContentBinding = null)
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal string Snapshot => JsonSerializer.Serialize(this);
    internal string Digest => IdentityConfigurationDigest.Of(this);
    internal IdentityConfigurationScope Scope => Authentication is not null ? new(IdentityConfigurationFamily.Authentication) : Assurance is not null
        ? new(IdentityConfigurationFamily.Assurance, Assurance.Metadata.Scope) : new(IdentityConfigurationFamily.AuthenticationContent, AuthenticationPolicyVersion: ContentBinding!.AuthenticationPolicyVersion);
    internal string Version => Authentication?.Values.Version ?? Assurance?.Metadata.VersionId ?? ContentBinding!.ConfigurationVersionReference;
    internal EffectivePolicyInterval Interval => Authentication?.Metadata.EffectiveInterval ?? Assurance?.Metadata.EffectiveInterval ?? ContentBinding!.EffectiveInterval;
    internal void Require()
    {
        if ((Authentication is null ? 0 : 1) + (Assurance is null ? 0 : 1) + (ContentBinding is null ? 0 : 1) != 1) throw new IdentityConfigurationUnavailableException();
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
        Scope.Require();
    }
    internal static IdentityConfigurationDocument Read(string snapshot)
    { var value = JsonSerializer.Deserialize<IdentityConfigurationDocument>(snapshot, Json) ?? throw new IdentityConfigurationUnavailableException(); value.Require(); return value; }
}
