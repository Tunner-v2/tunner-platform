using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Tunner.PresentationContent;

[JsonConverter(typeof(JsonStringEnumConverter<ContentCapabilityTarget>))]
public enum ContentCapabilityTarget { Workspace, Definition, Proposal, History }
public sealed record ContentCapabilityQuery(string Scope, ContentCapabilityTarget Target, string? Key, string? VariantReference,
    Guid? ProposalReference, long? ExpectedRevision, Guid? PublicationReference, string OperationId, string CorrelationId);
/// <summary>Advisory current eligibility only. A probe decision never authorizes a later command or its body/interval.</summary>
public sealed record ContentCapabilities(ContentCapabilityTarget Target, string Scope, string ProfileVersion, string? Key, string? VariantReference,
    Guid? ProposalReference, long? ExpectedRevision, Guid? PublicationReference, IReadOnlyList<string> AvailableActions);

public sealed partial class PresentationContentApplication
{
    public async Task<ContentCapabilities> CapabilitiesAsync(ContentCapabilityQuery command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); RequireCapabilityTarget(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var profile = Profile(command.Scope);
        await CheckBrowseProfileAsync(profile, ct).ConfigureAwait(false);
        ContentProposalRow? proposal = null; ContentVersionRow? version = null;
        var key = command.Key; var variant = command.VariantReference;
        if (command.Target == ContentCapabilityTarget.Proposal)
        {
            proposal = await ProposalAsync(command.ProposalReference!.Value, command.ExpectedRevision!.Value, ct).ConfigureAwait(false);
            if (proposal.Scope != command.Scope) throw new ContentConflictException();
            profile = await ProposalProfileAsync(proposal, ct).ConfigureAwait(false); key = proposal.Key; variant = proposal.VariantReference;
        }
        else if (command.Target == ContentCapabilityTarget.History)
        {
            var publication = await database.Publications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.PublicationReference, ct).ConfigureAwait(false);
            if (publication is null || publication.Scope != command.Scope || publication.ProfileVersion != profile.Metadata.VersionId) throw new ContentConflictException();
            version = await database.Versions.AsNoTracking().SingleAsync(x => x.Id == publication.VersionReference, ct).ConfigureAwait(false);
            proposal = await ProposalAsync(version.ProposalReference, null, ct).ConfigureAwait(false);
            profile = await ProposalProfileAsync(proposal, ct).ConfigureAwait(false); key = version.Key; variant = version.VariantReference;
        }
        if (key is not null) _ = Definition(profile, key);
        if (variant is not null) _ = Variant(profile, variant);
        var snapshot = new { command.Target, command.Scope, key, variant, proposalReference = proposal?.Id,
            revision = proposal?.Revision, state = proposal?.State, versionReference = proposal?.VersionReference, approverReference = proposal?.ApproverReference,
            contributors = proposal?.ContributorsJson, textDigest = proposal is null ? null : ContentDigest.Of(proposal.Text),
            command.PublicationReference, comparedVersion = version?.Id };
        var read = Request(ContentOperation.Browse, profile, proposal?.Id.ToString("N") ?? (key is null ? command.Scope : key + ":" + variant),
            proposal?.Revision ?? 0, ContentDigest.Of(snapshot), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(read, ct).ConfigureAwait(false);
        var actions = new List<string>();
        async Task Probe(ContentOperation operation, string target, bool eligible = true)
        {
            if (!eligible || !profile.Effects.ContainsKey(operation)) return;
            var request = Request(operation, profile, target, proposal?.Revision ?? 0, ContentDigest.Of(snapshot), command.OperationId, command.CorrelationId)
                with { IsCapabilityProbe = true };
            var decision = await authorization.AuthorizeAsync(request, ct).ConfigureAwait(false);
            // An explicit qualified denial is data; malformed/missing/currently unavailable authority is not a grant.
            if (decision is null || decision.RequestDigest != request.Digest || decision.ActorReference != actor.ActorReference ||
                decision.ActorKind != actor.ActorKind || decision.AuditActorType != actor.AuditActorType ||
                new[] { decision.AuthorizationPolicyVersion, decision.EvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
                decision.EffectiveInterval is null || !decision.EffectiveInterval.Contains(clock.GetUtcNow())) throw new ContentUnavailableException();
            if (decision.Allowed && (actor.ActorKind != ContentActorKind.Model || operation is ContentOperation.CreateDraft or ContentOperation.UpdateDraft))
                actions.Add(operation.ToString());
        }
        switch (command.Target)
        {
            case ContentCapabilityTarget.Workspace:
                await Probe(ContentOperation.Browse, command.Scope).ConfigureAwait(false); break;
            case ContentCapabilityTarget.Definition:
                await Probe(ContentOperation.CreateDraft, key + ":" + variant).ConfigureAwait(false);
                await Probe(ContentOperation.History, key + ":" + variant).ConfigureAwait(false); break;
            case ContentCapabilityTarget.Proposal:
                var row = proposal!; var target = row.Id.ToString("N");
                await Probe(ContentOperation.UpdateDraft, target, row.State != "PUBLISHED").ConfigureAwait(false);
                await Probe(ContentOperation.Validate, target, row.State == "DRAFT").ConfigureAwait(false);
                await Probe(ContentOperation.Preview, target).ConfigureAwait(false);
                await Probe(ContentOperation.Approve, target, row.State == "VALIDATED" && row.VersionReference is not null &&
                    (!profile.SeparateAuthorApprover || !Contributors(row.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal))).ConfigureAwait(false);
                await Probe(ContentOperation.Publish, target, row.VersionReference is not null && row.State is "VALIDATED" or "APPROVED" &&
                    (!Definition(profile, row.Key).RequiresApproval || row.State == "APPROVED" && !string.IsNullOrWhiteSpace(row.ApprovalEvidenceReference)) &&
                    EligiblePublisher(profile, row, actor)).ConfigureAwait(false); break;
            case ContentCapabilityTarget.History:
                await Probe(ContentOperation.Rollback, version!.Id.ToString("N"), proposal!.State == "PUBLISHED" && proposal.VersionReference == version.Id &&
                    (!Definition(profile, version.Key).RequiresApproval || !string.IsNullOrWhiteSpace(proposal.ApprovalEvidenceReference)) &&
                    EligiblePublisher(profile, proposal, actor)).ConfigureAwait(false); break;
        }
        await FinishAsync(tx, profile, read, actor, Guid.Empty, 0, read.CandidateDigest, ct).ConfigureAwait(false);
        return new(command.Target, command.Scope, profile.Metadata.VersionId, key, variant, proposal?.Id, proposal?.Revision, command.PublicationReference, actions);
    }

    private static bool EligiblePublisher(ContentGovernanceProfile profile, ContentProposalRow row, ContentAuthorizationDecision actor) =>
        (!profile.SeparateAuthorPublisher || !Contributors(row.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal)) &&
        (!profile.SeparateApproverPublisher || row.ApproverReference != actor.ActorReference);

    private static void RequireCapabilityTarget(ContentCapabilityQuery command)
    {
        var definition = command.Target == ContentCapabilityTarget.Definition;
        var proposal = command.Target == ContentCapabilityTarget.Proposal;
        var history = command.Target == ContentCapabilityTarget.History;
        if (!Enum.IsDefined(command.Target) || string.IsNullOrWhiteSpace(command.Scope) ||
            (definition ? string.IsNullOrWhiteSpace(command.Key) || string.IsNullOrWhiteSpace(command.VariantReference) : command.Key is not null || command.VariantReference is not null) ||
            (proposal ? command.ProposalReference is null || command.ProposalReference == Guid.Empty || command.ExpectedRevision is null or < 1 : command.ProposalReference is not null || command.ExpectedRevision is not null) ||
            (history ? command.PublicationReference is null || command.PublicationReference == Guid.Empty : command.PublicationReference is not null))
            throw new ArgumentException("An exact content capability target is required.");
    }
}
