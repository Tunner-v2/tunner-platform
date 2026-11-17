using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

public sealed record IdentityConfigurationBrowseBudget(PolicyVersionCandidate<IdentityConfigurationScope> Metadata,
    int MaximumPageSize, int MaximumOffset, int MaximumSearchLength)
{
    internal void Require(IdentityConfigurationScope scope, DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != scope || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) ||
            MaximumPageSize < 1 || MaximumPageSize == int.MaxValue || MaximumOffset < 0 || MaximumSearchLength < 0) throw new IdentityConfigurationUnavailableException();
    }
}
public interface IIdentityConfigurationBrowseBudgetSource
{ IdentityConfigurationBrowseBudget RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt); }
public sealed record IdentityConfigurationPage<T>(IReadOnlyList<T> Items, int? NextOffset);
public sealed record IdentityConfigurationWorkspace(IdentityConfigurationScope Scope, string GovernanceVersion, int MaximumPayloadBytes,
    int MaximumPageSize, int MaximumOffset, int MaximumSearchLength, bool SeparateAuthorApprover, bool SeparateAuthorPublisher, bool SeparateApproverPublisher);
public sealed record SearchIdentityConfiguration(IdentityConfigurationScope Scope, string? Search, int PageSize, int Offset, string OperationId, string CorrelationId);
public sealed record BrowseIdentityConfigurationHistory(IdentityConfigurationScope Scope, int PageSize, int Offset, string OperationId, string CorrelationId);
public sealed record IdentityConfigurationHistoricalPreview(IdentityConfigurationHistory Publication, AuthenticationPolicyVersion? Authentication,
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionActionContentBinding? SessionRevocationContent { get; init; }
}
public enum IdentityConfigurationCapabilityTarget { Workspace, Proposal, History }
public sealed record IdentityConfigurationCapabilityQuery(IdentityConfigurationScope Scope, IdentityConfigurationCapabilityTarget Target,
    Guid? ProposalReference, long? ExpectedRevision, Guid? PublicationReference, string OperationId, string CorrelationId);
public sealed record IdentityConfigurationCapabilities(IdentityConfigurationScope Scope, string GovernanceVersion, IdentityConfigurationCapabilityTarget Target,
    Guid? ProposalReference, long? ExpectedRevision, Guid? PublicationReference, IReadOnlyList<string> AvailableActions);

public sealed partial class IdentityRuntimeConfigurationApplication
{
    public async Task<IdentityConfigurationHistoricalPreview> HistoryPreviewAsync(IdentityConfigurationScope scope, Guid publicationReference,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
    {
        if (publicationReference == Guid.Empty) throw new ArgumentException("An exact publication is required.");
        var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false); var authority = Governance(scope);
        var publication = await database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == publicationReference, ct).ConfigureAwait(false);
        if (publication is null || publication.ScopeKey != scope.Key) throw new IdentityConfigurationConflictException();
        var version = await database.Set<IdentityConfigurationVersionRow>().AsNoTracking().SingleAsync(x => x.Id == publication.VersionReference, ct).ConfigureAwait(false);
        var value = IdentityConfigurationDocument.Read(version.Payload);
        if (value.Scope != scope || value.Digest != version.Digest || value.Version != version.PolicyVersionReference) throw new IdentityConfigurationConflictException();
        var request = Request(IdentityConfigurationOperation.History, scope, authority, publication.Id.ToString("N"), 0,
            IdentityConfigurationDigest.Of(new { publicationReference = publication.Id, versionReference = version.Id, version.Digest }), operationId, correlationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        await FinishAsync(tx, authority, request, actor, Guid.Empty, 0, request.CandidateDigest, null, null, null, ct).ConfigureAwait(false);
        return new(History(publication, version, scope), value.Authentication, value.Assurance, value.ContentBinding) { Registration = value.Registration, Totp = value.Totp, RegistrationContent = value.RegistrationContent, Passkey = value.Passkey, TotpContent = value.TotpContent, PasskeyContent = value.PasskeyContent, StepUpContent = value.StepUpContent, RecoveryCodes = value.RecoveryCodes, SessionListing = value.SessionListing, SessionListingContent = value.SessionListingContent, SessionRevocationContent = value.SessionRevocationContent };
    }
    public async Task<IdentityConfigurationWorkspace> WorkspaceAsync(IdentityConfigurationScope scope, IIdentityConfigurationBrowseBudgetSource budgets,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var authority = Governance(scope); var budget = BrowseBudget(budgets, scope);
        var request = Request(IdentityConfigurationOperation.Browse, scope, authority, scope.Key, 0, IdentityConfigurationDigest.Of(new { scope, budget }), operationId, correlationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        await FinishAsync(tx, authority, request, actor, Guid.Empty, 0, request.CandidateDigest, null, null, null, ct).ConfigureAwait(false); RequireBrowseBudget(budgets, scope, budget);
        return new(scope, authority.Metadata.VersionId, authority.MaximumPayloadBytes, budget.MaximumPageSize, budget.MaximumOffset, budget.MaximumSearchLength,
            authority.SeparateAuthorApprover, authority.SeparateAuthorPublisher, authority.SeparateApproverPublisher);
    }
    public async Task<IdentityConfigurationPage<IdentityConfigurationProposal>> SearchAsync(SearchIdentityConfiguration command, IIdentityConfigurationBrowseBudgetSource budgets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var authority = Governance(command.Scope); var budget = BrowseBudget(budgets, command.Scope); BrowseBounds(budget, command.Search, command.PageSize, command.Offset);
        var request = Request(IdentityConfigurationOperation.Browse, command.Scope, authority, command.Scope.Key, 0, IdentityConfigurationDigest.Of(new { command, budget }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var query = database.Set<IdentityConfigurationProposalRow>().AsNoTracking().Where(x => x.ScopeKey == command.Scope.Key && x.GovernanceVersion == authority.Metadata.VersionId);
        if (command.Search is not null) query = query.Where(x => x.PolicyVersionReference.Contains(command.Search));
        var rows = await query.OrderBy(x => x.PolicyVersionReference).ThenBy(x => x.Id).Skip(command.Offset).Take(command.PageSize + 1).ToArrayAsync(ct).ConfigureAwait(false);
        await FinishAsync(tx, authority, request, actor, Guid.Empty, 0, request.CandidateDigest, null, null, null, ct).ConfigureAwait(false); RequireBrowseBudget(budgets, command.Scope, budget);
        return Page(rows.Select(View).ToArray(), command.PageSize, command.Offset, budget);
    }
    public async Task<IdentityConfigurationPage<IdentityConfigurationHistory>> BrowseHistoryAsync(BrowseIdentityConfigurationHistory command, IIdentityConfigurationBrowseBudgetSource budgets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var authority = Governance(command.Scope); var budget = BrowseBudget(budgets, command.Scope); BrowseBounds(budget, null, command.PageSize, command.Offset);
        var request = Request(IdentityConfigurationOperation.History, command.Scope, authority, command.Scope.Key, 0, IdentityConfigurationDigest.Of(new { command, budget }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var rows = await database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().Where(x => x.ScopeKey == command.Scope.Key)
            .OrderByDescending(x => x.EffectiveFrom).ThenBy(x => x.Id).Skip(command.Offset).Take(command.PageSize + 1).ToArrayAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(x => x.VersionReference).ToArray();
        var versions = await database.Set<IdentityConfigurationVersionRow>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        await FinishAsync(tx, authority, request, actor, Guid.Empty, 0, request.CandidateDigest, null, null, null, ct).ConfigureAwait(false); RequireBrowseBudget(budgets, command.Scope, budget);
        return Page(rows.Select(x => History(x, versions[x.VersionReference], command.Scope)).ToArray(), command.PageSize, command.Offset, budget);
    }
    public async Task<IdentityConfigurationCapabilities> CapabilitiesAsync(IdentityConfigurationCapabilityQuery command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var proposalTarget = command.Target == IdentityConfigurationCapabilityTarget.Proposal;
        var historyTarget = command.Target == IdentityConfigurationCapabilityTarget.History;
        if (!Enum.IsDefined(command.Target) || (proposalTarget ? command.ProposalReference is null || command.ProposalReference == Guid.Empty || command.ExpectedRevision is null or < 1 : command.ProposalReference is not null || command.ExpectedRevision is not null) ||
            (historyTarget ? command.PublicationReference is null || command.PublicationReference == Guid.Empty : command.PublicationReference is not null)) throw new ArgumentException("An exact configuration capability target is required.");
        var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false); var authority = Governance(command.Scope);
        IdentityConfigurationProposalRow? row = null; IdentityConfigurationVersionRow? version = null;
        if (proposalTarget) row = await ProposalAsync(command.ProposalReference!.Value, command.ExpectedRevision, ct).ConfigureAwait(false);
        if (historyTarget)
        {
            var publication = await database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.PublicationReference, ct).ConfigureAwait(false);
            if (publication is null || publication.ScopeKey != command.Scope.Key) throw new IdentityConfigurationConflictException();
            version = await database.Set<IdentityConfigurationVersionRow>().AsNoTracking().SingleAsync(x => x.Id == publication.VersionReference, ct).ConfigureAwait(false);
            row = await ProposalAsync(version.ProposalReference, null, ct).ConfigureAwait(false);
        }
        if (row is not null)
        {
            if (row.ScopeKey != command.Scope.Key) throw new IdentityConfigurationConflictException();
            authority = await ProposalGovernanceAsync(row, IdentityConfigurationDocument.Read(row.Payload), ct).ConfigureAwait(false);
        }
        var digest = IdentityConfigurationDigest.Of(new { command.Target, command.Scope, proposal = row?.Id, revision = row?.Revision, state = row?.State,
            candidateDigest = row?.Digest, version = row?.VersionReference, authors = row?.ContributorsJson, approver = row?.ApproverReference, command.PublicationReference });
        var target = row?.Id.ToString("N") ?? command.Scope.Key;
        var read = Request(IdentityConfigurationOperation.Browse, command.Scope, authority, target, row?.Revision ?? 0, digest, command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(read, ct).ConfigureAwait(false); var actions = new List<string>();
        async Task Probe(IdentityConfigurationOperation operation, string reference, bool eligible = true)
        {
            if (!eligible || !authority.Effects.ContainsKey(operation)) return;
            var request = Request(operation, command.Scope, authority, reference, row?.Revision ?? 0, digest, command.OperationId, command.CorrelationId) with { IsCapabilityProbe = true };
            var decision = await authorization.AuthorizeAsync(request, ct).ConfigureAwait(false);
            if (decision is null || decision.RequestDigest != request.Digest || decision.ActorReference != actor.ActorReference || decision.ActorKind != actor.ActorKind ||
                decision.AuditActorType != actor.AuditActorType || new[] { decision.AuthorizationPolicyVersion, decision.EvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
                decision.EffectiveInterval is null || !decision.EffectiveInterval.Contains(clock.GetUtcNow())) throw new IdentityConfigurationUnavailableException();
            if (decision.Allowed && (actor.ActorKind != IdentityConfigurationActorKind.Model || operation is IdentityConfigurationOperation.CreateDraft or IdentityConfigurationOperation.UpdateDraft)) actions.Add(operation.ToString());
        }
        if (command.Target == IdentityConfigurationCapabilityTarget.Workspace)
        {
            await Probe(IdentityConfigurationOperation.Browse, target).ConfigureAwait(false);
            await Probe(IdentityConfigurationOperation.CreateDraft, target).ConfigureAwait(false);
            await Probe(IdentityConfigurationOperation.History, target).ConfigureAwait(false);
        }
        else if (proposalTarget)
        {
            await Probe(IdentityConfigurationOperation.UpdateDraft, target, row!.State != "PUBLISHED").ConfigureAwait(false);
            await Probe(IdentityConfigurationOperation.Validate, target, row.State == "DRAFT").ConfigureAwait(false);
            await Probe(IdentityConfigurationOperation.Preview, target).ConfigureAwait(false);
            await Probe(IdentityConfigurationOperation.Approve, target, row.State == "VALIDATED" && row.VersionReference is not null &&
                (!authority.SeparateAuthorApprover || !Authors(row.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal))).ConfigureAwait(false);
            await Probe(IdentityConfigurationOperation.Publish, target, row.State == "APPROVED" && row.VersionReference is not null && EligiblePublisher(authority, row, actor)).ConfigureAwait(false);
        }
        else await Probe(IdentityConfigurationOperation.Rollback, version!.Id.ToString("N"), row!.State == "PUBLISHED" && row.VersionReference == version.Id && EligiblePublisher(authority, row, actor)).ConfigureAwait(false);
        await FinishAsync(tx, authority, read, actor, Guid.Empty, 0, digest, null, null, null, ct).ConfigureAwait(false);
        return new(command.Scope, authority.Metadata.VersionId, command.Target, row?.Id, row?.Revision, command.PublicationReference, actions);
    }
    private static bool EligiblePublisher(IdentityConfigurationGovernance authority, IdentityConfigurationProposalRow row, IdentityConfigurationAuthorizationDecision actor) =>
        (!authority.SeparateAuthorPublisher || !Authors(row.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal)) &&
        (!authority.SeparateApproverPublisher || row.ApproverReference != actor.ActorReference);
    private IdentityConfigurationBrowseBudget BrowseBudget(IIdentityConfigurationBrowseBudgetSource budgets, IdentityConfigurationScope scope)
    { ArgumentNullException.ThrowIfNull(budgets); var budget = budgets.RequireEffective(scope, clock.GetUtcNow()) ?? throw new IdentityConfigurationUnavailableException(); budget.Require(scope, clock.GetUtcNow()); return budget; }
    private void RequireBrowseBudget(IIdentityConfigurationBrowseBudgetSource budgets, IdentityConfigurationScope scope, IdentityConfigurationBrowseBudget original)
    { if (IdentityConfigurationDigest.Of(original) != IdentityConfigurationDigest.Of(BrowseBudget(budgets, scope))) throw new IdentityConfigurationUnavailableException(); }
    private static void BrowseBounds(IdentityConfigurationBrowseBudget budget, string? search, int pageSize, int offset)
    {
        if (pageSize < 1 || pageSize > budget.MaximumPageSize || offset < 0 || offset > budget.MaximumOffset ||
            search is not null && (search.Length > budget.MaximumSearchLength || search.Any(char.IsControl))) throw new ArgumentException("The configuration query exceeds its qualified bounds.");
    }
    private static IdentityConfigurationPage<T> Page<T>(T[] items, int pageSize, int offset, IdentityConfigurationBrowseBudget budget) =>
        new(items.Take(pageSize).ToArray(), items.Length > pageSize && (long)offset + pageSize <= budget.MaximumOffset ? offset + pageSize : null);
}
