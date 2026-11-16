using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.PresentationContent;

public sealed record PublishedContent(string Key, string VariantReference, string VersionReference, string PublicationReference, string Locale, string Text);
public sealed record ContentResolutionRequest(string Scope, string VariantReference, DateTimeOffset EffectiveAt);
public sealed record ContentResolution(string Scope, string RequestedVariantReference, string EffectiveVariantReference, string Locale,
    IReadOnlyDictionary<string, PublishedContent> Content);
public interface IPresentationContentResolver
{
    ContentResolution Resolve(ContentResolutionRequest request);
    Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default);
}
public sealed class ContentUnavailableException() : InvalidOperationException("Qualified presentation content is unavailable.");
public sealed class ContentConflictException() : InvalidOperationException("The content reference or expected revision changed.");
public sealed class ContentForbiddenException() : InvalidOperationException("The content operation is not authorized.");
public sealed record ContentDefinition(string Key, string ContentClass, bool Mandatory, bool AllowFallback, bool RequiresApproval,
    IReadOnlyList<string> Variables, IReadOnlyList<string> RequiredVariables)
{ public required string Purpose { get; init; } }
public sealed record ContentVariant(string Reference, string Locale, IReadOnlyDictionary<string, string> SafeDimensions);
public sealed record ContentFallback(string RequestedVariantReference, IReadOnlyList<string> OrderedVariantReferences);
public sealed record ContentEffectContract(string AuditAction, string AuditResult, string Source, string EnvelopeVersion, string? EventType, string? EventSchemaVersion);

/// <summary>Trusted owner inputs; no locale, fallback, permissions, copy, retention or approval defaults.</summary>
public sealed record ContentGovernanceProfile(PolicyVersionCandidate<string> Metadata, string Surface, string EvidenceReference,
    string ProcessingReference, string RetentionReference, string AuditRetentionClassReference, string Classification,
    int MaximumTextLength, bool SeparateAuthorApprover, bool SeparateAuthorPublisher, bool SeparateApproverPublisher,
    IReadOnlyList<ContentDefinition> Definitions, IReadOnlyList<ContentVariant> Variants, IReadOnlyList<ContentFallback> Fallbacks,
    IReadOnlyDictionary<ContentOperation, ContentEffectContract> Effects)
{
    internal string Snapshot => JsonSerializer.Serialize(this);
    internal void Require(string scope, DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != scope || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) ||
            new[] { Surface, EvidenceReference, ProcessingReference, RetentionReference, AuditRetentionClassReference }.Any(string.IsNullOrWhiteSpace) ||
            Classification is not ("INTERNAL" or "CONFIDENTIAL" or "RESTRICTED_PERSONAL" or "RESTRICTED_SECURITY") || MaximumTextLength < 1 ||
            Definitions is null || Definitions.Count == 0 || Variants is null || Variants.Count == 0 || Fallbacks is null || Effects is null ||
            Definitions.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != Definitions.Count ||
            Variants.Select(x => x.Reference).Distinct(StringComparer.Ordinal).Count() != Variants.Count ||
            Fallbacks.Select(x => x.RequestedVariantReference).Distinct(StringComparer.Ordinal).Count() != Fallbacks.Count) throw new ContentUnavailableException();
        foreach (var d in Definitions)
            if (string.IsNullOrWhiteSpace(d.Key) || string.IsNullOrWhiteSpace(d.Purpose) || string.IsNullOrWhiteSpace(d.ContentClass) || d.Variables is null || d.RequiredVariables is null ||
                d.Variables.Any(string.IsNullOrWhiteSpace) || d.Variables.Distinct(StringComparer.Ordinal).Count() != d.Variables.Count ||
                d.RequiredVariables.Any(x => !d.Variables.Contains(x, StringComparer.Ordinal))) throw new ContentUnavailableException();
        foreach (var v in Variants)
            if (string.IsNullOrWhiteSpace(v.Reference) || string.IsNullOrWhiteSpace(v.Locale) || v.SafeDimensions is null ||
                v.SafeDimensions.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value))) throw new ContentUnavailableException();
        foreach (var f in Fallbacks)
            if (!Variants.Any(x => x.Reference == f.RequestedVariantReference) || f.OrderedVariantReferences is null ||
                f.OrderedVariantReferences.Count == 0 || f.OrderedVariantReferences[0] != f.RequestedVariantReference ||
                f.OrderedVariantReferences.Distinct(StringComparer.Ordinal).Count() != f.OrderedVariantReferences.Count ||
                f.OrderedVariantReferences.Any(x => !Variants.Any(v => v.Reference == x))) throw new ContentUnavailableException();
        foreach (var (operation, effect) in Effects)
            if (!Enum.IsDefined(operation) || new[] { effect.AuditAction, effect.AuditResult, effect.Source, effect.EnvelopeVersion }.Any(string.IsNullOrWhiteSpace) ||
                ((effect.EventType is null) != (effect.EventSchemaVersion is null)) ||
                (effect.EventType is not null && (string.IsNullOrWhiteSpace(effect.EventType) || string.IsNullOrWhiteSpace(effect.EventSchemaVersion))) ||
                (operation is not (ContentOperation.Preview or ContentOperation.History or ContentOperation.Browse) && effect.EventType is null)) throw new ContentUnavailableException();
    }
}
public interface IPresentationContentProfileSource { ContentGovernanceProfile RequireEffective(string scope, DateTimeOffset effectiveAt); }
public enum ContentOperation { CreateDraft, UpdateDraft, Validate, Preview, Approve, Publish, History, Rollback, Browse }
public enum ContentActorKind { Human, Service, Model }
/// <summary>The trusted server adapter evaluates its actual caller; client identity/permission assertions are not inputs.</summary>
public sealed record ContentAuthorizationRequest(ContentOperation Operation, string Scope, string TargetReference, long ExpectedRevision,
    string CandidateDigest, string ProfileVersion, string OperationId, string CorrelationId)
{
    public bool IsCapabilityProbe { get; init; }
    public string ProfileDigest { get; init; } = "";
    [JsonIgnore] public string Digest => ContentDigest.Of(this);
}
public sealed record ContentAuthorizationDecision(string RequestDigest, bool Allowed, string ActorReference, ContentActorKind ActorKind, string AuditActorType,
    string AuthorizationPolicyVersion, string EvidenceReference, EffectivePolicyInterval EffectiveInterval);
public interface IPresentationContentAuthorizationSource
{ ValueTask<ContentAuthorizationDecision> AuthorizeAsync(ContentAuthorizationRequest request, CancellationToken cancellationToken); }
public sealed record CreateContentDraft(string Scope, string Key, string VariantReference, string Text, string OperationId, string CorrelationId)
{ public string? ChangeReason { get; init; } }
public sealed record ChangeContentDraft(Guid ProposalReference, long ExpectedRevision, string Text, string OperationId, string CorrelationId)
{ public string? ChangeReason { get; init; } }
public sealed record ContentProposalCommand(Guid ProposalReference, long ExpectedRevision, string OperationId, string CorrelationId);
public sealed record PublishContent(Guid ProposalReference, long ExpectedRevision, EffectivePolicyInterval EffectiveInterval,
    Guid? SupersedesPublicationReference, string OperationId, string CorrelationId);
public sealed record RollbackContent(Guid ApprovedVersionReference, Guid SupersedesPublicationReference, EffectivePolicyInterval EffectiveInterval,
    string OperationId, string CorrelationId);
public sealed record ContentProposalView(Guid ProposalReference, long Revision, string State, string Scope, string Key, string VariantReference,
    Guid? VersionReference, string TextDigest)
{ public bool ModelOrigin { get; init; } }
public sealed record ContentPreview(ContentProposalView Proposal, string Text, string ContentClass, IReadOnlyList<string> Variables)
{ public string? ChangeReason { get; init; } }
public sealed record ContentHistoryEntry(Guid PublicationReference, Guid VersionReference, string Key, string VariantReference, string Locale,
    string Text, string TextDigest, EffectivePolicyInterval EffectiveInterval, Guid? SupersedesPublicationReference, string PublisherReference,
    string? ApproverReference, string ProfileVersion)
{ public bool ModelOrigin { get; init; } public string? ChangeReason { get; init; } }
internal static class ContentDigest
{
    internal static string Of<T>(T value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
