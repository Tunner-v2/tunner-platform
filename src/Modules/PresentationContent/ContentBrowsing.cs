using Microsoft.EntityFrameworkCore;
using Tunner.Platform.Policies;

namespace Tunner.PresentationContent;

/// <summary>Explicit qualified read capacity; no production pagination or search limits are selected.</summary>
public sealed record ContentBrowseBudget(PolicyVersionCandidate<string> Metadata, int MaximumPageSize, int MaximumOffset, int MaximumSearchLength)
{
    internal void Require(string scope, DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != scope || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) ||
            MaximumPageSize < 1 || MaximumPageSize == int.MaxValue || MaximumOffset < 0 || MaximumSearchLength < 0) throw new ContentUnavailableException();
    }
}
public interface IContentBrowseBudgetSource { ContentBrowseBudget RequireEffective(string scope, DateTimeOffset at); }
public sealed record ContentPage<T>(IReadOnlyList<T> Items, int? NextOffset);
public sealed record ContentWorkspaceInfo(string Scope, string Surface, string ProfileVersion, int MaximumTextLength,
    int MaximumPageSize, int MaximumOffset, int MaximumSearchLength, IReadOnlyList<ContentVariant> Variants,
    bool SeparateAuthorApprover, bool SeparateAuthorPublisher, bool SeparateApproverPublisher);
public sealed record BrowseContentDefinitions(string Scope, string? Search, int PageSize, int Offset, string OperationId, string CorrelationId);
public sealed record SearchContentProposals(string Scope, string? Key, string? VariantReference, string? Search, int PageSize, int Offset,
    string OperationId, string CorrelationId);
public sealed record BrowseContentHistory(string Scope, string Key, string VariantReference, int PageSize, int Offset, string OperationId, string CorrelationId);
public sealed record CompareContentVersion(Guid ProposalReference, long ExpectedRevision, Guid ComparedVersionReference, string OperationId, string CorrelationId);
public sealed record ContentVersionComparison(ContentProposalView Proposal, Guid ComparedVersionReference, string BeforeText, string AfterText,
    string BeforeDigest, string AfterDigest, bool Changed);

public sealed partial class PresentationContentApplication
{
    public async Task<ContentWorkspaceInfo> WorkspaceAsync(string scope, IContentBrowseBudgetSource budgets, string operationId, string correlationId,
        CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false); var profile = Profile(scope);
        var budget = BrowseBudget(budgets, scope); await CheckBrowseProfileAsync(profile, ct).ConfigureAwait(false);
        var request = Request(ContentOperation.Browse, profile, scope, 0, ContentDigest.Of(new { scope, budget }), operationId, correlationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        await FinishAsync(tx, profile, request, actor, Guid.Empty, 0, request.CandidateDigest, ct).ConfigureAwait(false);
        RequireBrowseBudget(budgets, scope, budget);
        return new(scope, profile.Surface, profile.Metadata.VersionId, profile.MaximumTextLength, budget.MaximumPageSize, budget.MaximumOffset,
            budget.MaximumSearchLength, profile.Variants.ToArray(), profile.SeparateAuthorApprover, profile.SeparateAuthorPublisher, profile.SeparateApproverPublisher);
    }

    public async Task<ContentPage<ContentDefinition>> DefinitionsAsync(BrowseContentDefinitions command, IContentBrowseBudgetSource budgets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var profile = Profile(command.Scope); var budget = BrowseBudget(budgets, command.Scope);
        BrowseBounds(budget, command.Search, command.PageSize, command.Offset); await CheckBrowseProfileAsync(profile, ct).ConfigureAwait(false);
        var request = Request(ContentOperation.Browse, profile, command.Scope, 0, ContentDigest.Of(new { command, budget }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var selected = profile.Definitions.Where(x => command.Search is null || x.Key.Contains(command.Search, StringComparison.OrdinalIgnoreCase) ||
            x.Purpose.Contains(command.Search, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Skip(command.Offset).Take(command.PageSize + 1).ToArray();
        await FinishAsync(tx, profile, request, actor, Guid.Empty, 0, request.CandidateDigest, ct).ConfigureAwait(false); RequireBrowseBudget(budgets, command.Scope, budget);
        return Page(selected, command.PageSize, command.Offset, budget);
    }

    public async Task<ContentPage<ContentProposalView>> SearchProposalsAsync(SearchContentProposals command, IContentBrowseBudgetSource budgets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var profile = Profile(command.Scope); var budget = BrowseBudget(budgets, command.Scope);
        BrowseBounds(budget, command.Search, command.PageSize, command.Offset); await CheckBrowseProfileAsync(profile, ct).ConfigureAwait(false);
        if (command.Key is not null) _ = Definition(profile, command.Key);
        if (command.VariantReference is not null) _ = Variant(profile, command.VariantReference);
        var request = Request(ContentOperation.Browse, profile, command.Scope, 0, ContentDigest.Of(new { command, budget }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var query = database.Proposals.AsNoTracking().Where(x => x.Scope == command.Scope && x.ProfileVersion == profile.Metadata.VersionId);
        if (command.Key is not null) query = query.Where(x => x.Key == command.Key);
        if (command.VariantReference is not null) query = query.Where(x => x.VariantReference == command.VariantReference);
        if (command.Search is not null) query = query.Where(x => x.Key.Contains(command.Search) || x.Text.Contains(command.Search));
        var rows = await query.OrderBy(x => x.Key).ThenBy(x => x.VariantReference).ThenBy(x => x.Id).Skip(command.Offset).Take(command.PageSize + 1).ToArrayAsync(ct).ConfigureAwait(false);
        await FinishAsync(tx, profile, request, actor, Guid.Empty, 0, request.CandidateDigest, ct).ConfigureAwait(false); RequireBrowseBudget(budgets, command.Scope, budget);
        return Page(rows.Select(View).ToArray(), command.PageSize, command.Offset, budget);
    }

    public async Task<ContentPage<ContentHistoryEntry>> BrowseHistoryAsync(BrowseContentHistory command, IContentBrowseBudgetSource budgets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var profile = Profile(command.Scope); var budget = BrowseBudget(budgets, command.Scope);
        BrowseBounds(budget, null, command.PageSize, command.Offset); _ = Definition(profile, command.Key); _ = Variant(profile, command.VariantReference);
        await CheckBrowseProfileAsync(profile, ct).ConfigureAwait(false);
        var request = Request(ContentOperation.History, profile, command.Key + ":" + command.VariantReference, 0,
            ContentDigest.Of(new { command, budget }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var publications = await database.Publications.AsNoTracking().Where(x => x.Scope == command.Scope && x.ProfileVersion == profile.Metadata.VersionId &&
            x.Key == command.Key && x.VariantReference == command.VariantReference).OrderByDescending(x => x.From).ThenBy(x => x.Id)
            .Skip(command.Offset).Take(command.PageSize + 1).ToArrayAsync(ct).ConfigureAwait(false);
        var ids = publications.Select(x => x.VersionReference).ToArray();
        var versions = await database.Versions.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var result = publications.Select(x => History(x, versions[x.VersionReference])).ToArray();
        await FinishAsync(tx, profile, request, actor, Guid.Empty, 0, request.CandidateDigest, ct).ConfigureAwait(false); RequireBrowseBudget(budgets, command.Scope, budget);
        return Page(result, command.PageSize, command.Offset, budget);
    }

    public async Task<ContentVersionComparison> CompareAsync(CompareContentVersion command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var proposal = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false);
        var profile = await ProposalProfileAsync(proposal, ct).ConfigureAwait(false);
        var version = await database.Versions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.ComparedVersionReference, ct).ConfigureAwait(false);
        if (version is null || version.Scope != proposal.Scope || version.ProfileVersion != proposal.ProfileVersion || version.Key != proposal.Key ||
            version.VariantReference != proposal.VariantReference) throw new ContentConflictException();
        var request = Request(ContentOperation.Preview, profile, proposal.Id.ToString("N"), proposal.Revision,
            ContentDigest.Of(new { command, version.TextDigest, proposal.Text }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        await FinishAsync(tx, profile, request, actor, proposal.Id, proposal.Revision, ContentDigest.Of(proposal.Text), ct, proposal.VersionReference).ConfigureAwait(false);
        return new(View(proposal), version.Id, version.Text, proposal.Text, version.TextDigest, ContentDigest.Of(proposal.Text), version.Text != proposal.Text);
    }

    private ContentBrowseBudget BrowseBudget(IContentBrowseBudgetSource budgets, string scope)
    {
        ArgumentNullException.ThrowIfNull(budgets); var budget = budgets.RequireEffective(scope, clock.GetUtcNow());
        if (budget is null) throw new ContentUnavailableException(); budget.Require(scope, clock.GetUtcNow()); return budget;
    }
    private void RequireBrowseBudget(IContentBrowseBudgetSource budgets, string scope, ContentBrowseBudget original)
    { if (ContentDigest.Of(BrowseBudget(budgets, scope)) != ContentDigest.Of(original)) throw new ContentUnavailableException(); }
    private async Task CheckBrowseProfileAsync(ContentGovernanceProfile profile, CancellationToken ct)
    {
        var stored = await database.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.Scope == profile.Metadata.Scope && x.Version == profile.Metadata.VersionId, ct).ConfigureAwait(false);
        if (stored is not null && stored.Snapshot != profile.Snapshot) throw new ContentUnavailableException();
    }
    private static void BrowseBounds(ContentBrowseBudget budget, string? search, int pageSize, int offset)
    {
        if (pageSize < 1 || pageSize > budget.MaximumPageSize || offset < 0 || offset > budget.MaximumOffset ||
            search is not null && (search.Length > budget.MaximumSearchLength || search.Any(char.IsControl))) throw new ArgumentException("The content query exceeds its qualified bounds.");
    }
    private static ContentPage<T> Page<T>(T[] items, int pageSize, int offset, ContentBrowseBudget budget) =>
        new(items.Take(pageSize).ToArray(), items.Length > pageSize && (long)offset + pageSize <= budget.MaximumOffset ? offset + pageSize : null);
}
