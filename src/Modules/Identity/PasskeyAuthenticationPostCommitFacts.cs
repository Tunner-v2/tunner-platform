using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Historical primary-factor/rejection witnesses only. No current eligibility or completed MFA is inferred.</summary>
internal static class PasskeyAuthenticationPostCommitFacts
{
    internal static async Task<IdentityPostCommitOwnerFacts?> ReadAsync(IdentityPersistenceDbContext db, PendingOutboxMessage e,
        PasskeySecurityIntent intent, PasskeyCeremonyRow? row, long version, CancellationToken ct)
    {
        if (e.Source != "tunner/identity" || e.EnvelopeVersion != "1.0" || e.EventSchemaVersion != "1" || e.CausationEventId is not null ||
            row is null || row.Version < version || version < 2 || row.EndedAt is not { } ended || ended < row.CreatedAt || ended > e.OccurredAtUtc ||
            intent.AuthenticatorReference != row.CredentialReference ||
            e.Subject != (row.Kind == "REVOKE" ? row.CredentialReference : row.CeremonyId)?.ToString("N") ||
            e.OperationId != (row.Kind == "STEP_UP" ? row.BoundOperationId : "OP-PASSKEY-" + row.CeremonyId.ToString("N")) ||
            e.CorrelationId != (row.Kind == "STEP_UP" ? row.BoundCorrelationId : row.CeremonyId.ToString("N")) ||
            intent.Operation != (row.Kind switch { "ENROLL" => "REGISTER_PASSKEY", "REVOKE" => "REVOKE_PASSKEY", "STEP_UP" => row.BoundOperation, _ => "AUTHENTICATE_PASSKEY" })) return null;
        var committed = await db.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == e.EventId, ct).ConfigureAwait(false);
        if (committed is null || IdentityPostCommitProcessor.Project(committed) != e) return null;
        var references = new Dictionary<string, string>(StringComparer.Ordinal) { ["ceremonyReference"] = row.CeremonyId.ToString("N"), ["operation"] = intent.Operation };
        if (row.CredentialReference is { } credentialId)
        {
            var credential = await db.Set<PasskeyCredentialRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CredentialReference == credentialId, ct).ConfigureAwait(false);
            if (credential is null || row.AccountId is not null && credential.AccountId != row.AccountId) return null;
            references.Add("authenticatorReference", credentialId.ToString("N"));
        }
        string? actor = null; Guid? sessionId = null;
        if (e.EventType == "PasskeyAuthenticated")
        {
            if (row.Kind != "SIGN_IN" || row.State != "COMPLETED" || version != 2 || row.AccountId is null || row.CredentialReference is null) return null;
            actor = row.AccountId; sessionId = row.ResultSessionId;
            references.Add("primaryFactorVerified", "true");
            references.Add("sessionIssued", sessionId is null ? "false" : "true");
            if (sessionId is null)
            {
                var pending = await db.Set<TotpPendingAuthenticationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.BrowserDigest == row.BrowserDigest && x.RequestId == row.RequestId, ct).ConfigureAwait(false);
                if (pending is null || pending.AccountId != row.AccountId || pending.Origin != row.Origin || pending.PrimaryMethod != "passkey" ||
                    pending.PasskeyCredentialReference != row.CredentialReference || pending.PasskeyPolicyVersion != row.PolicyVersion) return null;
                references.Add("originalPendingMfaReference", pending.PendingId.ToString("N"));
            }
        }
        else
        {
            if (row.State is not ("REJECTED" or "EXPIRED") || row.Kind is not ("SIGN_IN" or "ENROLL" or "REVOKE" or "STEP_UP" or "RECOVERY")) return null;
            // A discovered/target Account is not proof of a successful anonymous assertion.
            if (row.AccountId is not null) references.Add("targetAccountReference", row.AccountId);
            if (row.Kind is ("ENROLL" or "REVOKE" or "STEP_UP") && row.SessionId is not null) { actor = row.AccountId; sessionId = row.SessionId; }
        }
        if (sessionId is { } actualSession)
        {
            var session = await db.Set<AuthenticationSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == actualSession, ct).ConfigureAwait(false);
            if (session is null || actor is null || session.AccountId != actor || session.Origin != row.Origin || session.Scheme != AuthenticationIdentity.Scheme ||
                e.EventType == "PasskeyAuthenticated" && session.PasskeyCredentialReference != row.CredentialReference) return null;
        }
        return new(e.EventType == "PasskeyAuthenticated" ? "PASSKEY_PRIMARY_AUTHENTICATION" : "PASSKEY_AUTHENTICATION_REJECTION",
            e.Subject, actor, sessionId?.ToString("N"), row.PolicyVersion, row.ProcessingReference, row.RetentionReference, references);
    }
}
