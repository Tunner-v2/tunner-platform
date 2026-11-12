using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    /// <summary>Exact already-locked recovery owner target. No caller/session authentication is manufactured.</summary>
    internal Guid ContainRecoveryCaseSession(RecoveryCaseRow recovery, AuthenticationSessionRow session, DateTimeOffset at)
    {
        if (database.Database.CurrentTransaction is null || database.Entry(session).State != EntityState.Unchanged ||
            recovery.State != "PENDING_CODE" || recovery.Goal != nameof(RecoveryCaseGoal.ContainSelectedTunnerSession) ||
            recovery.SessionEffect != nameof(RecoveryCaseSessionEffect.RevokeExactSelectedSession) ||
            session.SessionId != recovery.TargetSessionId || session.AccountId != recovery.AccountId || session.Origin != recovery.Origin ||
            session.Scheme != AuthenticationIdentity.Scheme || session.State != "ACTIVE" || session.Version != recovery.ExpectedSessionVersion ||
            at < recovery.CreatedAt || at >= recovery.ExpiresAt || at < session.AuthenticatedAt || at >= session.ExpiresAt)
            throw new InvalidOperationException("A current exact recovery session effect is required.");
        End(session, "REVOKED", "TunnerSessionRevoked", at, RecoveryCaseApplication.OperationId(recovery), recovery.RequestId.ToString("N"));
        return database.ChangeTracker.Entries<OutboxMessage>().Single(x => x.State == EntityState.Added &&
            x.Entity.EventType == "TunnerSessionRevoked" && x.Entity.OperationId == RecoveryCaseApplication.OperationId(recovery) &&
            x.Entity.CorrelationId == recovery.RequestId.ToString("N")).Entity.EventId;
    }
}
