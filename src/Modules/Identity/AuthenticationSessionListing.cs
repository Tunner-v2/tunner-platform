using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    internal const string ListSessionsOperation = "LIST_SESSIONS";

    /// <summary>Bounded User-surface snapshot. Reading a target never renews or ends that target.</summary>
    internal async Task<SessionListingPage?> ListOwnedSessionsAsync(StepUpCaller caller, SessionListingReadWindow window,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(window);
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Session listing requires a clean Identity transaction boundary.");
        if (window.After is { } boundary && (boundary.AccountId != caller.AccountId || boundary.ActorSessionId != caller.SessionId ||
            boundary.Origin != caller.Origin || boundary.SessionId == Guid.Empty))
            throw new ArgumentException("The session listing continuation does not match its current actor.", nameof(window));

        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var actor = await LockForOperationAsync(caller, ListSessionsOperation, ct).ConfigureAwait(false);
            if (actor is null)
            {
                // Preserve the ordinary authentication validator's expiry/revocation, if any.
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
                return null;
            }
            var policy = policies.RequireEffective(clock.GetUtcNow()); policy.Validate();
            var query = database.Set<AuthenticationSessionRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId &&
                x.Scheme == AuthenticationIdentity.Scheme && x.Origin == caller.Origin);
            if (window.After is { } after)
                query = query.Where(x => x.AuthenticatedAt > after.AuthenticatedAt ||
                    x.AuthenticatedAt == after.AuthenticatedAt && x.SessionId.CompareTo(after.SessionId) > 0);
            var rows = await query.OrderBy(x => x.AuthenticatedAt).ThenBy(x => x.SessionId)
                .Take(window.MaximumRows + 1).ToArrayAsync(ct).ConfigureAwait(false);
            var hasMore = rows.Length > window.MaximumRows;
            var items = new List<SessionListingItem>(Math.Min(rows.Length, window.MaximumRows));
            foreach (var persisted in rows.Take(window.MaximumRows))
            {
                var row = persisted.SessionId == caller.SessionId ? actor : persisted;
                var recorded = ListingState(row.State); var effective = recorded;
                var at = clock.GetUtcNow();
                if (recorded == SessionListingState.Active)
                {
                    if (Expired(row, policy, at)) effective = SessionListingState.Expired;
                    else if (row.SecurityStampDigest != actor.SecurityStampDigest ||
                        !await MethodIsCurrentAsync(row, policy, at, ct).ConfigureAwait(false)) effective = SessionListingState.Revoked;
                    at = clock.GetUtcNow();
                    if (effective == SessionListingState.Active && Expired(row, policy, at)) effective = SessionListingState.Expired;
                    else if (effective == SessionListingState.Active && !MethodFactsCurrentAt(row, policy, at)) effective = SessionListingState.Revoked;
                }
                items.Add(new(row.SessionId, row.Version, row.SessionId == caller.SessionId, row.AuthenticatedAt,
                    row.LastSeenAt, row.ExpiresAt, recorded, effective, at));
            }
            // Recheck mutable external policy/access authority after all asynchronous target reads.
            var account = await database.Accounts.SingleAsync(x => x.AccountId == caller.AccountId, ct).ConfigureAwait(false);
            var use = await AccessAsync(account, "SESSION_USE", clock.GetUtcNow(), ct).ConfigureAwait(false);
            var listing = await AccessAsync(account, ListSessionsOperation, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var currentPolicy = policies.RequireEffective(clock.GetUtcNow()); currentPolicy.Validate();
            var methodCurrent = await MethodIsCurrentAsync(actor, currentPolicy, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var decisionAt = clock.GetUtcNow();
            var decisionPolicy = policies.RequireEffective(decisionAt); decisionPolicy.Validate();
            if (policy != currentPolicy || currentPolicy != decisionPolicy || use?.Allowed != true || listing?.Allowed != true ||
                !use.EffectiveInterval.Contains(decisionAt) || !listing.EffectiveInterval.Contains(decisionAt) ||
                actor.State != "ACTIVE" || Expired(actor, currentPolicy, decisionAt) || !methodCurrent ||
                !MethodFactsCurrentAt(actor, currentPolicy, decisionAt))
                throw new InvalidOperationException("Current session listing authority is unavailable.");
            SessionListingContinuation? next = hasMore && items.Count != 0
                ? new(caller.AccountId, caller.SessionId, caller.Origin, items[^1].AuthenticatedAt, items[^1].SessionReference) : null;
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new(items.AsReadOnly(), hasMore, next);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }

    private static SessionListingState ListingState(string state) => state switch
    {
        "ACTIVE" => SessionListingState.Active, "REVOKED" => SessionListingState.Revoked,
        "EXPIRED" => SessionListingState.Expired, _ => throw new InvalidOperationException("Unsupported session lifecycle state.")
    };
}
