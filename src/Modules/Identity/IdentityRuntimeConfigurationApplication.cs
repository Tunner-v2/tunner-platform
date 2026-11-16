using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Database.Audit;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Identity-owned security configuration. Typed candidates are proposals, not publication or approval authority.</summary>
public sealed partial class IdentityRuntimeConfigurationApplication(IdentityPersistenceDbContext database, IIdentityConfigurationGovernanceSource governance,
    IIdentityConfigurationAuthorizationSource authorization, TimeProvider clock)
{
    public Task<IdentityConfigurationProposal> CreateAuthenticationDraftAsync(AuthenticationPolicyVersion candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Authentication: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateAssuranceDraftAsync(AssurancePolicyVersion candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Assurance: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateContentBindingDraftAsync(AuthenticationContentBinding candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(ContentBinding: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateRegistrationDraftAsync(RegistrationPolicyVersion candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Registration: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateRegistrationContentDraftAsync(RegistrationContentBinding candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(RegistrationContent: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateTotpContentDraftAsync(TotpContentBinding candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(TotpContent: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreatePasskeyContentDraftAsync(PasskeyContentBinding candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(PasskeyContent: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateStepUpContentDraftAsync(StepUpContentBinding candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(StepUpContent: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateTotpDraftAsync(TotpPolicyVersion candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Totp: SnapshotTotpCandidate(candidate)), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreatePasskeyDraftAsync(PasskeyConfigurationVersion candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Passkey: SnapshotPasskeyCandidate(candidate)), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateSessionListingContentDraftAsync(SessionListingContentBinding candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(SessionListingContent: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateSessionListingDraftAsync(SessionListingHttpBudget candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(SessionListing: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> CreateRecoveryCodeDraftAsync(RecoveryCodeConfigurationVersion candidate, string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(RecoveryCodes: candidate), null, null, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateAuthenticationDraftAsync(Guid proposalReference, long expectedRevision, AuthenticationPolicyVersion candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Authentication: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateAssuranceDraftAsync(Guid proposalReference, long expectedRevision, AssurancePolicyVersion candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Assurance: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateContentBindingDraftAsync(Guid proposalReference, long expectedRevision, AuthenticationContentBinding candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(ContentBinding: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateRegistrationDraftAsync(Guid proposalReference, long expectedRevision, RegistrationPolicyVersion candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Registration: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateRegistrationContentDraftAsync(Guid proposalReference, long expectedRevision, RegistrationContentBinding candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(RegistrationContent: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateTotpContentDraftAsync(Guid proposalReference, long expectedRevision, TotpContentBinding candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(TotpContent: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdatePasskeyContentDraftAsync(Guid proposalReference, long expectedRevision, PasskeyContentBinding candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(PasskeyContent: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateStepUpContentDraftAsync(Guid proposalReference, long expectedRevision, StepUpContentBinding candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(StepUpContent: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateTotpDraftAsync(Guid proposalReference, long expectedRevision, TotpPolicyVersion candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Totp: SnapshotTotpCandidate(candidate)), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdatePasskeyDraftAsync(Guid proposalReference, long expectedRevision, PasskeyConfigurationVersion candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(Passkey: SnapshotPasskeyCandidate(candidate)), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateSessionListingContentDraftAsync(Guid proposalReference, long expectedRevision, SessionListingContentBinding candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(SessionListingContent: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateSessionListingDraftAsync(Guid proposalReference, long expectedRevision, SessionListingHttpBudget candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(SessionListing: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);
    public Task<IdentityConfigurationProposal> UpdateRecoveryCodeDraftAsync(Guid proposalReference, long expectedRevision, RecoveryCodeConfigurationVersion candidate,
        string operationId, string correlationId, CancellationToken cancellationToken = default)
        => DraftAsync(new(RecoveryCodes: candidate), proposalReference, expectedRevision, operationId, correlationId, cancellationToken);

    private static PasskeyConfigurationVersion SnapshotPasskeyCandidate(PasskeyConfigurationVersion candidate)
    {
        if (candidate is null || candidate.Values is null) throw new IdentityConfigurationUnavailableException();
        return candidate with { Values = candidate.Values.Snapshot() };
    }

    private static TotpPolicyVersion SnapshotTotpCandidate(TotpPolicyVersion candidate)
    {
        if (candidate is null || candidate.Values is null || candidate.Values.MfaAssurances is null) throw new IdentityConfigurationUnavailableException();
        // IReadOnlyDictionary is not ownership: the caller may retain and mutate its underlying dictionary during authorization.
        var mappings = new ReadOnlyDictionary<string, string>(candidate.Values.MfaAssurances.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        return candidate with { Values = candidate.Values with { MfaAssurances = mappings } };
    }

    private async Task<IdentityConfigurationProposal> DraftAsync(IdentityConfigurationDocument document, Guid? id, long? revision, string operationId, string correlationId, CancellationToken ct)
    {
        document.Require(); await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var authority = Governance(document.Scope); Bound(document, authority);
        var row = id is { } original ? await ProposalAsync(original, revision, ct).ConfigureAwait(false) : null;
        if (row is not null && (row.State == "PUBLISHED" || row.ScopeKey != document.Scope.Key || row.GovernanceVersion != authority.Metadata.VersionId)) throw new IdentityConfigurationConflictException();
        var op = row is null ? IdentityConfigurationOperation.CreateDraft : IdentityConfigurationOperation.UpdateDraft;
        var target = row?.Id ?? Guid.NewGuid(); var request = Request(op, document.Scope, authority, target.ToString("N"), row?.Revision ?? 0, document.Digest, operationId, correlationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false); await EnsureGovernanceAsync(authority, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new() { Id = target, ScopeKey = document.Scope.Key, GovernanceVersion = authority.Metadata.VersionId, Revision = 1,
                ContributorsJson = JsonSerializer.Serialize(new[] { actor.ActorReference }) };
            database.Set<IdentityConfigurationProposalRow>().Add(row);
        }
        else { row.Revision++; row.ContributorsJson = JsonSerializer.Serialize(Authors(row.ContributorsJson).Append(actor.ActorReference).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)); }
        row.Payload = document.Snapshot; row.Digest = document.Digest; row.PolicyVersionReference = document.Version; row.State = "DRAFT";
        row.ModelOrigin |= actor.ActorKind == IdentityConfigurationActorKind.Model; row.VersionReference = null;
        row.ApproverReference = null; row.ApprovalEvidenceReference = null; row.ApprovalEventReference = null;
        await FinishAsync(tx, authority, request, actor, target, row.Revision, row.Digest, row, null, null, ct).ConfigureAwait(false); return View(row);
    }

    public async Task<IdentityConfigurationProposal> ValidateAsync(IdentityConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false); var document = IdentityConfigurationDocument.Read(row.Payload);
        var authority = await ProposalGovernanceAsync(row, document, ct).ConfigureAwait(false);
        await LockAsync("version:" + row.ScopeKey + ":" + row.PolicyVersionReference, ct).ConfigureAwait(false);
        if (row.State != "DRAFT" || await database.Set<IdentityConfigurationVersionRow>().AnyAsync(x => x.ScopeKey == row.ScopeKey && x.PolicyVersionReference == row.PolicyVersionReference, ct).ConfigureAwait(false))
            throw new IdentityConfigurationConflictException();
        var request = Request(IdentityConfigurationOperation.Validate, document.Scope, authority, row.Id.ToString("N"), row.Revision, row.Digest, command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var version = new IdentityConfigurationVersionRow { Id = Guid.NewGuid(), ProposalReference = row.Id, ScopeKey = row.ScopeKey, GovernanceVersion = row.GovernanceVersion,
            PolicyVersionReference = row.PolicyVersionReference, Payload = row.Payload, Digest = row.Digest, ContributorsJson = row.ContributorsJson, ModelOrigin = row.ModelOrigin };
        database.Set<IdentityConfigurationVersionRow>().Add(version); row.State = "VALIDATED"; row.VersionReference = version.Id; row.Revision++;
        await FinishAsync(tx, authority, request, actor, version.Id, row.Revision, row.Digest, row, version, null, ct).ConfigureAwait(false); return View(row);
    }

    public async Task<IdentityConfigurationProposal> ApproveAsync(IdentityConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false); var document = IdentityConfigurationDocument.Read(row.Payload);
        var authority = await ProposalGovernanceAsync(row, document, ct).ConfigureAwait(false);
        if (row.State != "VALIDATED" || row.VersionReference is null) throw new IdentityConfigurationConflictException();
        var request = Request(IdentityConfigurationOperation.Approve, document.Scope, authority, row.Id.ToString("N"), row.Revision,
            IdentityConfigurationDigest.Of(new { row.Digest, row.VersionReference }), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (authority.SeparateAuthorApprover && Authors(row.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal)) throw new IdentityConfigurationForbiddenException();
        row.ApproverReference = actor.ActorReference; row.ApprovalEvidenceReference = actor.EvidenceReference; row.State = "APPROVED"; row.Revision++;
        await FinishAsync(tx, authority, request, actor, row.Id, row.Revision, row.Digest, row, null, null, ct).ConfigureAwait(false); return View(row);
    }

    public async Task<IdentityConfigurationPreview> PreviewAsync(IdentityConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false); var value = IdentityConfigurationDocument.Read(row.Payload);
        var authority = await ProposalGovernanceAsync(row, value, ct).ConfigureAwait(false);
        var request = Request(IdentityConfigurationOperation.Preview, value.Scope, authority, row.Id.ToString("N"), row.Revision, row.Digest, command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        await FinishAsync(tx, authority, request, actor, row.Id, row.Revision, row.Digest, null, null, null, ct).ConfigureAwait(false);
        return new(View(row), value.Authentication, value.Assurance, value.ContentBinding) { Registration = value.Registration, Totp = value.Totp, RegistrationContent = value.RegistrationContent, Passkey = value.Passkey, TotpContent = value.TotpContent, PasskeyContent = value.PasskeyContent, StepUpContent = value.StepUpContent, RecoveryCodes = value.RecoveryCodes, SessionListing = value.SessionListing, SessionListingContent = value.SessionListingContent };
    }

    public async Task<IdentityConfigurationHistory> PublishAsync(PublishIdentityConfiguration command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var row = await ProposalAsync(command.ProposalReference, command.ExpectedRevision, ct).ConfigureAwait(false); var value = IdentityConfigurationDocument.Read(row.Payload);
        var authority = await ProposalGovernanceAsync(row, value, ct).ConfigureAwait(false);
        if (row.State != "APPROVED" || row.VersionReference is null || row.ApprovalEventReference is null || string.IsNullOrWhiteSpace(row.ApproverReference) || string.IsNullOrWhiteSpace(row.ApprovalEvidenceReference))
            throw new IdentityConfigurationConflictException();
        var version = await database.Set<IdentityConfigurationVersionRow>().SingleAsync(x => x.Id == row.VersionReference, ct).ConfigureAwait(false);
        var request = Request(IdentityConfigurationOperation.Publish, value.Scope, authority, row.Id.ToString("N"), row.Revision,
            IdentityConfigurationDigest.Of(command), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false); Publisher(authority, version, row, actor);
        var publication = await PublicationAsync(authority, value, version, row, actor, command.EffectiveInterval, command.SupersedesPublicationReference, ct).ConfigureAwait(false);
        row.State = "PUBLISHED"; row.Revision++;
        await FinishAsync(tx, authority, request, actor, publication.Id, row.Revision, row.Digest, row, null, publication, ct).ConfigureAwait(false); return History(publication, version, value.Scope);
    }

    public async Task<IdentityConfigurationHistory> RollbackAsync(RollbackIdentityConfiguration command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false);
        var version = await database.Set<IdentityConfigurationVersionRow>().SingleOrDefaultAsync(x => x.Id == command.ApprovedVersionReference, ct).ConfigureAwait(false) ?? throw new IdentityConfigurationConflictException();
        var row = await ProposalAsync(version.ProposalReference, null, ct).ConfigureAwait(false); var value = IdentityConfigurationDocument.Read(version.Payload);
        var authority = await ProposalGovernanceAsync(row, value, ct).ConfigureAwait(false);
        if (row.State != "PUBLISHED" || row.VersionReference != version.Id || row.ApprovalEventReference is null ||
            !await database.Set<IdentityConfigurationPublicationRow>().AnyAsync(x => x.VersionReference == version.Id, ct).ConfigureAwait(false)) throw new IdentityConfigurationConflictException();
        var request = Request(IdentityConfigurationOperation.Rollback, value.Scope, authority, version.Id.ToString("N"), row.Revision,
            IdentityConfigurationDigest.Of(command), command.OperationId, command.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false); Publisher(authority, version, row, actor);
        var publication = await PublicationAsync(authority, value, version, row, actor, command.EffectiveInterval, command.SupersedesPublicationReference, ct).ConfigureAwait(false);
        await FinishAsync(tx, authority, request, actor, publication.Id, row.Revision, row.Digest, null, null, publication, ct).ConfigureAwait(false); return History(publication, version, value.Scope);
    }

    public async Task<IReadOnlyList<IdentityConfigurationHistory>> HistoryAsync(IdentityConfigurationScope scope, string operationId, string correlationId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken; await using var tx = await BeginAsync(ct).ConfigureAwait(false); var authority = Governance(scope);
        var request = Request(IdentityConfigurationOperation.History, scope, authority, scope.Key, 0, IdentityConfigurationDigest.Of(scope), operationId, correlationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        var versions = await database.Set<IdentityConfigurationVersionRow>().AsNoTracking().Where(x => x.ScopeKey == scope.Key).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var publications = await database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().Where(x => x.ScopeKey == scope.Key).ToArrayAsync(ct).ConfigureAwait(false);
        await FinishAsync(tx, authority, request, actor, Guid.Empty, 0, request.CandidateDigest, null, null, null, ct).ConfigureAwait(false);
        return publications.OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id).Select(x => History(x, versions[x.VersionReference], scope)).ToArray();
    }

    private async Task<IdentityConfigurationPublicationRow> PublicationAsync(IdentityConfigurationGovernance authority, IdentityConfigurationDocument value,
        IdentityConfigurationVersionRow version, IdentityConfigurationProposalRow row, IdentityConfigurationAuthorizationDecision actor,
        EffectivePolicyInterval interval, Guid? supersedes, CancellationToken ct)
    {
        Interval(authority, value.Interval, interval); await LockAsync("publication:" + row.ScopeKey, ct).ConfigureAwait(false);
        var prior = await database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().Where(x => x.ScopeKey == row.ScopeKey).ToArrayAsync(ct).ConfigureAwait(false);
        var leaves = prior.Where(x => !prior.Any(y => y.SupersedesPublicationReference == x.Id)).ToArray();
        if (leaves.Length > 1 || (leaves.Length == 0 ? supersedes is not null : supersedes != leaves[0].Id || interval.From <= leaves[0].EffectiveFrom)) throw new IdentityConfigurationConflictException();
        var publication = new IdentityConfigurationPublicationRow { Id = Guid.NewGuid(), VersionReference = version.Id, ScopeKey = row.ScopeKey,
            EffectiveFrom = interval.From, IncludesFrom = interval.IncludesFrom, EffectiveUntil = interval.Until, IncludesUntil = interval.IncludesUntil,
            SupersedesPublicationReference = supersedes, ApproverReference = row.ApproverReference!, ApprovalEvidenceReference = row.ApprovalEvidenceReference!,
            ApprovalEventReference = row.ApprovalEventReference!.Value, PublisherReference = actor.ActorReference, PublisherEvidenceReference = actor.EvidenceReference,
            PublisherAuthorizationVersion = actor.AuthorizationPolicyVersion };
        database.Set<IdentityConfigurationPublicationRow>().Add(publication); return publication;
    }

    private async Task<IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new IdentityConfigurationConflictException();
        database.ChangeTracker.Clear(); return await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
    }
    private Task<int> LockAsync(string key, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"identity-runtime-configuration:" + key},0))", ct);
    private async Task<IdentityConfigurationProposalRow> ProposalAsync(Guid reference, long? revision, CancellationToken ct)
    {
        if (reference == Guid.Empty) throw new IdentityConfigurationConflictException(); await LockAsync("proposal:" + reference.ToString("N"), ct).ConfigureAwait(false);
        var row = await database.Set<IdentityConfigurationProposalRow>().SingleOrDefaultAsync(x => x.Id == reference, ct).ConfigureAwait(false) ?? throw new IdentityConfigurationConflictException();
        if (revision is not null && row.Revision != revision) throw new IdentityConfigurationConflictException(); return row;
    }
    private IdentityConfigurationGovernance Governance(IdentityConfigurationScope scope)
    { var value = governance.RequireEffective(scope, clock.GetUtcNow()) ?? throw new IdentityConfigurationUnavailableException(); value.Require(scope, clock.GetUtcNow()); return value; }
    private static void Bound(IdentityConfigurationDocument value, IdentityConfigurationGovernance authority)
    { if (Encoding.UTF8.GetByteCount(value.Snapshot) > authority.MaximumPayloadBytes) throw new IdentityConfigurationUnavailableException(); }
    private async Task<IdentityConfigurationGovernance> ProposalGovernanceAsync(IdentityConfigurationProposalRow row, IdentityConfigurationDocument value, CancellationToken ct)
    {
        if (value.Scope.Key != row.ScopeKey || value.Digest != row.Digest || value.Version != row.PolicyVersionReference) throw new IdentityConfigurationConflictException();
        var authority = Governance(value.Scope); Bound(value, authority);
        if (authority.Metadata.VersionId != row.GovernanceVersion) throw new IdentityConfigurationConflictException();
        var stored = await database.Set<IdentityConfigurationGovernanceRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == row.ScopeKey && x.Version == row.GovernanceVersion, ct).ConfigureAwait(false);
        if (stored?.Snapshot != authority.Snapshot) throw new IdentityConfigurationConflictException(); return authority;
    }
    private async Task EnsureGovernanceAsync(IdentityConfigurationGovernance authority, CancellationToken ct)
    {
        var scope = authority.Metadata.Scope.Key; await LockAsync("governance:" + scope + ":" + authority.Metadata.VersionId, ct).ConfigureAwait(false);
        var existing = await database.Set<IdentityConfigurationGovernanceRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Version == authority.Metadata.VersionId, ct).ConfigureAwait(false);
        if (existing is not null) { if (existing.Snapshot != authority.Snapshot) throw new IdentityConfigurationConflictException(); return; }
        database.Set<IdentityConfigurationGovernanceRow>().Add(new() { ScopeKey = scope, Version = authority.Metadata.VersionId, Snapshot = authority.Snapshot });
    }
    private static string[] Authors(string json) => JsonSerializer.Deserialize<string[]>(json) ?? throw new IdentityConfigurationUnavailableException();
    private static IdentityConfigurationProposal View(IdentityConfigurationProposalRow row) => new(row.Id, row.Revision, row.State,
        IdentityConfigurationDocument.Read(row.Payload).Scope, row.PolicyVersionReference, row.VersionReference, row.Digest, row.ModelOrigin);
    private static IdentityConfigurationHistory History(IdentityConfigurationPublicationRow row, IdentityConfigurationVersionRow version, IdentityConfigurationScope scope) =>
        new(row.Id, version.Id, scope, version.PolicyVersionReference, version.Digest, new(row.EffectiveFrom, row.IncludesFrom, row.EffectiveUntil, row.IncludesUntil),
            row.SupersedesPublicationReference, row.ApproverReference, row.ApprovalEvidenceReference, row.PublisherReference, version.GovernanceVersion);
    private static void Publisher(IdentityConfigurationGovernance authority, IdentityConfigurationVersionRow version, IdentityConfigurationProposalRow row, IdentityConfigurationAuthorizationDecision actor)
    {
        if (authority.SeparateAuthorPublisher && Authors(version.ContributorsJson).Contains(actor.ActorReference, StringComparer.Ordinal) ||
            authority.SeparateApproverPublisher && row.ApproverReference == actor.ActorReference) throw new IdentityConfigurationForbiddenException();
    }
    private void Interval(IdentityConfigurationGovernance authority, EffectivePolicyInterval candidate, EffectivePolicyInterval publication)
    {
        if (publication is null || publication.From.Offset != TimeSpan.Zero || publication.From.Ticks % 10 != 0 ||
            publication.Until is { } end && (end.Offset != TimeSpan.Zero || end.Ticks % 10 != 0) || publication.From < clock.GetUtcNow() ||
            !Within(candidate, publication) || !Within(authority.Metadata.EffectiveInterval, publication)) throw new IdentityConfigurationConflictException();
    }
    private static bool Within(EffectivePolicyInterval outer, EffectivePolicyInterval inner) =>
        (inner.From > outer.From || inner.From == outer.From && (!inner.IncludesFrom || outer.IncludesFrom)) &&
        (outer.Until is null || inner.Until is { } end && (end < outer.Until || end == outer.Until && (!inner.IncludesUntil || outer.IncludesUntil)));
    private static IdentityConfigurationAuthorizationRequest Request(IdentityConfigurationOperation op, IdentityConfigurationScope scope, IdentityConfigurationGovernance authority,
        string target, long revision, string digest, string operationId, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (!authority.Effects.ContainsKey(op)) throw new IdentityConfigurationUnavailableException();
        return new(op, scope, target, revision, digest, authority.Metadata.VersionId, IdentityConfigurationDigest.Of(authority), operationId, correlationId);
    }
    private async Task<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(IdentityConfigurationAuthorizationRequest request, CancellationToken ct)
    {
        var actor = await authorization.AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (actor is null || !actor.Allowed || actor.RequestDigest != request.Digest || !Enum.IsDefined(actor.ActorKind) ||
            new[] { actor.ActorReference, actor.AuditActorType, actor.AuthorizationPolicyVersion, actor.EvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
            actor.EffectiveInterval is null || !actor.EffectiveInterval.Contains(clock.GetUtcNow()) ||
            actor.ActorKind == IdentityConfigurationActorKind.Model && request.Operation is not (IdentityConfigurationOperation.CreateDraft or IdentityConfigurationOperation.UpdateDraft))
            throw new IdentityConfigurationForbiddenException(); return actor;
    }
    private async Task FrontierAsync(IdentityConfigurationGovernance authority, IdentityConfigurationAuthorizationRequest request, IdentityConfigurationAuthorizationDecision actor,
        IdentityConfigurationPublicationRow? publication, IdentityConfigurationDocument? value, CancellationToken ct)
    {
        var current = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (current.ActorReference != actor.ActorReference || current.ActorKind != actor.ActorKind || current.AuditActorType != actor.AuditActorType ||
            current.AuthorizationPolicyVersion != actor.AuthorizationPolicyVersion || current.EvidenceReference != actor.EvidenceReference) throw new IdentityConfigurationForbiddenException();
        if (Governance(request.Scope).Snapshot != authority.Snapshot || IdentityConfigurationDigest.Of(authority) != request.GovernanceDigest) throw new IdentityConfigurationConflictException();
        if (publication is not null) Interval(authority, value!.Interval, new(publication.EffectiveFrom, publication.IncludesFrom, publication.EffectiveUntil, publication.IncludesUntil));
    }
    private async Task FinishAsync(IDbContextTransaction tx, IdentityConfigurationGovernance authority, IdentityConfigurationAuthorizationRequest request,
        IdentityConfigurationAuthorizationDecision actor, Guid target, long revision, string digest, IdentityConfigurationProposalRow? proposal,
        IdentityConfigurationVersionRow? version, IdentityConfigurationPublicationRow? publication, CancellationToken ct)
    {
        IdentityConfigurationDocument? value = null;
        if (publication is not null) value = IdentityConfigurationDocument.Read((await database.Set<IdentityConfigurationVersionRow>().AsNoTracking().SingleAsync(x => x.Id == publication.VersionReference, ct).ConfigureAwait(false)).Payload);
        await FrontierAsync(authority, request, actor, publication, value, ct).ConfigureAwait(false);
        var eventId = Guid.NewGuid(); var at = clock.GetUtcNow(); var reference = target == Guid.Empty ? request.TargetReference : target.ToString("N");
        if (proposal is not null)
        {
            proposal.LastEventReference = eventId; proposal.LastOperation = request.Operation.ToString(); proposal.LastTargetReference = target; proposal.LastActorReference = actor.ActorReference;
            if (request.Operation == IdentityConfigurationOperation.Approve) proposal.ApprovalEventReference = eventId;
        }
        if (version is not null) version.ValidationEventReference = eventId;
        if (publication is not null) { publication.PublicationEventReference = eventId; publication.Operation = request.Operation.ToString(); }
        var effect = authority.Effects[request.Operation]; var versionReference = publication?.VersionReference ?? version?.Id ?? proposal?.VersionReference;
        var audit = new AuditEvent(eventId, at, new(actor.ActorReference, actor.AuditActorType, [actor.EvidenceReference]), effect.AuditAction, request.OperationId,
            request.CorrelationId, [new(reference, revision.ToString(CultureInfo.InvariantCulture))], effect.AuditResult, authority.Classification, authority.AuditRetentionClassReference,
            policyVersionReferences: [authority.Metadata.VersionId, actor.AuthorizationPolicyVersion],
            evidenceReferences: authority.Metadata.ApprovalEvidenceReferences.Concat([authority.EvidenceReference, actor.EvidenceReference]));
        EfAuditAppender.StageAppend(database, audit);
        if (effect.EventType is not null) database.Set<OutboxMessage>().Add(new(eventId, effect.EnvelopeVersion, effect.EventType, effect.EventSchemaVersion!, at, effect.Source,
            reference, authority.Classification, CommandPayload(new { reference, scopeKey = request.Scope.Key, governanceVersion = authority.Metadata.VersionId,
                revision, candidateDigest = digest, versionReference = versionReference?.ToString("N"), authorizationEvidenceReference = actor.EvidenceReference,
                authorizationRequestDigest = request.Digest, effectiveFrom = publication?.EffectiveFrom, includesFrom = publication?.IncludesFrom,
                effectiveUntil = publication?.EffectiveUntil, includesUntil = publication?.IncludesUntil,
                supersedesPublicationReference = publication?.SupersedesPublicationReference?.ToString("N") }, CommandReceipt(request, actor, proposal, publication)), revision.ToString(CultureInfo.InvariantCulture), request.OperationId, request.CorrelationId));
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        await FrontierAsync(authority, request, actor, publication, value, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
