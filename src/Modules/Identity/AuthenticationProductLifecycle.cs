using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    /// <summary>Read owner facts under the canonical locks. Never validate a browser ticket, touch activity or end/revive a session here.</summary>
    internal async Task<ProductAuthorizationLifecycleActorFacts> ReadProductLifecycleFactsAsync(ProductAuthorizationLineage original,
        ProductAuthorizationLifecycleOperation operation, CancellationToken ct)
    {
        RequireProductTransaction();
        await LockSessionAsync(original.SessionId, ct).ConfigureAwait(false);
        var session = await database.Set<AuthenticationSessionRow>().FromSqlInterpolated(
            $"SELECT * FROM identity.tunner_sessions WHERE session_id={original.SessionId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (session is not null) await database.Entry(session).ReloadAsync(ct).ConfigureAwait(false);
        var account = await database.Accounts.FromSqlInterpolated(
            $"SELECT * FROM accounts.accounts WHERE account_id={original.AccountId} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is not null) await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.FromSqlInterpolated(
            $"""SELECT * FROM identity."AspNetUsers" WHERE "AccountId"={original.AccountId} FOR NO KEY UPDATE""").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is not null) await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        var contacts = await database.VerifiedContacts.FromSqlInterpolated(
            $"SELECT * FROM accounts.verified_contacts WHERE account_id={original.AccountId} ORDER BY contact_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);

        // Client-owned artifact revocation is not a request to use the Account/browser session.
        // Missing/denied Account authentication policy must not become a revocation prerequisite.
        var evaluateAccountAccess = operation != ProductAuthorizationLifecycleOperation.Revocation;
        var accessDecision = account is not null && evaluateAccountAccess
            ? await AccessAsync(account, ProductAuthorizationLifecycleApplication.AccountOperation(operation), clock.GetUtcNow(), ct).ConfigureAwait(false)
            : null;
        var accountFacts = new ProductAuthorizationAccountFacts(original.AccountId, account is not null, account?.State.ToString(),
            account?.Version, evaluateAccountAccess, accessDecision);
        if (session is null) return new(accountFacts, null);

        var binding = session.AccountId == original.AccountId && session.Origin == original.Origin && session.Scheme == AuthenticationIdentity.Scheme;
        var stamp = binding && user is not null && !string.IsNullOrWhiteSpace(user.SecurityStamp) &&
            AuthenticationIdentity.Digest(user.SecurityStamp) == session.SecurityStampDigest;
        var contact = binding && user is { EmailConfirmed: true } && contacts.Any(x => x.State == ContactVerificationState.Verified &&
            x.AnonymizedAtUtc is null && x.NormalizedEmail == user.NormalizedEmail);
        AuthenticationPolicy? policy = null; bool? method = null;
        if (evaluateAccountAccess)
        {
            policy = policies.RequireEffective(clock.GetUtcNow()); policy.Validate();
            method = binding && await MethodIsCurrentAsync(session, policy, clock.GetUtcNow(), ct).ConfigureAwait(false);
        }
        var at = clock.GetUtcNow();
        if (policy is not null)
        {
            var current = policies.RequireEffective(at); current.Validate();
            if (current != policy) throw new InvalidOperationException("Authentication policy changed during lifecycle fact observation.");
            method = method == true && MethodFactsCurrentAt(session, current, at);
        }
        return new(accountFacts, new(session.SessionId, session.AccountId, session.Origin, session.Scheme, session.State, session.Version,
            session.AuthenticatedAt, session.LastSeenAt, session.ExpiresAt, policy is null ? null : ProductSessionDeadline(session, policy),
            session.AuthenticationMethods, session.Assurance,
            policy?.Version, policy is null ? null : AuthenticationIdentity.Digest(JsonSerializer.Serialize(policy)), binding,
            policy is null ? null : Expired(session, policy, at), stamp, contact, method));
    }

    private static DateTimeOffset ProductSessionDeadline(AuthenticationSessionRow session, AuthenticationPolicy policy)
    {
        var absolute = session.RememberMe ? policy.RememberAbsoluteLifetime : policy.AbsoluteLifetime;
        var idle = session.RememberMe ? policy.RememberIdleLifetime : policy.IdleLifetime;
        return new[] { session.ExpiresAt, session.AuthenticatedAt + absolute,
            session.LastSeenAt + TimeSpan.FromTicks(Math.Min(session.IdleTicks, idle.Ticks)) }.Min();
    }
}
