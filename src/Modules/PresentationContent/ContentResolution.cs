using System.Collections.ObjectModel;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Tunner.Platform.Policies;

namespace Tunner.PresentationContent;

public sealed partial class PresentationContentApplication
{
    public ContentResolution Resolve(ContentResolutionRequest request)
    {
        try { return ResolveStored(request); }
        catch (Exception error) when (error is DbException or TimeoutException) { throw new ContentUnavailableException(); }
    }
    private ContentResolution ResolveStored(ContentResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); var profile = ResolutionProfile(request);
        var stored = database.Profiles.AsNoTracking().SingleOrDefault(x => x.Scope == request.Scope && x.Version == profile.Metadata.VersionId);
        if (stored?.Snapshot != profile.Snapshot) throw new ContentUnavailableException();
        var publications = database.Publications.AsNoTracking().Where(x => x.Scope == request.Scope && x.ProfileVersion == profile.Metadata.VersionId).ToArray();
        if (database.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM presentation_content.publications WHERE \"Scope\"={request.Scope} AND \"ProfileVersion\"={profile.Metadata.VersionId} AND NOT presentation_content.valid_publication(\"Id\")").Single() != 0)
            throw new ContentUnavailableException();
        var versions = database.Versions.AsNoTracking().Where(x => x.Scope == request.Scope && x.ProfileVersion == profile.Metadata.VersionId).ToArray();
        return ResolveBundle(request, profile, publications, versions);
    }

    public async Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
    {
        try { return await ResolveStoredAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is DbException or TimeoutException)
        { cancellationToken.ThrowIfCancellationRequested(); throw new ContentUnavailableException(); }
    }
    private async Task<ContentResolution> ResolveStoredAsync(ContentResolutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); var profile = ResolutionProfile(request); var ct = cancellationToken;
        await RequireStoredProfileAsync(profile, ct).ConfigureAwait(false);
        var publications = await database.Publications.AsNoTracking().Where(x => x.Scope == request.Scope && x.ProfileVersion == profile.Metadata.VersionId).ToArrayAsync(ct).ConfigureAwait(false);
        if (await database.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM presentation_content.publications WHERE \"Scope\"={request.Scope} AND \"ProfileVersion\"={profile.Metadata.VersionId} AND NOT presentation_content.valid_publication(\"Id\")").SingleAsync(ct).ConfigureAwait(false) != 0)
            throw new ContentUnavailableException();
        var versions = await database.Versions.AsNoTracking().Where(x => x.Scope == request.Scope && x.ProfileVersion == profile.Metadata.VersionId).ToArrayAsync(ct).ConfigureAwait(false);
        return ResolveBundle(request, profile, publications, versions);
    }

    private ContentGovernanceProfile ResolutionProfile(ContentResolutionRequest request)
    {
        var profile = profiles.RequireEffective(request.Scope, request.EffectiveAt); profile.Require(request.Scope, request.EffectiveAt);
        _ = Variant(profile, request.VariantReference); return profile;
    }
    private static EffectivePolicyInterval SelectionAvailability(ContentResolutionRequest request, ContentGovernanceProfile profile,
        ContentPublicationRow[] publications, IEnumerable<PublishedContent> content, EffectivePolicyInterval? readQualificationInterval = null)
    {
        var selected = content.Select(text => publications.Single(row => row.Id.ToString("N") == text.PublicationReference));
        var intervals = selected.Select(row => new EffectivePolicyInterval(row.From, row.IncludesFrom, row.Until, row.IncludesUntil))
            .Append(profile.Metadata.EffectiveInterval).ToList();
        if (readQualificationInterval is not null) intervals.Add(readQualificationInterval);
        var from = intervals.Max(interval => interval.From); var includesFrom = intervals.Where(interval => interval.From == from).All(interval => interval.IncludesFrom);
        DateTimeOffset? until = null; var includesUntil = false;
        void Clip(DateTimeOffset? end, bool included)
        {
            if (end is null) return;
            if (until is null || end < until) { until = end; includesUntil = included; }
            else if (end == until) includesUntil &= included;
        }
        foreach (var interval in intervals) Clip(interval.Until, interval.IncludesUntil);
        var variants = profile.Fallbacks.Single(fallback => fallback.RequestedVariantReference == request.VariantReference).OrderedVariantReferences.ToHashSet(StringComparer.Ordinal);
        var keys = profile.Definitions.Select(definition => definition.Key).ToHashSet(StringComparer.Ordinal);
        var next = publications.Where(row => variants.Contains(row.VariantReference) && keys.Contains(row.Key) &&
            (row.From > request.EffectiveAt || row.From == request.EffectiveAt && !row.IncludesFrom)).OrderBy(row => row.From).ThenByDescending(row => row.IncludesFrom).FirstOrDefault();
        if (next is not null) Clip(next.From, !next.IncludesFrom);
        return new(from, includesFrom, until, includesUntil);
    }

    private static ContentResolution ResolveBundle(ContentResolutionRequest request, ContentGovernanceProfile profile,
        ContentPublicationRow[] publications, ContentVersionRow[] versions)
    {
        var chain = profile.Fallbacks.SingleOrDefault(x => x.RequestedVariantReference == request.VariantReference)?.OrderedVariantReferences
            ?? throw new ContentUnavailableException();
        for (var index = 0; index < chain.Count; index++)
        {
            var variant = Variant(profile, chain[index]); var values = new SortedDictionary<string, PublishedContent>(StringComparer.Ordinal); var missing = false;
            foreach (var definition in profile.Definitions)
            {
                if (index > 0 && !definition.AllowFallback)
                { if (definition.Mandatory) throw new ContentUnavailableException(); continue; }
                var sameKey = publications.Where(x => x.Key == definition.Key && x.VariantReference == variant.Reference).ToArray();
                // Supersession is explicit and permanent from the successor's effective start: an expired successor cannot resurrect old wording.
                var available = sameKey.Where(x => !sameKey.Any(y => y.SupersedesPublicationReference == x.Id &&
                    (request.EffectiveAt > y.From || request.EffectiveAt == y.From && y.IncludesFrom))).ToArray();
                var candidates = available.Select(x => new PolicyVersionCandidate<string>(x.Id.ToString("N"), "PUBLISHED", definition.Key,
                    new(x.From, x.IncludesFrom, x.Until, x.IncludesUntil), x.SupersedesPublicationReference?.ToString("N"),
                    [x.ApprovalEvidenceReference ?? x.PublisherEvidenceReference]));
                var selected = EffectivePolicyVersionResolver.Resolve(request.EffectiveAt, definition.Key, candidates,
                    static (actual, expected) => actual == expected, static candidate => candidate.Status == "PUBLISHED");
                if (selected.Outcome == PolicyVersionResolutionOutcome.Ambiguous) throw new ContentUnavailableException();
                if (selected.SelectedVersion is null) { missing |= definition.Mandatory; continue; }
                var publication = available.Single(x => x.Id.ToString("N") == selected.SelectedVersion.VersionId);
                var version = versions.SingleOrDefault(x => x.Id == publication.VersionReference);
                if (version is null || version.Scope != request.Scope || version.Key != definition.Key || version.VariantReference != variant.Reference || version.Locale != variant.Locale ||
                    version.TextDigest != ContentDigest.Of(version.Text) || string.IsNullOrWhiteSpace(publication.PublisherEvidenceReference) ||
                    definition.RequiresApproval && (string.IsNullOrWhiteSpace(publication.ApproverReference) || string.IsNullOrWhiteSpace(publication.ApprovalEvidenceReference)))
                    throw new ContentUnavailableException();
                ValidateText(version.Text, profile, definition);
                values.Add(definition.Key, new(definition.Key, variant.Reference, version.Id.ToString("N"), publication.Id.ToString("N"), variant.Locale, version.Text));
            }
            if (!missing) return new(request.Scope, request.VariantReference, variant.Reference, variant.Locale,
                new ReadOnlyDictionary<string, PublishedContent>(values))
            { EffectiveInterval = SelectionAvailability(request, profile, publications, values.Values) };
        }
        throw new ContentUnavailableException();
    }
}
