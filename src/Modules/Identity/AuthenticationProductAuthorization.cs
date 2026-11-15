using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    internal async Task<ProductAuthorizationActor?> LockProductAuthorizationAsync(StepUpCaller caller, string operation, CancellationToken ct)
    {
        RequireProductTransaction();
        var session = await LockForOperationAsync(caller, operation, ct).ConfigureAwait(false);
        return session is null ? null : await ProductActorAsync(session, ct).ConfigureAwait(false);
    }

    // A native validated code supplies the original session identity, never an invented browser principal.
    // Reuse the same current session validator and canonical session -> Account -> Identity/contact locks.
    internal async Task<ProductAuthorizationActor?> LockProductAuthorizationAsync(ProductAuthorizationLineage original, string operation, CancellationToken ct)
    {
        RequireProductTransaction();
        await LockSessionAsync(original.SessionId, ct).ConfigureAwait(false);
        var session = await database.Set<AuthenticationSessionRow>().FromSqlInterpolated(
            $"SELECT * FROM identity.tunner_sessions WHERE session_id={original.SessionId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (session is null) return null;
        await database.Entry(session).ReloadAsync(ct).ConfigureAwait(false);
        if (session.AccountId != original.AccountId || session.Origin != original.Origin || session.Scheme != AuthenticationIdentity.Scheme) return null;
        var account = await database.Accounts.FromSqlInterpolated(
            $"SELECT * FROM accounts.accounts WHERE account_id={original.AccountId} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null) return null;
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.FromSqlInterpolated(
            $"""SELECT * FROM identity."AspNetUsers" WHERE "AccountId"={original.AccountId} FOR NO KEY UPDATE""").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is not null) await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        _ = await database.VerifiedContacts.FromSqlInterpolated(
            $"SELECT * FROM accounts.verified_contacts WHERE account_id={original.AccountId} ORDER BY contact_id FOR SHARE").ToArrayAsync(ct).ConfigureAwait(false);
        if (await ValidateAsync(session, original.Origin, clock.GetUtcNow(), ct).ConfigureAwait(false) is null) return null;
        var decision = await AccessAsync(account, operation, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return decision?.Allowed == true ? await ProductActorAsync(session, ct).ConfigureAwait(false) : null;
    }

    private async Task<ProductAuthorizationActor?> ProductActorAsync(AuthenticationSessionRow session, CancellationToken ct)
    {
        var account = await database.Accounts.SingleAsync(x => x.AccountId == session.AccountId, ct).ConfigureAwait(false);
        var policy = policies.RequireEffective(clock.GetUtcNow()); policy.Validate();
        var use = await AccessAsync(account, "SESSION_USE", clock.GetUtcNow(), ct).ConfigureAwait(false);
        var authorization = await AccessAsync(account, ProductAuthorizationApplication.AuthorizeOperation, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var value = ReadSessionTicket(session)?.Properties.GetString(AuthenticationInstantProperty);
        DateTimeOffset? performedAt = DateTimeOffset.TryParseExact(value, "O", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var instant) && instant.Offset == TimeSpan.Zero &&
            instant == IdentityPersistenceTime.Normalize(instant) && instant.ToUnixTimeSeconds() == session.AuthenticatedAt.ToUnixTimeSeconds()
            ? instant : null;
        return use?.Allowed == true && authorization?.Allowed == true
            ? new ProductAuthorizationActor(session, account.Version, policy, use, authorization) { AuthenticationPerformedAt = performedAt } : null;
    }

    internal async Task<bool> ProductAuthorizationCurrentAsync(ProductAuthorizationActor actor, string operation, CancellationToken ct)
    {
        RequireProductTransaction();
        var session = actor.Session;
        // Refresh external authenticator policy/facts before the final clock sample, under the held owner locks.
        var initial = policies.RequireEffective(clock.GetUtcNow()); initial.Validate();
        var method = await MethodIsCurrentAsync(session, initial, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == session.AccountId, ct).ConfigureAwait(false);
        await database.Entry(account).ReloadAsync(ct).ConfigureAwait(false);
        var user = await database.Users.SingleOrDefaultAsync(x => x.AccountId == session.AccountId, ct).ConfigureAwait(false);
        if (user is null) return false;
        await database.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            AuthenticationIdentity.Digest(user.SecurityStamp) != session.SecurityStampDigest ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == session.AccountId && x.State == ContactVerificationState.Verified &&
                x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false)) return false;
        var use = await AccessAsync(account, "SESSION_USE", clock.GetUtcNow(), ct).ConfigureAwait(false);
        var authorization = await AccessAsync(account, ProductAuthorizationApplication.AuthorizeOperation, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var permission = await AccessAsync(account, operation, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var at = clock.GetUtcNow(); var current = policies.RequireEffective(at); current.Validate();
        return method && initial == current && current == actor.AuthenticationPolicy && account.Version == actor.AccountVersion &&
            session.State == "ACTIVE" && session.Scheme == AuthenticationIdentity.Scheme && !Expired(session, current, at) &&
            MethodFactsCurrentAt(session, current, at) && use?.Allowed == true && permission?.Allowed == true &&
            authorization?.Allowed == true && use.EffectiveInterval.Contains(at) && permission.EffectiveInterval.Contains(at) &&
            authorization.EffectiveInterval.Contains(at) &&
            System.Text.Json.JsonSerializer.Serialize(use) == System.Text.Json.JsonSerializer.Serialize(actor.SessionUse) &&
            System.Text.Json.JsonSerializer.Serialize(authorization) == System.Text.Json.JsonSerializer.Serialize(actor.ProductAuthorization);
    }

    private void RequireProductTransaction()
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native Product authorization requires the caller-owned Identity transaction.");
    }
}
