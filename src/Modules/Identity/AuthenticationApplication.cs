using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tunner.Accounts;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal sealed class AuthenticationPasswordWork
{
    private readonly ConcurrentDictionary<int, Lazy<string>> dummies = new();
    internal static PasswordHasher<IdentityUserAdapter> Hasher(AuthenticationPolicy policy) => new(Options.Create(new PasswordHasherOptions
        { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = policy.PasswordHashIterations }));
    internal string Dummy(AuthenticationPolicy policy) => dummies.GetOrAdd(policy.PasswordHashIterations,
        _ => new Lazy<string>(() => Hasher(policy).HashPassword(new IdentityUserAdapter(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32))))).Value;
}

/// <summary>Identity owner; transactions contain state plus outbox, never HTTP or Product sessions.</summary>
internal sealed class AuthenticationApplication(IdentityPersistenceDbContext database, IAuthenticationPolicySource policies,
    IAccountAuthenticationAccessSource access, AuthenticationPartitionKey partitions, IDataProtectionProvider protection,
    AuthenticationPasswordWork passwordWork, TimeProvider clock) : IAuthenticationSessionMaintenance
{
    private readonly IDataProtector ticketProtection = protection.CreateProtector("Tunner.User.Session.Ticket.v1");
    private readonly IDataProtector keyProtection = protection.CreateProtector("Tunner.User.Session.RetryKey.v1");
    private static readonly UpperInvariantLookupNormalizer Normalizer = new();

    internal async Task<AuthenticationAttempt> SignInAsync(AuthenticationCaller caller, PasswordSignInCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(command); caller.Validate();
        var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        if (command.IdempotencyKey == Guid.Empty || !IdentityEmail.IsValid(command.Email) || string.IsNullOrEmpty(command.Password) ||
            command.Password.EnumerateRunes().Count() > policy.PasswordMaximumLength)
            return new("VALIDATION_ERROR", null);
        var normalized = Normalizer.NormalizeEmail(command.Email.Trim())!;
        var identifier = partitions.Identifier(normalized);
        var dummy = passwordWork.Dummy(policy); var hasher = AuthenticationPasswordWork.Hasher(policy);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await AllowAsync("N:" + caller.NetworkDigest, policy.NetworkAttemptLimit, policy, now, ct).ConfigureAwait(false) ||
            !await AllowAsync("I:" + identifier, policy.IdentifierAttemptLimit, policy, now, ct).ConfigureAwait(false))
        {
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new("RATE_OR_ABUSE_LIMITED", null);
        }
        await LockAsync("auth-browser:" + caller.BrowserDigest, ct).ConfigureAwait(false);
        var prior = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.IdempotencyKey == command.IdempotencyKey, ct).ConfigureAwait(false);
        var user = await (from contact in database.VerifiedContacts join candidate in database.Users on contact.AccountId equals candidate.AccountId
            where contact.NormalizedEmail == normalized && contact.AnonymizedAtUtc == null && contact.State == ContactVerificationState.Verified &&
                candidate.EmailConfirmed && candidate.NormalizedEmail == normalized select candidate).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        PasswordVerificationResult verification;
        try { verification = hasher.VerifyHashedPassword(user ?? new IdentityUserAdapter(), user?.PasswordHash ?? dummy, command.Password); }
        catch (FormatException) { verification = PasswordVerificationResult.Failed; }
        if (user is null || verification == PasswordVerificationResult.Failed || string.IsNullOrWhiteSpace(user.SecurityStamp))
            return await RejectAsync(transaction, now, ct).ConfigureAwait(false);
        var account = await database.Accounts.SingleAsync(x => x.AccountId == user.AccountId, ct).ConfigureAwait(false);
        var decision = await AccessAsync(account, "SIGN_IN", now, ct).ConfigureAwait(false);
        if (decision is null || !decision.Allowed) return await RejectAsync(transaction, now, ct).ConfigureAwait(false);
        if (prior is not null)
        {
            if (prior.IdentifierDigest != identifier || prior.AccountId != user.AccountId || prior.RememberMe != command.RememberMe)
            {
                await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
                return new("VERSION_CONFLICT", null);
            }
            await LockSessionAsync(prior.SessionId, ct).ConfigureAwait(false);
            await database.Entry(prior).ReloadAsync(ct).ConfigureAwait(false);
            var valid = await ValidateAsync(prior, caller.Origin, now, ct).ConfigureAwait(false);
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
            return valid is null ? new("AUTHENTICATION_FAILED", null) : new(null, new AuthenticationGrant(keyProtection.Unprotect(prior.ProtectedKey), valid));
        }
        if (caller.PreviousSessionId is { } previous)
        {
            await LockSessionAsync(previous, ct).ConfigureAwait(false);
            var old = await database.Set<AuthenticationSessionRow>().SingleOrDefaultAsync(x => x.SessionId == previous && x.Origin == caller.Origin && x.Scheme == AuthenticationIdentity.Scheme, ct).ConfigureAwait(false);
            if (old is not null) Revoke(old, now);
        }
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, command.Password); user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
        var sessionId = Guid.NewGuid(); var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var absolute = command.RememberMe ? policy.RememberAbsoluteLifetime : policy.AbsoluteLifetime;
        var idle = command.RememberMe ? policy.RememberIdleLifetime : policy.IdleLifetime;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.AccountId), new Claim(AuthenticationIdentity.SessionClaim, sessionId.ToString("N")),
            new Claim(AuthenticationIdentity.OriginClaim, caller.Origin), new Claim("amr", "pwd"), new Claim("acr", policy.PasswordAssurance)
        ], AuthenticationIdentity.Scheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + absolute,
            IsPersistent = command.RememberMe, AllowRefresh = false }, AuthenticationIdentity.Scheme);
        var row = new AuthenticationSessionRow
        {
            SessionId = sessionId, KeyDigest = AuthenticationIdentity.Digest(key), ProtectedKey = keyProtection.Protect(key),
            ProtectedTicket = ticketProtection.Protect(Convert.ToBase64String(TicketSerializer.Default.Serialize(ticket))),
            BrowserDigest = caller.BrowserDigest, IdempotencyKey = command.IdempotencyKey, IdentifierDigest = identifier, RememberMe = command.RememberMe,
            AccountId = user.AccountId, SecurityStampDigest = AuthenticationIdentity.Digest(user.SecurityStamp), Scheme = AuthenticationIdentity.Scheme,
            Origin = caller.Origin, AuthenticationMethods = "pwd", Assurance = policy.PasswordAssurance, AuthenticatedAt = ticket.Properties.IssuedUtc!.Value, LastSeenAt = ticket.Properties.IssuedUtc.Value,
            ExpiresAt = ticket.Properties.ExpiresUtc!.Value, IdleTicks = idle.Ticks, PolicyVersion = policy.Version, AccessPolicyVersion = decision.PolicyVersion,
            AccessEvidenceReference = decision.EvidenceReference, ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference,
            OperationId = "OP-" + Guid.NewGuid().ToString("N"), CorrelationId = Guid.NewGuid().ToString("N"), Version = 1
        };
        database.Add(row); Event(row, "TunnerSessionAuthenticated", now);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new(null, new AuthenticationGrant(key, ticket));
    }

    internal async Task<AuthenticationTicket?> RetrieveAsync(string key, string origin, CancellationToken ct)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return null;
        var digest = AuthenticationIdentity.Digest(key);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var id = await database.Set<AuthenticationSessionRow>().Where(x => x.KeyDigest == digest).Select(x => (Guid?)x.SessionId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is null) return null;
        await LockSessionAsync(id.Value, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == id, ct).ConfigureAwait(false);
        var ticket = await ValidateAsync(row, origin, clock.GetUtcNow(), ct).ConfigureAwait(false);
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return ticket;
    }

    internal async Task RemoveAsync(string key, string origin, CancellationToken ct)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return;
        var digest = AuthenticationIdentity.Digest(key);
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var id = await database.Set<AuthenticationSessionRow>().Where(x => x.KeyDigest == digest && x.Origin == origin && x.Scheme == AuthenticationIdentity.Scheme).Select(x => (Guid?)x.SessionId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is null) return;
        await LockSessionAsync(id.Value, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == id, ct).ConfigureAwait(false);
        Revoke(row, clock.GetUtcNow());
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ExpireSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        // Bounded technical batch. The worker may repeat until zero; this is not a policy lifetime or retention default.
        var candidates = await database.Set<AuthenticationSessionRow>().FromSqlInterpolated($"""
            SELECT * FROM identity.tunner_sessions WHERE state='ACTIVE' AND (
              expires_at <= {now} OR last_seen_at + idle_ticks * interval '1 microsecond' / 10 <= {now} OR
              last_seen_at + CASE WHEN remember_me THEN {policy.RememberIdleLifetime} ELSE {policy.IdleLifetime} END <= {now} OR
              authenticated_at + CASE WHEN remember_me THEN {policy.RememberAbsoluteLifetime} ELSE {policy.AbsoluteLifetime} END <= {now})
            ORDER BY authenticated_at, session_id LIMIT 100
            """).AsNoTracking().Select(x => x.SessionId).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var ended = 0;
        foreach (var id in candidates)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await LockSessionAsync(id, cancellationToken).ConfigureAwait(false);
            var row = await database.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == id, cancellationToken).ConfigureAwait(false);
            // A previous iteration may have tracked a row; always reload after acquiring its lifecycle lock.
            await database.Entry(row).ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (row.State == "ACTIVE" && Expired(row, policy, now))
            {
                End(row, "EXPIRED", "TunnerSessionExpired", now); ended++;
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        return ended;
    }
    public Task<int> PurgeExpiredAbuseAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return database.Set<AuthenticationAbuseRow>().Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }
    private static bool Expired(AuthenticationSessionRow row, AuthenticationPolicy policy, DateTimeOffset now)
    {
        var absolute = row.RememberMe ? policy.RememberAbsoluteLifetime : policy.AbsoluteLifetime;
        var idle = row.RememberMe ? policy.RememberIdleLifetime : policy.IdleLifetime;
        return now < row.AuthenticatedAt || now < row.LastSeenAt || now >= row.ExpiresAt || now - row.AuthenticatedAt >= absolute ||
            now - row.LastSeenAt >= TimeSpan.FromTicks(Math.Min(row.IdleTicks, idle.Ticks));
    }

    private async Task<AuthenticationTicket?> ValidateAsync(AuthenticationSessionRow row, string origin, DateTimeOffset now, CancellationToken ct)
    {
        if (row.State != "ACTIVE" || row.Scheme != AuthenticationIdentity.Scheme || row.Origin != origin) return null;
        var policy = policies.RequireEffective(now); policy.Validate();
        if (Expired(row, policy, now))
        { End(row, "EXPIRED", "TunnerSessionExpired", now); return null; }
        var account = await database.Accounts.SingleAsync(x => x.AccountId == row.AccountId, ct).ConfigureAwait(false);
        var user = await database.Users.SingleOrDefaultAsync(x => x.AccountId == row.AccountId, ct).ConfigureAwait(false);
        if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            AuthenticationIdentity.Digest(user.SecurityStamp) != row.SecurityStampDigest || row.Assurance != policy.PasswordAssurance ||
            !await database.VerifiedContacts.AnyAsync(x => x.AccountId == row.AccountId && x.State == ContactVerificationState.Verified &&
                x.AnonymizedAtUtc == null && x.NormalizedEmail == user.NormalizedEmail, ct).ConfigureAwait(false))
        { Revoke(row, now); return null; }
        var decision = await AccessAsync(account, "SESSION_USE", now, ct).ConfigureAwait(false);
        if (decision is null || !decision.Allowed) { Revoke(row, now); return null; }
        var ticket = TicketSerializer.Default.Deserialize(Convert.FromBase64String(ticketProtection.Unprotect(row.ProtectedTicket)));
        if (ticket is null || ticket.AuthenticationScheme != row.Scheme || AuthenticationIdentity.SessionId(ticket.Principal) != row.SessionId ||
            ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier) != row.AccountId || ticket.Principal.FindFirstValue(AuthenticationIdentity.OriginClaim) != row.Origin ||
            ticket.Properties.IssuedUtc != row.AuthenticatedAt || ticket.Properties.ExpiresUtc != row.ExpiresAt)
        { Revoke(row, now); return null; }
        row.LastSeenAt = now; row.AccessPolicyVersion = decision.PolicyVersion; row.AccessEvidenceReference = decision.EvidenceReference; row.Version++;
        return ticket;
    }
    private async ValueTask<AccountAuthenticationAccessDecision?> AccessAsync(AccountRow account, string operation, DateTimeOffset now, CancellationToken ct)
    {
        if (account.State is AccountState.Closed or AccountState.VerificationPending) return null;
        var decision = await access.RequireAsync(new(account.AccountId, account.State, account.Version, operation, now), ct).ConfigureAwait(false);
        if (decision.AccountId != account.AccountId || decision.StateVersion != account.Version || decision.Operation != operation ||
            string.IsNullOrWhiteSpace(decision.PolicyVersion) || string.IsNullOrWhiteSpace(decision.EvidenceReference) || !decision.EffectiveInterval.Contains(now))
            throw new InvalidOperationException("Current Account authentication authority is unavailable.");
        return decision;
    }
    private async Task<bool> AllowAsync(string partition, int limit, AuthenticationPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        var window = now.UtcTicks / policy.AbuseWindow.Ticks;
        await LockAsync("auth-abuse:" + partition, ct).ConfigureAwait(false);
        var row = await database.Set<AuthenticationAbuseRow>().SingleOrDefaultAsync(x => x.Partition == partition && x.PolicyVersion == policy.Version && x.Window == window, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new AuthenticationAbuseRow { Partition = partition, PolicyVersion = policy.Version, Window = window,
                ExpiresAt = new DateTimeOffset(checked((window + 1) * policy.AbuseWindow.Ticks), TimeSpan.Zero) };
            database.Add(row);
        }
        if (row.Count >= limit) return false;
        row.Count++; return true;
    }
    private async Task<AuthenticationAttempt> RejectAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, DateTimeOffset now, CancellationToken ct)
    {
        var id = Guid.NewGuid(); var operation = "OP-" + id.ToString("N");
        database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", "AuthenticationRejected", "1", now, "tunner/identity", id.ToString("N"),
            "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = operation }), "1", operation, Guid.NewGuid().ToString("N")));
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new("AUTHENTICATION_FAILED", null);
    }
    private void Revoke(AuthenticationSessionRow row, DateTimeOffset now) => End(row, "REVOKED", "TunnerSessionRevoked", now);
    private void End(AuthenticationSessionRow row, string state, string eventType, DateTimeOffset now)
    {
        if (row.State != "ACTIVE") return;
        row.State = state; row.RevokedAt = now; row.OperationId = "OP-" + Guid.NewGuid().ToString("N"); row.CorrelationId = Guid.NewGuid().ToString("N"); row.ProtectedKey = string.Empty; row.ProtectedTicket = string.Empty; row.Version++;
        Event(row, eventType, now);
    }
    private void Event(AuthenticationSessionRow row, string type, DateTimeOffset now) => database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", now,
        "tunner/identity", row.SessionId.ToString("N"), "RESTRICTED_SECURITY", JsonSerializer.Serialize(new { reference = row.SessionId.ToString("N") }),
        row.Version.ToString(CultureInfo.InvariantCulture), row.OperationId, row.CorrelationId));
    private Task<int> LockAsync(string resource, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", ct);
    private Task<int> LockSessionAsync(Guid id, CancellationToken ct) => LockAsync("auth-session:" + id.ToString("N"), ct);
}
