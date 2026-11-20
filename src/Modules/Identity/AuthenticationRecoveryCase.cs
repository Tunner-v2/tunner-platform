using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    internal void ApplyRecoveryPassword(RecoveryCaseRow recovery, IdentityUserAdapter user, string preparedNativeHash)
    {
        if (database.Database.CurrentTransaction is null || database.Entry(user).State != EntityState.Unchanged ||
            recovery.Goal != nameof(RecoveryCaseGoal.RepairPassword) || recovery.State is not ("PENDING_CODE" or "PENDING_PASSKEY" or "PENDING_CONTACT_RISK" or "PENDING_GOVERNED_REVIEW") ||
            recovery.SessionEffect != nameof(RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions) ||
            user.AccountId != recovery.AccountId || user.PasswordHash is null || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            string.IsNullOrWhiteSpace(preparedNativeHash) || AuthenticationIdentity.Digest(user.PasswordHash) != recovery.PriorPasswordDigest ||
            AuthenticationIdentity.Digest(user.SecurityStamp) != recovery.PriorSecurityStampDigest || RecoveryCaseApplication.UserSourceDigest(user) != recovery.UserSourceDigest)
            throw new InvalidOperationException("An exact current recovery credential transition is required.");
        // Native IdentityV3 hash is prepared before locks. Stamp invalidates every existing Tunner-session source,
        // without creating a session, changing unrelated factors, or silently selecting a Product-token policy.
        user.PasswordHash = preparedNativeHash; user.SecurityStamp = Guid.NewGuid().ToString("N"); user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
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
