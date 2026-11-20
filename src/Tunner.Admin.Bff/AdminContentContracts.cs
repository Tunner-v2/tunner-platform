using System.Security.Claims;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Admin.Bff;

/// <summary>Explicit transport budgets and workspace scopes; these select no workforce permission or production value.</summary>
public sealed record AdminContentHttpProfile(PolicyVersionCandidate<string> Metadata, IReadOnlyList<string> Scopes, int MaximumRequestBytes)
{
    public AdminContentPresentationBinding? Presentation { get; init; }
    public const string Scope = "ADMIN_CONTENT_HTTP";
    internal void Require(DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != Scope || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) ||
            Scopes is null || Scopes.Count == 0 || Scopes.Any(string.IsNullOrWhiteSpace) ||
            Scopes.Distinct(StringComparer.Ordinal).Count() != Scopes.Count || MaximumRequestBytes < 1)
            throw new ContentUnavailableException();
        if (Presentation is { } binding && (string.IsNullOrWhiteSpace(binding.Scope) || string.IsNullOrWhiteSpace(binding.VariantReference) ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at) || Metadata.ApprovalEvidenceReferences.Count == 0 ||
            Metadata.ApprovalEvidenceReferences.Any(string.IsNullOrWhiteSpace))) throw new ContentUnavailableException();
    }
}
public sealed record AdminContentPresentationBinding(string Scope, string VariantReference, EffectivePolicyInterval EffectiveInterval);
public interface IAdminContentHttpProfileSource { AdminContentHttpProfile RequireEffective(DateTimeOffset at); }

/// <summary>Trusted workforce/session owner receipt derived from the actual authenticated Admin principal.</summary>
public sealed record AdminContentSession(string ActorReference, string SessionReference, ContentActorKind ActorKind,
    string QualificationReference, EffectivePolicyInterval EffectiveInterval);
public interface IAdminContentSessionSource
{
    ValueTask<AdminContentSession?> RequireCurrentAsync(ClaimsPrincipal principal, DateTimeOffset at, CancellationToken cancellationToken);
}
public interface IAdminContentAuthorizationSource
{
    ValueTask<ContentAuthorizationDecision> AuthorizeAsync(AdminContentSession session, ContentAuthorizationRequest request,
        CancellationToken cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentDefinitionQuery
{
    public required string Scope { get; init; }
    public string? Search { get; init; }
    public required int PageSize { get; init; }
    public required int Offset { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentProposalQuery
{
    public required string Scope { get; init; }
    public string? Key { get; init; }
    public string? VariantReference { get; init; }
    public string? Search { get; init; }
    public required int PageSize { get; init; }
    public required int Offset { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminCreateContentDraft
{
    public required Guid IdempotencyKey { get; init; }
    public required string Scope { get; init; }
    public required string Key { get; init; }
    public required string VariantReference { get; init; }
    public required string Text { get; init; }
    public string? ChangeReason { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminChangeContentDraft
{
    public required Guid IdempotencyKey { get; init; }
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
    public required string Text { get; init; }
    public string? ChangeReason { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentCapabilityQuery
{
    public required string Scope { get; init; }
    public required string Target { get; init; }
    public string? Key { get; init; }
    public string? VariantReference { get; init; }
    public Guid? ProposalReference { get; init; }
    public long? ExpectedRevision { get; init; }
    public Guid? PublicationReference { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentProposalCommand
{
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentWriteCommand
{
    public required Guid IdempotencyKey { get; init; }
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentInterval
{
    public required DateTimeOffset From { get; init; }
    public required bool IncludesFrom { get; init; }
    public DateTimeOffset? Until { get; init; }
    public required bool IncludesUntil { get; init; }
    internal EffectivePolicyInterval Value() => new(From, IncludesFrom, Until, IncludesUntil);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminPublishContent
{
    public required Guid IdempotencyKey { get; init; }
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
    public required AdminContentInterval EffectiveInterval { get; init; }
    public Guid? SupersedesPublicationReference { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminRollbackContent
{
    public required Guid IdempotencyKey { get; init; }
    public required Guid ApprovedVersionReference { get; init; }
    public required Guid SupersedesPublicationReference { get; init; }
    public required AdminContentInterval EffectiveInterval { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentHistoryQuery
{
    public required string Scope { get; init; }
    public required string Key { get; init; }
    public required string VariantReference { get; init; }
    public required int PageSize { get; init; }
    public required int Offset { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminContentDiffQuery
{
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
    public required Guid ComparedVersionReference { get; init; }
}
