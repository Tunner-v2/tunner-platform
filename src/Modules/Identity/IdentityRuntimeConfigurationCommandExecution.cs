using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tunner.Database.Outbox;
using Tunner.Database.Transactions;

namespace Tunner.Identity.Persistence;

public sealed record IdentityConfigurationCommandResult<T>(T Result, bool Replayed, string OperationId);
internal sealed record IdentityConfigurationCommandReceipt(string RequestDigest, IdentityConfigurationAuthorizationRequest Authorization,
    string ActorReference, IdentityConfigurationActorKind ActorKind, IdentityConfigurationProposal? Proposal, Guid? PublicationReference);

public sealed partial class IdentityRuntimeConfigurationApplication
{
    private sealed class ActiveCommand(string operationId, string digest)
    { internal string OperationId { get; } = operationId; internal string Digest { get; } = digest; internal bool Attached; }
    private ActiveCommand? activeCommand;
    private static string CommandPayload(object metadata, IdentityConfigurationCommandReceipt? receipt)
    {
        if (receipt is null) return JsonSerializer.Serialize(metadata);
        var payload = JsonSerializer.SerializeToNode(metadata)!.AsObject(); payload["commandReceipt"] = JsonSerializer.SerializeToNode(receipt); return payload.ToJsonString();
    }
    public async Task<IdentityConfigurationCommandResult<T>> ExecuteCommandAsync<T>(string operationId, string requestDigest, Func<Task<T>> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentNullException.ThrowIfNull(command);
        if (requestDigest is not { Length: 64 } || !requestDigest.All(Uri.IsHexDigit)) throw new ArgumentException("An exact command digest is required.");
        if (typeof(T) != typeof(IdentityConfigurationProposal) && typeof(T) != typeof(IdentityConfigurationHistory)) throw new ArgumentException("Unsupported configuration command result.");
        if (activeCommand is not null || database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new IdentityConfigurationConflictException();
        database.ChangeTracker.Clear(); var ct = cancellationToken; PostgresCommandLease? lease = null; Exception? failure = null;
        if (database.Database.GetDbConnection().State != ConnectionState.Closed) throw new IdentityConfigurationConflictException();
        try
        {
            lease = await PostgresCommandLease.AcquireAsync(database.Database, "identity-runtime-configuration-command:" + operationId,
                static connection => { if (connection is NpgsqlConnection npgsql) NpgsqlConnection.ClearPool(npgsql); }, ct).ConfigureAwait(false);
            // An applied command remains applied after governance changes; current permission is independently rechecked below.
            var prior = await database.Set<OutboxMessage>().AsNoTracking().Where(x => x.OperationId == operationId).Take(2).ToArrayAsync(ct).ConfigureAwait(false);
            if (prior.Length != 0)
            {
                if (prior.Length != 1) throw new IdentityConfigurationConflictException();
                using var payload = JsonDocument.Parse(prior[0].Payload);
                if (!payload.RootElement.TryGetProperty("commandReceipt", out var stored)) throw new IdentityConfigurationConflictException();
                var receipt = stored.Deserialize<IdentityConfigurationCommandReceipt>() ?? throw new IdentityConfigurationConflictException();
                if (receipt.Authorization is null || receipt.RequestDigest != requestDigest || receipt.Authorization.OperationId != operationId ||
                    string.IsNullOrWhiteSpace(receipt.ActorReference) || receipt.Authorization.IsCapabilityProbe) throw new IdentityConfigurationConflictException();
                return new(await ReplayCommandAsync<T>(receipt, ct).ConfigureAwait(false), true, operationId);
            }
            activeCommand = new(operationId, requestDigest); var result = await command().ConfigureAwait(false);
            if (!activeCommand.Attached) throw new IdentityConfigurationConflictException(); return new(result, false, operationId);
        }
        catch (Exception error) { failure = error; database.ChangeTracker.Clear(); throw; }
        finally { activeCommand = null; if (lease is not null) await lease.ReleaseAsync(failure).ConfigureAwait(false); }
    }
    private IdentityConfigurationCommandReceipt? CommandReceipt(IdentityConfigurationAuthorizationRequest request, IdentityConfigurationAuthorizationDecision actor,
        IdentityConfigurationProposalRow? proposal, IdentityConfigurationPublicationRow? publication)
    {
        if (activeCommand is null) return null;
        if (request.OperationId != activeCommand.OperationId || request.IsCapabilityProbe || request.Operation is IdentityConfigurationOperation.Browse or IdentityConfigurationOperation.Preview or IdentityConfigurationOperation.History ||
            publication is null && proposal is null) throw new IdentityConfigurationConflictException();
        activeCommand.Attached = true;
        return new(activeCommand.Digest, request, actor.ActorReference, actor.ActorKind, publication is null ? View(proposal!) : null, publication?.Id);
    }
    private async Task<T> ReplayCommandAsync<T>(IdentityConfigurationCommandReceipt receipt, CancellationToken ct)
    {
        var original = receipt.Authorization; var authority = Governance(original.Scope);
        var request = Request(original.Operation, original.Scope, authority, original.TargetReference, original.ExpectedRevision, original.CandidateDigest, original.OperationId, original.CorrelationId);
        var actor = await AuthorizeAsync(request, ct).ConfigureAwait(false);
        if (actor.ActorReference != receipt.ActorReference || actor.ActorKind != receipt.ActorKind) throw new IdentityConfigurationForbiddenException();
        object result;
        if (typeof(T) == typeof(IdentityConfigurationProposal) && receipt.Proposal is { } proposal)
        {
            if (proposal.Scope != original.Scope || proposal.ProposalReference == Guid.Empty || proposal.Revision < 1) throw new IdentityConfigurationConflictException(); result = proposal;
        }
        else if (typeof(T) == typeof(IdentityConfigurationHistory) && receipt.PublicationReference is { } id)
        {
            var publication = await database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw new IdentityConfigurationConflictException();
            var version = await database.Set<IdentityConfigurationVersionRow>().AsNoTracking().SingleAsync(x => x.Id == publication.VersionReference, ct).ConfigureAwait(false);
            if (publication.ScopeKey != original.Scope.Key) throw new IdentityConfigurationConflictException(); result = History(publication, version, original.Scope);
        }
        else throw new IdentityConfigurationConflictException();
        await FrontierAsync(authority, request, actor, null, null, ct).ConfigureAwait(false); return (T)result;
    }
}
