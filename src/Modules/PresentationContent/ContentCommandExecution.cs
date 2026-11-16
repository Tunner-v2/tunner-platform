using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tunner.Database.Outbox;

namespace Tunner.PresentationContent;

/// <summary>Original command result, not a claim about the resource's current revision. Clients reload after replay.</summary>
public sealed record ContentCommandResult<T>(T Result, bool Replayed, string OperationId);
internal sealed record ContentCommandReceipt(string RequestDigest, ContentAuthorizationRequest Authorization,
    string ActorReference, ContentActorKind ActorKind, ContentProposalView? Proposal, Guid? PublicationReference);

public sealed partial class PresentationContentApplication
{
    private sealed class ActiveCommand(string operationId, string digest)
    {
        internal string OperationId { get; } = operationId;
        internal string Digest { get; } = digest;
        internal bool Attached;
    }
    private ActiveCommand? activeCommand;
    private static string CommandPayload(object metadata, ContentCommandReceipt? receipt)
    {
        if (receipt is null) return JsonSerializer.Serialize(metadata);
        var payload = JsonSerializer.SerializeToNode(metadata)!.AsObject(); payload["commandReceipt"] = JsonSerializer.SerializeToNode(receipt);
        return payload.ToJsonString();
    }

    /// <summary>Serialize one server-bound caller key on this held PostgreSQL connection. The existing owner transaction
    /// atomically writes its original reference-only receipt in the existing outbox; no separate receipt commit exists.</summary>
    public async Task<ContentCommandResult<T>> ExecuteCommandAsync<T>(string operationId, string requestDigest, Func<Task<T>> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentNullException.ThrowIfNull(command);
        if (requestDigest is not { Length: 64 } || !requestDigest.All(Uri.IsHexDigit)) throw new ArgumentException("An exact command digest is required.");
        if (typeof(T) != typeof(ContentProposalView) && typeof(T) != typeof(ContentHistoryEntry)) throw new ArgumentException("Unsupported content command result.");
        if (activeCommand is not null || database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new ContentConflictException();
        database.ChangeTracker.Clear(); var ct = cancellationToken; var connection = database.Database.GetDbConnection();
        if (connection.State != ConnectionState.Closed) throw new ContentConflictException();
        var acquired = false; Exception? originalFailure = null;
        try
        {
            await database.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            _ = await AdvisoryAsync(connection, operationId, true, ct).ConfigureAwait(false); acquired = true;
            // Never filter by CURRENT profile: an applied action must remain an applied action after governance changes.
            var prior = await database.Set<OutboxMessage>().AsNoTracking().Where(x => x.OperationId == operationId).Take(2).ToArrayAsync(ct).ConfigureAwait(false);
            if (prior.Length != 0)
            {
                if (prior.Length != 1) throw new ContentConflictException();
                using var payload = JsonDocument.Parse(prior[0].Payload);
                if (!payload.RootElement.TryGetProperty("commandReceipt", out var stored) || stored.ValueKind != JsonValueKind.Object) throw new ContentConflictException();
                var receipt = stored.Deserialize<ContentCommandReceipt>() ?? throw new ContentConflictException();
                if (receipt.Authorization is null || string.IsNullOrWhiteSpace(receipt.ActorReference) ||
                    receipt.RequestDigest != requestDigest || receipt.Authorization.OperationId != operationId) throw new ContentConflictException();
                return new(await ReplayCommandAsync<T>(receipt, ct).ConfigureAwait(false), true, operationId);
            }
            activeCommand = new(operationId, requestDigest);
            var result = await command().ConfigureAwait(false);
            if (!activeCommand.Attached) throw new ContentConflictException();
            return new(result, false, operationId);
        }
        catch (Exception error) { originalFailure = error; database.ChangeTracker.Clear(); throw; }
        finally
        {
            activeCommand = null;
            await ReleaseCommandConnectionAsync(connection, operationId, acquired, originalFailure).ConfigureAwait(false);
        }
    }

    private async Task ReleaseCommandConnectionAsync(DbConnection connection, string operationId, bool acquired, Exception? originalFailure)
    {
        try
        {
            // Acquisition can succeed on the server just before client cancellation: release even that uncertain case.
            if (connection.State == ConnectionState.Open)
            {
                var released = await AdvisoryAsync(connection, operationId, false, CancellationToken.None).ConfigureAwait(false);
                if (acquired && !Equals(released, true)) throw new ContentUnavailableException();
            }
        }
        catch when (originalFailure is not null)
        { if (connection is NpgsqlConnection npgsql) NpgsqlConnection.ClearPool(npgsql); }
        catch
        { if (connection is NpgsqlConnection npgsql) NpgsqlConnection.ClearPool(npgsql); throw; }
        finally
        {
            try { await database.Database.CloseConnectionAsync().ConfigureAwait(false); }
            catch when (originalFailure is not null) { if (connection is NpgsqlConnection npgsql) NpgsqlConnection.ClearPool(npgsql); }
        }
    }

    private static async Task<object?> AdvisoryAsync(DbConnection connection, string operationId, bool acquire, CancellationToken ct)
    {
        await using var sql = connection.CreateCommand();
        sql.CommandText = acquire ? "SELECT pg_advisory_lock(hashtextextended(@reference,0))" : "SELECT pg_advisory_unlock(hashtextextended(@reference,0))";
        var value = sql.CreateParameter(); value.ParameterName = "reference"; value.Value = "presentation-content-command:" + operationId; sql.Parameters.Add(value);
        return await sql.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private ContentCommandReceipt? CommandReceipt(ContentAuthorizationRequest request, ContentAuthorizationDecision actor, Guid reference)
    {
        if (activeCommand is null) return null;
        if (request.OperationId != activeCommand.OperationId || request.Operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History)
            throw new ContentConflictException();
        ContentProposalView? proposal = null; Guid? publication = null;
        if (request.Operation is ContentOperation.Publish or ContentOperation.Rollback)
        {
            if (!database.ChangeTracker.Entries<ContentPublicationRow>().Any(x => x.State == EntityState.Added && x.Entity.Id == reference)) throw new ContentConflictException();
            publication = reference;
        }
        else
        {
            // Each exact command changes one proposal; never choose the first unrelated tracked resource.
            var changed = database.ChangeTracker.Entries<ContentProposalRow>().Where(x => x.State is EntityState.Added or EntityState.Modified).Select(x => x.Entity).ToArray();
            if (changed.Length != 1 || changed[0].Scope != request.Scope) throw new ContentConflictException();
            proposal = View(changed[0]);
        }
        activeCommand.Attached = true;
        return new(activeCommand.Digest, request, actor.ActorReference, actor.ActorKind, proposal, publication);
    }

    private async Task<T> ReplayCommandAsync<T>(ContentCommandReceipt receipt, CancellationToken ct)
    {
        var original = receipt.Authorization; var profile = Profile(original.Scope); var profileSnapshot = profile.Snapshot;
        // Recheck CURRENT authorization against the original exact resource/intent, without reapplying the old mutation.
        var request = Request(original.Operation, profile, original.TargetReference, original.ExpectedRevision, original.CandidateDigest, original.OperationId, original.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (actor.ActorReference != receipt.ActorReference || actor.ActorKind != receipt.ActorKind) throw new ContentForbiddenException();
        object result;
        if (typeof(T) == typeof(ContentProposalView) && receipt.Proposal is { } proposal)
        {
            if (proposal.Scope != original.Scope || proposal.ProposalReference == Guid.Empty || proposal.Revision < 1) throw new ContentConflictException();
            result = proposal;
        }
        else if (typeof(T) == typeof(ContentHistoryEntry) && receipt.PublicationReference is { } publicationId)
        {
            var publication = await database.Publications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == publicationId, ct).ConfigureAwait(false) ?? throw new ContentConflictException();
            var version = await database.Versions.AsNoTracking().SingleAsync(x => x.Id == publication.VersionReference, ct).ConfigureAwait(false);
            if (publication.Scope != original.Scope) throw new ContentConflictException(); result = History(publication, version);
        }
        else throw new ContentConflictException();
        var final = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (final.ActorReference != actor.ActorReference || final.ActorKind != actor.ActorKind || final.AuthorizationPolicyVersion != actor.AuthorizationPolicyVersion ||
            final.EvidenceReference != actor.EvidenceReference || Profile(original.Scope).Snapshot != profileSnapshot) throw new ContentForbiddenException();
        return (T)result;
    }
}
