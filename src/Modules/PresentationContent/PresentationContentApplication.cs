using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Audit;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.PresentationContent;

/// <summary>Content-owned writes only. Identity/configuration authority and actual-caller authorization remain explicit owner inputs.</summary>
public sealed partial class PresentationContentApplication(PresentationContentDbContext database, IPresentationContentProfileSource profiles,
    IPresentationContentAuthorizationSource authorization, TimeProvider clock) : IPresentationContentResolver
{
    public async Task<ContentProposalView> CreateDraftAsync(CreateContentDraft command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var profile = Profile(command.Scope);
        _ = Definition(profile, command.Key); _ = Variant(profile, command.VariantReference); BoundText(command.Text, profile);
        var id = Guid.NewGuid(); var request = Request(ContentOperation.CreateDraft, profile, command.Key + ":" + command.VariantReference, 0, ContentDigest.Of(command), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        await EnsureProfileAsync(profile, ct).ConfigureAwait(false);
        var row = new ContentProposalRow { Id = id, Scope = command.Scope, ProfileVersion = profile.Metadata.VersionId, Key = command.Key,
            VariantReference = command.VariantReference, Text = command.Text, Revision = 1, ContributorsJson = JsonSerializer.Serialize(new[] { actor.ActorReference }),
            SourceEvidenceReference = actor.EvidenceReference, ModelOrigin = actor.ActorKind == ContentActorKind.Model };
        database.Proposals.Add(row);
        await FinishAsync(tx, profile, request, actor, id, row.Revision, ContentDigest.Of(row.Text), ct).ConfigureAwait(false); return View(row);
    }

    public async Task<ContentProposalView> UpdateDraftAsync(ChangeContentDraft command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false);
        var profile = await ProposalProfileAsync(row, ct).ConfigureAwait(false);
        if (row.State == "PUBLISHED") throw new ContentConflictException(); BoundText(command.Text, profile);
        var request = Request(ContentOperation.UpdateDraft, profile, row.Id.ToString("N"), row.Revision, ContentDigest.Of(command), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        row.ContributorsJson = JsonSerializer.Serialize(Contributors(row.ContributorsJson).Append(actor.ActorReference).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        row.ModelOrigin |= actor.ActorKind == ContentActorKind.Model; row.Text = command.Text; row.State = "DRAFT"; row.Revision++;
        row.VersionReference = null; row.ApproverReference = null; row.ApprovalEvidenceReference = null; row.ApprovalEventReference = null;
        await FinishAsync(tx, profile, request, actor, row.Id, row.Revision, ContentDigest.Of(row.Text), ct).ConfigureAwait(false); return View(row);
    }

    public async Task<ContentProposalView> ValidateAsync(ContentProposalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false);
        var profile = await ProposalProfileAsync(row, ct).ConfigureAwait(false); if (row.State != "DRAFT") throw new ContentConflictException();
        var request = Request(ContentOperation.Validate, profile, row.Id.ToString("N"), row.Revision, ContentDigest.Of(new { row.Text, command }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false); ValidateText(row.Text, profile, Definition(profile, row.Key));
        var version = new ContentVersionRow { Id = Guid.NewGuid(), ProposalReference = row.Id, Scope = row.Scope, ProfileVersion = row.ProfileVersion,
            Key = row.Key, VariantReference = row.VariantReference, Locale = Variant(profile, row.VariantReference).Locale, Text = row.Text,
            TextDigest = ContentDigest.Of(row.Text), ContributorsJson = row.ContributorsJson, SourceEvidenceReference = row.SourceEvidenceReference, ModelOrigin = row.ModelOrigin };
        database.Versions.Add(version); row.VersionReference = version.Id; row.State = "VALIDATED"; row.Revision++;
        await FinishAsync(tx, profile, request, actor, version.Id, row.Revision, version.TextDigest, ct, version.Id).ConfigureAwait(false); return View(row);
    }

    public async Task<ContentPreview> PreviewAsync(ContentProposalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false);
        var profile = await ProposalProfileAsync(row, ct).ConfigureAwait(false); var definition = Definition(profile, row.Key);
        var request = Request(ContentOperation.Preview, profile, row.Id.ToString("N"), row.Revision, ContentDigest.Of(command), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false); ValidateText(row.Text, profile, definition);
        await FinishAsync(tx, profile, request, actor, row.Id, row.Revision, ContentDigest.Of(row.Text), ct, row.VersionReference).ConfigureAwait(false);
        return new(View(row), row.Text, definition.ContentClass, definition.Variables.ToArray());
    }

    public async Task<ContentProposalView> ApproveAsync(ContentProposalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false);
        var profile = await ProposalProfileAsync(row, ct).ConfigureAwait(false);
        if (row.State != "VALIDATED" || row.VersionReference is null) throw new ContentConflictException();
        var request = Request(ContentOperation.Approve, profile, row.Id.ToString("N"), row.Revision, ContentDigest.Of(new { row.VersionReference, command }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (profile.SeparateAuthorApprover && Contributors(row.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal)) throw new ContentForbiddenException();
        row.State = "APPROVED"; row.ApproverReference = actor.ActorReference; row.ApprovalEvidenceReference = actor.EvidenceReference; row.Revision++;
        await FinishAsync(tx, profile, request, actor, row.Id, row.Revision, ContentDigest.Of(row.Text), ct, row.VersionReference).ConfigureAwait(false); return View(row);
    }

    public async Task<ContentHistoryEntry> PublishAsync(PublishContent command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false); var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false);
        var profile = await ProposalProfileAsync(row, ct).ConfigureAwait(false); var definition = Definition(profile, row.Key);
        if (row.VersionReference is not { } versionId || row.State is not ("VALIDATED" or "APPROVED") ||
            definition.RequiresApproval && (row.State != "APPROVED" || string.IsNullOrWhiteSpace(row.ApprovalEvidenceReference))) throw new ContentConflictException();
        var version = await database.Versions.SingleAsync(x => x.Id == versionId, ct).ConfigureAwait(false);
        var request = Request(ContentOperation.Publish, profile, row.Id.ToString("N"), row.Revision, ContentDigest.Of(command), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false); RequirePublisher(profile, version, row.ApproverReference, actor);
        var publication = await PublicationAsync(profile, version, row.ApproverReference, row.ApprovalEvidenceReference, row.ApprovalEventReference, actor,
            command.EffectiveInterval, command.SupersedesPublicationReference, ct).ConfigureAwait(false);
        row.State = "PUBLISHED"; row.Revision++;
        await FinishAsync(tx, profile, request, actor, publication.Id, row.Revision, version.TextDigest, ct, version.Id).ConfigureAwait(false);
        return History(publication, version);
    }

    public async Task<ContentHistoryEntry> RollbackAsync(RollbackContent command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken;
        await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var version = await database.Versions.SingleOrDefaultAsync(x => x.Id == command.ApprovedVersionReference, ct).ConfigureAwait(false) ?? throw new ContentConflictException();
        var row = await ProposalAsync(version.ProposalReference, null, ct).ConfigureAwait(false); var profile = await ProposalProfileAsync(row, ct).ConfigureAwait(false);
        if (row.State != "PUBLISHED" || row.VersionReference != version.Id || !await database.Publications.AnyAsync(x => x.VersionReference == version.Id, ct).ConfigureAwait(false) ||
            Definition(profile, row.Key).RequiresApproval && string.IsNullOrWhiteSpace(row.ApprovalEvidenceReference)) throw new ContentConflictException();
        var request = Request(ContentOperation.Rollback, profile, version.Id.ToString("N"), row.Revision, ContentDigest.Of(command), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false); RequirePublisher(profile, version, row.ApproverReference, actor);
        var publication = await PublicationAsync(profile, version, row.ApproverReference, row.ApprovalEvidenceReference, row.ApprovalEventReference, actor,
            command.EffectiveInterval, command.SupersedesPublicationReference, ct).ConfigureAwait(false);
        await FinishAsync(tx, profile, request, actor, publication.Id, row.Revision, version.TextDigest, ct, version.Id).ConfigureAwait(false); return History(publication, version);
    }

    public async Task<IReadOnlyList<ContentHistoryEntry>> HistoryAsync(string scope, string key, string variantReference, string operationId, string correlationId,
        CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false); var profile = Profile(scope);
        _ = Definition(profile, key); _ = Variant(profile, variantReference); await RequireStoredProfileAsync(profile, ct).ConfigureAwait(false);
        var request = Request(ContentOperation.History, profile, key + ":" + variantReference, 0, ContentDigest.Of(new { scope, key, variantReference }), operationId, correlationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var publications = await database.Publications.AsNoTracking().Where(x => x.Scope == scope && x.ProfileVersion == profile.Metadata.VersionId && x.Key == key && x.VariantReference == variantReference).ToArrayAsync(ct).ConfigureAwait(false);
        var versions = await database.Versions.AsNoTracking().Where(x => x.Scope == scope && x.ProfileVersion == profile.Metadata.VersionId && x.Key == key && x.VariantReference == variantReference).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        await FinishAsync(tx, profile, request, actor, Guid.Empty, 0, request.CandidateDigest, ct).ConfigureAwait(false);
        return publications.OrderBy(x => x.From).ThenBy(x => x.Id).Select(x => History(x, versions[x.VersionReference])).ToArray();
    }

    private async Task<ContentPublicationRow> PublicationAsync(ContentGovernanceProfile profile, ContentVersionRow version, string? approver, string? approval, Guid? approvalEvent,
        ContentAuthorizationDecision actor, EffectivePolicyInterval interval, Guid? supersedes, CancellationToken ct)
    {
        RequireInterval(profile, interval); ValidateText(version.Text, profile, Definition(profile, version.Key));
        await LockAsync("publication:" + ContentDigest.Of(new { version.Scope, version.Key, version.VariantReference }), ct).ConfigureAwait(false);
        var prior = await database.Publications.AsNoTracking().Where(x => x.Scope == version.Scope && x.ProfileVersion == version.ProfileVersion && x.Key == version.Key && x.VariantReference == version.VariantReference).ToArrayAsync(ct).ConfigureAwait(false);
        var leaves = prior.Where(x => !prior.Any(y => y.SupersedesPublicationReference == x.Id)).ToArray();
        if (leaves.Length > 1 || (leaves.Length == 0 ? supersedes is not null : supersedes != leaves[0].Id || interval.From <= leaves[0].From)) throw new ContentConflictException();
        var publication = new ContentPublicationRow { Id = Guid.NewGuid(), VersionReference = version.Id, Scope = version.Scope, ProfileVersion = version.ProfileVersion,
            Key = version.Key, VariantReference = version.VariantReference, From = interval.From, IncludesFrom = interval.IncludesFrom, Until = interval.Until,
            IncludesUntil = interval.IncludesUntil, SupersedesPublicationReference = supersedes, PublisherReference = actor.ActorReference,
            ApproverReference = approver, ApprovalEvidenceReference = approval, ApprovalEventReference = approvalEvent, PublisherEvidenceReference = actor.EvidenceReference,
            PublisherAuthorizationVersion = actor.AuthorizationPolicyVersion };
        database.Publications.Add(publication); return publication;
    }

    private async Task<IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new ContentConflictException();
        database.ChangeTracker.Clear(); return await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
    }
    private Task<int> LockAsync(string key, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"presentation-content:" + key},0))", ct);
    private async Task<ContentProposalRow> ProposalAsync(Guid id, long? revision, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ContentConflictException(); await LockAsync("proposal:" + id.ToString("N"), ct).ConfigureAwait(false);
        var row = await database.Proposals.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw new ContentConflictException();
        if (revision is not null && row.Revision != revision) throw new ContentConflictException(); return row;
    }
    private ContentGovernanceProfile Profile(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope); var profile = profiles.RequireEffective(scope, clock.GetUtcNow()); profile.Require(scope, clock.GetUtcNow()); return profile;
    }
    private async Task<ContentGovernanceProfile> ProposalProfileAsync(ContentProposalRow row, CancellationToken ct)
    {
        var profile = Profile(row.Scope); if (profile.Metadata.VersionId != row.ProfileVersion) throw new ContentConflictException();
        await RequireStoredProfileAsync(profile, ct).ConfigureAwait(false); return profile;
    }
    private async Task EnsureProfileAsync(ContentGovernanceProfile profile, CancellationToken ct)
    {
        await LockAsync("profile:" + profile.Metadata.Scope + ":" + profile.Metadata.VersionId, ct).ConfigureAwait(false);
        var stored = await database.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.Scope == profile.Metadata.Scope && x.Version == profile.Metadata.VersionId, ct).ConfigureAwait(false);
        if (stored is not null) { if (stored.Snapshot != profile.Snapshot) throw new ContentConflictException(); return; }
        database.Profiles.Add(new() { Scope = profile.Metadata.Scope, Version = profile.Metadata.VersionId, Snapshot = profile.Snapshot });
        database.Definitions.AddRange(profile.Definitions.Select(x => new ContentDefinitionRow { Scope = profile.Metadata.Scope, ProfileVersion = profile.Metadata.VersionId, Key = x.Key, Snapshot = JsonSerializer.Serialize(x) }));
        database.Variants.AddRange(profile.Variants.Select(x => new ContentVariantRow { Scope = profile.Metadata.Scope, ProfileVersion = profile.Metadata.VersionId, Reference = x.Reference, Snapshot = JsonSerializer.Serialize(x) }));
    }
    private async Task RequireStoredProfileAsync(ContentGovernanceProfile profile, CancellationToken ct)
    {
        var stored = await database.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.Scope == profile.Metadata.Scope && x.Version == profile.Metadata.VersionId, ct).ConfigureAwait(false);
        if (stored?.Snapshot != profile.Snapshot) throw new ContentUnavailableException();
    }
    private static ContentDefinition Definition(ContentGovernanceProfile profile, string key) => profile.Definitions.SingleOrDefault(x => x.Key == key) ?? throw new ContentUnavailableException();
    private static ContentVariant Variant(ContentGovernanceProfile profile, string reference) => profile.Variants.SingleOrDefault(x => x.Reference == reference) ?? throw new ContentUnavailableException();
    private static string[] Contributors(string json) => JsonSerializer.Deserialize<string[]>(json) ?? throw new ContentUnavailableException();
    private static ContentProposalView View(ContentProposalRow row) => new(row.Id, row.Revision, row.State, row.Scope, row.Key, row.VariantReference, row.VersionReference, ContentDigest.Of(row.Text))
    { ModelOrigin = row.ModelOrigin };
    private static ContentHistoryEntry History(ContentPublicationRow row, ContentVersionRow version) => new(row.Id, version.Id, version.Key, version.VariantReference,
        version.Locale, version.Text, version.TextDigest, new(row.From, row.IncludesFrom, row.Until, row.IncludesUntil), row.SupersedesPublicationReference, row.PublisherReference, row.ApproverReference, row.ProfileVersion)
    { ModelOrigin = version.ModelOrigin };
    private static void BoundText(string text, ContentGovernanceProfile profile)
    { if (text is null || text.EnumerateRunes().Count() > profile.MaximumTextLength || text.Contains('\0')) throw new ArgumentException("Content exceeds its declared plain-text bounds.", nameof(text)); }
    private static void ValidateText(string text, ContentGovernanceProfile profile, ContentDefinition definition)
    {
        BoundText(text, profile); if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A content version requires text.", nameof(text));
        var variables = Regex.Matches(text, "\\{([A-Za-z][A-Za-z0-9_.-]*)\\}", RegexOptions.CultureInvariant).Select(x => x.Groups[1].Value).ToArray();
        var remainder = Regex.Replace(text, "\\{([A-Za-z][A-Za-z0-9_.-]*)\\}", "", RegexOptions.CultureInvariant);
        if (remainder.Contains('{') || remainder.Contains('}') || variables.Any(x => !definition.Variables.Contains(x, StringComparer.Ordinal)) ||
            definition.RequiredVariables.Any(x => !variables.Contains(x, StringComparer.Ordinal))) throw new ArgumentException("Content slots disagree with the approved definition.", nameof(text));
    }
    private void RequireInterval(ContentGovernanceProfile profile, EffectivePolicyInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval); var limit = profile.Metadata.EffectiveInterval;
        if (interval.From.Offset != TimeSpan.Zero || interval.From.Ticks % 10 != 0 || interval.Until is { } end && (end.Offset != TimeSpan.Zero || end.Ticks % 10 != 0) ||
            interval.From < clock.GetUtcNow() || !limit.Contains(interval.From) || limit.Until is { } until &&
            (interval.Until is null || interval.Until > until || interval.Until == until && interval.IncludesUntil && !limit.IncludesUntil)) throw new ContentConflictException();
    }
    private static void RequirePublisher(ContentGovernanceProfile profile, ContentVersionRow version, string? approver, ContentAuthorizationDecision actor)
    {
        if (profile.SeparateAuthorPublisher && Contributors(version.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal) ||
            profile.SeparateApproverPublisher && approver == actor.ActorReference) throw new ContentForbiddenException();
    }
    private static ContentAuthorizationRequest Request(ContentOperation operation, ContentGovernanceProfile profile, string target, long revision, string digest, string operationId, string correlationId)
    {
        if (!profile.Effects.ContainsKey(operation)) throw new ContentUnavailableException();
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new(operation, profile.Metadata.Scope, target, revision, digest, profile.Metadata.VersionId, operationId, correlationId)
        { ProfileDigest = ContentDigest.Of(profile) };
    }
    private async Task<ContentAuthorizationDecision> AuthorizeAsync(ContentAuthorizationRequest request, CancellationToken ct)
    {
        var decision = await authorization.AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (decision is null || !decision.Allowed || decision.RequestDigest != request.Digest || !Enum.IsDefined(decision.ActorKind) ||
            new[] { decision.ActorReference, decision.AuditActorType, decision.AuthorizationPolicyVersion, decision.EvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
            decision.EffectiveInterval is null || !decision.EffectiveInterval.Contains(clock.GetUtcNow()) ||
            decision.ActorKind == ContentActorKind.Model && request.Operation is not (ContentOperation.CreateDraft or ContentOperation.UpdateDraft)) throw new ContentForbiddenException();
        return decision;
    }
    private async Task FinishAsync(IDbContextTransaction tx, ContentGovernanceProfile profile, ContentAuthorizationRequest request,
        ContentAuthorizationDecision actor, Guid reference, long revision, string digest, CancellationToken ct, Guid? versionReference = null)
    {
        if (Profile(profile.Metadata.Scope).Snapshot != profile.Snapshot || ContentDigest.Of(profile) != request.ProfileDigest) throw new ContentConflictException();
        var final = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (final.ActorReference != actor.ActorReference || final.ActorKind != actor.ActorKind || final.AuditActorType != actor.AuditActorType ||
            final.AuthorizationPolicyVersion != actor.AuthorizationPolicyVersion || final.EvidenceReference != actor.EvidenceReference) throw new ContentForbiddenException();
        if (ContentDigest.Of(profile) != request.ProfileDigest || Profile(profile.Metadata.Scope).Snapshot != profile.Snapshot) throw new ContentConflictException();
        foreach (var entry in database.ChangeTracker.Entries<ContentPublicationRow>().Where(x => x.State == EntityState.Added))
            RequireInterval(profile, new(entry.Entity.From, entry.Entity.IncludesFrom, entry.Entity.Until, entry.Entity.IncludesUntil));
        var contract = profile.Effects[request.Operation]; var eventId = Guid.NewGuid(); var at = clock.GetUtcNow(); var target = reference == Guid.Empty ? request.TargetReference : reference.ToString("N");
        var scheduled = database.ChangeTracker.Entries<ContentPublicationRow>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray();
        foreach (var entry in database.ChangeTracker.Entries<ContentProposalRow>().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.LastEventReference = eventId; entry.Entity.LastTargetReference = reference; entry.Entity.LastOperation = request.Operation.ToString(); entry.Entity.LastActorReference = actor.ActorReference; entry.Entity.LastDigest = digest;
            if (request.Operation == ContentOperation.Approve) entry.Entity.ApprovalEventReference = eventId;
        }
        foreach (var entry in database.ChangeTracker.Entries<ContentVersionRow>().Where(x => x.State == EntityState.Added)) entry.Entity.ValidationEventReference = eventId;
        foreach (var row in scheduled) { row.PublicationEventReference = eventId; row.Operation = request.Operation.ToString(); }
        var audit = new AuditEvent(eventId, at, new(actor.ActorReference, actor.AuditActorType, [actor.EvidenceReference]), contract.AuditAction,
            request.OperationId, request.CorrelationId, [new(target, revision.ToString(CultureInfo.InvariantCulture))], contract.AuditResult, profile.Classification,
            profile.AuditRetentionClassReference, policyVersionReferences: [profile.Metadata.VersionId, actor.AuthorizationPolicyVersion],
            evidenceReferences: profile.Metadata.ApprovalEvidenceReferences.Concat([profile.EvidenceReference, actor.EvidenceReference]));
        EfAuditAppender.StageAppend(database, audit);
        if (contract.EventType is not null)
            database.Set<OutboxMessage>().Add(new(eventId, contract.EnvelopeVersion, contract.EventType, contract.EventSchemaVersion!, at, contract.Source,
                target, profile.Classification, JsonSerializer.Serialize(new { reference = target, scope = profile.Metadata.Scope, profileVersion = profile.Metadata.VersionId,
                    revision, contentDigest = digest, authorizationEvidenceReference = actor.EvidenceReference,
                    versionReference = versionReference?.ToString("N") }), revision.ToString(CultureInfo.InvariantCulture), request.OperationId, request.CorrelationId));
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        // Saving/SavedChanges hooks and the storage await may outlive the approval or scheduled effective frontier.
        // Re-read actual authority after the final write; any failure rolls back rows, audit and outbox together.
        var committedActor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (committedActor.ActorReference != actor.ActorReference || committedActor.ActorKind != actor.ActorKind || committedActor.AuditActorType != actor.AuditActorType ||
            committedActor.AuthorizationPolicyVersion != actor.AuthorizationPolicyVersion || committedActor.EvidenceReference != actor.EvidenceReference) throw new ContentForbiddenException();
        if (Profile(profile.Metadata.Scope).Snapshot != profile.Snapshot || ContentDigest.Of(profile) != request.ProfileDigest) throw new ContentConflictException();
        foreach (var row in scheduled) RequireInterval(profile, new(row.From, row.IncludesFrom, row.Until, row.IncludesUntil));
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
