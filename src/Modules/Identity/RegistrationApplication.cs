using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Tunner.Accounts;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Identity-owned registration. The caller/context and effective policy are trusted server inputs.</summary>
public sealed class RegistrationApplication(
    IdentityPersistenceDbContext database, IRegistrationPolicySource policies,
    IRegistrationPasswordBlocklist blocklist, IDataProtectionProvider protection,
    IRegistrationNotificationDispatcher notifications, TimeProvider timeProvider) : IRegistrationApplication
{
    private readonly IDataProtector credentialProtection = protection.CreateProtector("Tunner.Identity.Registration.Credential.v1");
    private readonly IDataProtector challengeProtection = protection.CreateProtector("Tunner.Identity.Registration.Challenge.v1");
    private readonly IDataProtector addressProtection = protection.CreateProtector("Tunner.Identity.Registration.Address.v1");
    private static readonly UpperInvariantLookupNormalizer Normalizer = new();

    public async Task<RegistrationResult> FindAsync(RegistrationCaller caller, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller); caller.Validate();
        var now = timeProvider.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await AllowAsync(caller, "STATUS", policy.LookupLimit, policy, now, cancellationToken).ConfigureAwait(false))
            return await CompleteAsync(transaction, Failure(Guid.Empty, "RATE_OR_ABUSE_LIMITED"), null, cancellationToken).ConfigureAwait(false);
        var row = await database.Set<RegistrationOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.IdempotencyKey == idempotencyKey, cancellationToken).ConfigureAwait(false);
        return await CompleteAsync(transaction, row is null ? Failure(Guid.Empty, "NOT_FOUND_OR_NOT_DISCLOSABLE") : Result(row), null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegistrationResult> RegisterAsync(RegistrationCaller caller, RegisterAccountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(command); caller.Validate();
        var now = timeProvider.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await AllowAsync(caller, "REGISTER", policy.RegisterLimit, policy, now, cancellationToken).ConfigureAwait(false))
            return await CompleteAsync(transaction, Failure(Guid.Empty, "RATE_OR_ABUSE_LIMITED"), null, cancellationToken).ConfigureAwait(false);
        await LockAsync("registration:" + caller.BrowserDigest + command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        var prior = await database.Set<RegistrationOperationRow>().SingleOrDefaultAsync(x => x.BrowserDigest == caller.BrowserDigest && x.IdempotencyKey == command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (prior is not null) return await CompleteAsync(transaction, Result(prior), prior, cancellationToken).ConfigureAwait(false);
        if (command.IdempotencyKey == Guid.Empty || !ValidEmail(command.Email) || command.Password is null ||
            command.Password.EnumerateRunes().Count() < policy.PasswordMinimumLength || command.Password.EnumerateRunes().Count() > policy.PasswordMaximumLength ||
            !command.AcknowledgedNotices || command.TermsVersion != policy.TermsVersion || command.PrivacyVersion != policy.PrivacyVersion ||
            await blocklist.IsBlockedAsync(command.Password, cancellationToken).ConfigureAwait(false))
            return await CompleteAsync(transaction, Failure(Guid.Empty, "VALIDATION_ERROR"), null, cancellationToken).ConfigureAwait(false);
        var operation = new RegistrationOperationRow
        {
            OperationId = Guid.NewGuid(), BrowserDigest = caller.BrowserDigest, IdempotencyKey = command.IdempotencyKey,
            CorrelationId = Guid.NewGuid().ToString("N"), NormalizedEmail = Normalizer.NormalizeEmail(command.Email.Trim())!,
            ProtectedAddress = addressProtection.Protect(command.Email.Trim()), PolicyVersion = policy.Version,
            AgePolicyVersion = policy.AgePolicyVersion, TermsVersion = policy.TermsVersion, PrivacyVersion = policy.PrivacyVersion,
            ProcessingReference = policy.ProcessingReference, RetentionReference = policy.RetentionReference,
            CreatedAt = now, ExpiresAt = now + policy.ProofLifetime, ResendAfter = now + policy.ResendCooldown,
            PurgeAfter = now + policy.PendingMaterialRetention, Version = 1,
            AgeEvidenceReference = "registration-age:" + Guid.NewGuid().ToString("N")
        };
        var age = new AgeEligibilityEvaluator(new SnapshotClock(now)).Evaluate(command.DateOfBirth, policy.AgePolicyVersion, operation.AgeEvidenceReference, operation.CorrelationId);
        operation.AgeEvaluatedAt = age.AgeEvaluatedAt;
        database.Add(operation);
        if (age.Status != AgeEligibilityStatus.Eligible)
        {
            operation.Status = "FAILED"; operation.ErrorCode = "AGE_INELIGIBLE"; operation.ProtectedAddress = null; operation.NormalizedEmail = string.Empty;
            return await CompleteAsync(transaction, Result(operation), null, cancellationToken).ConfigureAwait(false);
        }
        // The same expensive framework hashing path runs before existing/new Account lookup.
        var hasher = Hasher(policy);
        var proposedHash = hasher.HashPassword(new IdentityUserAdapter(), command.Password);
        await LockAsync("email:" + operation.NormalizedEmail, cancellationToken).ConfigureAwait(false);
        var candidates = await (from c in database.VerifiedContacts join a in database.Accounts on c.AccountId equals a.AccountId
            where c.NormalizedEmail == operation.NormalizedEmail && c.AnonymizedAtUtc == null &&
                (c.State == ContactVerificationState.Verified || c.State == ContactVerificationState.Unverified && a.State == AccountState.VerificationPending)
            orderby c.State == ContactVerificationState.Verified descending select c).Take(2).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (candidates.Length > 1 && candidates.All(x => x.State != ContactVerificationState.Verified))
        {
            // Existing ambiguous legacy ownership cannot be silently merged or assigned.
            operation.TemplateVersion = policy.ExistingAccountTemplateVersion; operation.DeliveryId = Guid.NewGuid();
            AddEvent(operation, "RegistrationNotificationRequested", operation.DeliveryId.Value.ToString("N"), operation.Version, now);
            return await CompleteAsync(transaction, Result(operation), operation, cancellationToken).ConfigureAwait(false);
        }
        var contact = candidates.FirstOrDefault();
        var account = contact is null ? null : await database.Accounts.SingleAsync(x => x.AccountId == contact.AccountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            account = new AccountRow { AccountId = "ACC-" + Guid.NewGuid().ToString("N"), State = AccountState.VerificationPending, Version = 1,
                RecordedAtUtc = now, OperationId = OperationReference(operation.OperationId), CorrelationId = operation.CorrelationId,
                EvidenceReference = "registration:" + operation.OperationId, AgeEligibilityStatus = "ELIGIBLE", AgeEvaluatedAtUtc = now,
                AgePolicyVersion = policy.AgePolicyVersion, AgeEvidenceReference = operation.AgeEvidenceReference };
            contact = new VerifiedContactRow { ContactId = Guid.NewGuid(), AccountId = account.AccountId,
                NormalizedEmail = operation.NormalizedEmail, State = ContactVerificationState.Unverified, Version = 1,
                EvidenceReference = "registration-contact:" + operation.OperationId };
            database.Add(account); database.Add(contact);
            database.Users.Add(new IdentityUserAdapter { Id = Guid.NewGuid().ToString("N"), AccountId = account.AccountId,
                UserName = account.AccountId, NormalizedUserName = Normalizer.NormalizeName(account.AccountId),
                Email = command.Email.Trim(), NormalizedEmail = operation.NormalizedEmail, EmailConfirmed = false,
                SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N") });
            AddEvent(operation, "AccountRegistered", account.AccountId, account.Version, now);
        }
        if (account.State == AccountState.VerificationPending && contact!.State == ContactVerificationState.Unverified)
        {
            operation.AccountId = account.AccountId; operation.ContactId = contact.ContactId;
            // Never activate a credential supplied by a different pending registration.
            // Each browser operation's proposed credential is bound to its own contact proof.
            operation.ProtectedCredential = credentialProtection.Protect(proposedHash);
            SetChallenge(operation, policy, now);
        }
        else
        {
            operation.TemplateVersion = policy.ExistingAccountTemplateVersion;
            operation.DeliveryId = Guid.NewGuid();
        }
        AddEvent(operation, "RegistrationNotificationRequested", operation.DeliveryId!.Value.ToString("N"), operation.Version, now);
        return await CompleteAsync(transaction, Result(operation), operation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegistrationResult> VerifyAsync(RegistrationCaller caller, VerifyRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(command); caller.Validate();
        var now = timeProvider.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await AllowAsync(caller, "VERIFY", policy.VerifyLimit, policy, now, cancellationToken).ConfigureAwait(false))
            return await CompleteAsync(transaction, Failure(command.OperationId, "RATE_OR_ABUSE_LIMITED"), null, cancellationToken).ConfigureAwait(false);
        await LockAsync("operation:" + command.OperationId, cancellationToken).ConfigureAwait(false);
        var operation = await OwnedAsync(caller, command.OperationId, cancellationToken).ConfigureAwait(false);
        if (operation is null) return await CompleteAsync(transaction, Failure(command.OperationId, "NOT_FOUND_OR_NOT_DISCLOSABLE"), null, cancellationToken).ConfigureAwait(false);
        var replay = await ReplayAsync(operation.OperationId, "VERIFY", command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return await CompleteAsync(transaction, replay, null, cancellationToken).ConfigureAwait(false);
        RegistrationResult result;
        var digest = Digest(operation, command.Code ?? string.Empty);
        var validProof = operation.ProtectedDigest is not null && CryptographicOperations.FixedTimeEquals(digest, challengeProtection.Unprotect(Convert.FromBase64String(operation.ProtectedDigest)));
        if (command.IdempotencyKey == Guid.Empty || operation.Status != "REQUIRES_ACTION" || operation.AccountId is null ||
            operation.PolicyVersion != policy.Version || now >= operation.ExpiresAt || operation.Attempts >= policy.ProofAttempts)
            result = Failure(operation.OperationId, "NOT_FOUND_OR_NOT_DISCLOSABLE", operation);
        else if (command.ExpectedVersion != operation.Version)
            result = Failure(operation.OperationId, "VERSION_CONFLICT", operation);
        else if (!validProof)
        {
            operation.Attempts++; operation.Version++;
            if (operation.Attempts >= policy.ProofAttempts) { operation.ProtectedCode = null; operation.ProtectedDigest = null; }
            result = Failure(operation.OperationId, "NOT_FOUND_OR_NOT_DISCLOSABLE", operation);
        }
        else
        {
            await LockAsync("email:" + operation.NormalizedEmail, cancellationToken).ConfigureAwait(false);
            var account = await database.Accounts.SingleAsync(x => x.AccountId == operation.AccountId, cancellationToken).ConfigureAwait(false);
            var contact = await database.VerifiedContacts.SingleAsync(x => x.ContactId == operation.ContactId, cancellationToken).ConfigureAwait(false);
            var collision = await database.VerifiedContacts.AnyAsync(x => x.AccountId != account.AccountId && x.NormalizedEmail == operation.NormalizedEmail && x.State == ContactVerificationState.Verified && x.AnonymizedAtUtc == null, cancellationToken).ConfigureAwait(false);
            if (account.State != AccountState.VerificationPending || contact.State != ContactVerificationState.Unverified || collision || operation.ProtectedCredential is null)
                result = Failure(operation.OperationId, "NOT_FOUND_OR_NOT_DISCLOSABLE", operation);
            else
            {
                var user = await database.Users.SingleAsync(x => x.AccountId == account.AccountId, cancellationToken).ConfigureAwait(false);
                user.PasswordHash = credentialProtection.Unprotect(operation.ProtectedCredential); user.EmailConfirmed = true;
                user.SecurityStamp = Guid.NewGuid().ToString("N"); user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                account.State = AccountState.Active; account.Version++; account.RecordedAtUtc = now;
                account.OperationId = OperationReference(operation.OperationId); account.CorrelationId = operation.CorrelationId;
                account.EvidenceReference = "contact-proof:" + operation.OperationId;
                account.AgeEligibilityStatus = "ELIGIBLE"; account.AgeEvaluatedAtUtc = operation.AgeEvaluatedAt;
                account.AgePolicyVersion = operation.AgePolicyVersion; account.AgeEvidenceReference = operation.AgeEvidenceReference;
                contact.State = ContactVerificationState.Verified; contact.Version++; contact.EvidenceReference = account.EvidenceReference;
                operation.Status = "SUCCEEDED"; operation.Version++; ClearMaterial(operation);
                AddEvent(operation, "AccountActivated", account.AccountId, account.Version, now);
                result = Result(operation);
            }
        }
        Remember(operation.OperationId, "VERIFY", command.IdempotencyKey, result);
        return await CompleteAsync(transaction, result, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegistrationResult> ResendAsync(RegistrationCaller caller, ResendRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(command); caller.Validate();
        var now = timeProvider.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Validate();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await AllowAsync(caller, "RESEND", policy.ResendRequestLimit, policy, now, cancellationToken).ConfigureAwait(false))
            return await CompleteAsync(transaction, Failure(command.OperationId, "RATE_OR_ABUSE_LIMITED"), null, cancellationToken).ConfigureAwait(false);
        await LockAsync("operation:" + command.OperationId, cancellationToken).ConfigureAwait(false);
        var operation = await OwnedAsync(caller, command.OperationId, cancellationToken).ConfigureAwait(false);
        if (operation is null) return await CompleteAsync(transaction, Failure(command.OperationId, "NOT_FOUND_OR_NOT_DISCLOSABLE"), null, cancellationToken).ConfigureAwait(false);
        var replay = await ReplayAsync(operation.OperationId, "RESEND", command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return await CompleteAsync(transaction, replay, null, cancellationToken).ConfigureAwait(false);
        RegistrationResult result; RegistrationOperationRow? delivery = null;
        if (command.IdempotencyKey == Guid.Empty || operation.Status != "REQUIRES_ACTION" || now >= operation.PurgeAfter || operation.PolicyVersion != policy.Version)
            result = Failure(operation.OperationId, "NOT_FOUND_OR_NOT_DISCLOSABLE", operation);
        else if (command.ExpectedVersion != operation.Version) result = Failure(operation.OperationId, "VERSION_CONFLICT", operation);
        else if (now < operation.ResendAfter || operation.Resends >= policy.ResendLimit) result = Failure(operation.OperationId, "RATE_OR_ABUSE_LIMITED", operation);
        else
        {
            operation.Resends++; operation.Version++;
            if (operation.AccountId is not null) SetChallenge(operation, policy, now);
            else { operation.DeliveryId = Guid.NewGuid(); operation.ExpiresAt = now + policy.ProofLifetime; operation.ResendAfter = now + policy.ResendCooldown; }
            AddEvent(operation, "RegistrationNotificationRequested", operation.DeliveryId!.Value.ToString("N"), operation.Version, now);
            result = Result(operation); delivery = operation;
        }
        Remember(operation.OperationId, "RESEND", command.IdempotencyKey, result);
        return await CompleteAsync(transaction, result, delivery, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PurgeExpiredMaterialAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var now = timeProvider.GetUtcNow(); policies.RequireEffective(now).Validate();
        var ids = await database.Set<RegistrationOperationRow>().AsNoTracking().Where(x => x.PurgeAfter <= now &&
            (x.ProtectedAddress != null || x.ProtectedCredential != null || x.ProtectedCode != null || x.ProtectedDigest != null || x.NormalizedEmail != string.Empty))
            .Select(x => x.OperationId).Take(batchSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var purged = 0;
        foreach (var id in ids)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await LockAsync("operation:" + id, cancellationToken).ConfigureAwait(false);
            var row = await database.Set<RegistrationOperationRow>().SingleAsync(x => x.OperationId == id, cancellationToken).ConfigureAwait(false);
            if (row.PurgeAfter <= now)
            {
                ClearMaterial(row); row.Version++; purged++;
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        var buckets = await database.Set<RegistrationAbuseBucketRow>().AsNoTracking().Where(x => x.ExpiresAt <= now)
            .Take(batchSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        foreach (var bucket in buckets)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await LockAsync("abuse:" + bucket.Partition + bucket.Kind, cancellationToken).ConfigureAwait(false);
            await database.Set<RegistrationAbuseBucketRow>().Where(x => x.Partition == bucket.Partition && x.Kind == bucket.Kind && x.Window == bucket.Window && x.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        return purged;
    }

    /// <summary>Trusted notification consumer retry entry; not an HTTP endpoint. Adapter deduplicates DeliveryId.</summary>
    public async Task<bool> DispatchNotificationAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow(); policies.RequireEffective(now).Validate();
        var row = await database.Set<RegistrationOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.DeliveryId == deliveryId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Status != "REQUIRES_ACTION" || row.ExpiresAt <= now || row.PurgeAfter <= now || row.ProtectedAddress is null) return false;
        await notifications.QueueAsync(new RegistrationNotification(deliveryId, addressProtection.Unprotect(row.ProtectedAddress),
            row.ProtectedCode is null ? null : challengeProtection.Unprotect(row.ProtectedCode), row.TemplateVersion), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> AllowAsync(RegistrationCaller caller, string kind, int limit, RegistrationPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        var window = now.UtcTicks / policy.AbuseWindow.Ticks;
        await LockAsync("abuse:" + caller.NetworkDigest + kind, ct).ConfigureAwait(false);
        var bucket = await database.Set<RegistrationAbuseBucketRow>().SingleOrDefaultAsync(x => x.Partition == caller.NetworkDigest && x.Kind == kind && x.Window == window, ct).ConfigureAwait(false);
        if (bucket is null) { bucket = new RegistrationAbuseBucketRow { Partition = caller.NetworkDigest, Kind = kind, Window = window, ExpiresAt = new DateTimeOffset(checked((window + 1) * policy.AbuseWindow.Ticks), TimeSpan.Zero) }; database.Add(bucket); }
        if (bucket.Count >= limit) return false;
        bucket.Count++; return true;
    }
    private Task<int> LockAsync(string resource, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", ct);
    private Task<RegistrationOperationRow?> OwnedAsync(RegistrationCaller caller, Guid id, CancellationToken ct) =>
        database.Set<RegistrationOperationRow>().SingleOrDefaultAsync(x => x.OperationId == id && x.BrowserDigest == caller.BrowserDigest, ct);
    private async Task<RegistrationResult?> ReplayAsync(Guid operationId, string kind, Guid key, CancellationToken ct)
    {
        var row = await database.Set<RegistrationMutationRow>().SingleOrDefaultAsync(x => x.OperationId == operationId && x.Kind == kind && x.IdempotencyKey == key, ct).ConfigureAwait(false);
        return row is null ? null : JsonSerializer.Deserialize<RegistrationResult>(row.ResultJson);
    }
    private void Remember(Guid id, string kind, Guid key, RegistrationResult result) => database.Add(new RegistrationMutationRow
        { OperationId = id, Kind = kind, IdempotencyKey = key, ResultJson = JsonSerializer.Serialize(result) });
    private void SetChallenge(RegistrationOperationRow row, RegistrationPolicy policy, DateTimeOffset now)
    {
        var bound = (int)Math.Pow(10, policy.ProofDigits);
        var previous = row.ProtectedCode is null ? null : challengeProtection.Unprotect(row.ProtectedCode);
        string code;
        do { code = RandomNumberGenerator.GetInt32(bound).ToString("D" + policy.ProofDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); }
        while (StringComparer.Ordinal.Equals(previous, code));
        row.ProtectedCode = challengeProtection.Protect(code);
        row.ProtectedDigest = Convert.ToBase64String(challengeProtection.Protect(Digest(row, code)));
        row.TemplateVersion = policy.VerificationTemplateVersion; row.DeliveryId = Guid.NewGuid(); row.Attempts = 0;
        row.ExpiresAt = now + policy.ProofLifetime; row.ResendAfter = now + policy.ResendCooldown; row.PurgeAfter = now + policy.PendingMaterialRetention;
    }
    private static byte[] Digest(RegistrationOperationRow row, string code) => SHA256.HashData(Encoding.UTF8.GetBytes(row.OperationId.ToString("N") + ":" + row.PolicyVersion + ":" + code));
    private static PasswordHasher<IdentityUserAdapter> Hasher(RegistrationPolicy policy) => new(Options.Create(new PasswordHasherOptions
        { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = policy.PasswordHashIterations }));
    private static bool ValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 256) return false;
        return MailAddress.TryCreate(email.Trim(), out var parsed) && StringComparer.Ordinal.Equals(parsed.Address, email.Trim());
    }
    private static string OperationReference(Guid id) => "OP-" + id.ToString("N");
    private static RegistrationResult Result(RegistrationOperationRow row) => new(row.OperationId, row.CorrelationId, row.Status,
        row.Version, row.ErrorCode, row.Status == "REQUIRES_ACTION" ? row.ExpiresAt : null,
        row.Status == "REQUIRES_ACTION" ? row.ResendAfter : null, row.Status == "SUCCEEDED" ? row.AccountId : null);
    private static RegistrationResult Failure(Guid id, string code, RegistrationOperationRow? row = null) =>
        new(id, row?.CorrelationId ?? Guid.NewGuid().ToString("N"), "FAILED", row?.Version ?? 0, code, null, null);
    private void AddEvent(RegistrationOperationRow row, string type, string subject, long version, DateTimeOffset now) =>
        database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", type, "1", now, "tunner/identity", subject, "RESTRICTED_SECURITY",
            JsonSerializer.Serialize(new { reference = subject }), version.ToString(CultureInfo.InvariantCulture), OperationReference(row.OperationId), row.CorrelationId));
    private async Task<RegistrationResult> CompleteAsync(IDbContextTransaction transaction, RegistrationResult result, RegistrationOperationRow? delivery, CancellationToken ct)
    {
        await database.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        if (delivery?.DeliveryId is { } id && delivery.ProtectedAddress is not null && delivery.Status == "REQUIRES_ACTION")
        {
            var notice = new RegistrationNotification(id, addressProtection.Unprotect(delivery.ProtectedAddress),
                delivery.ProtectedCode is null ? null : challengeProtection.Unprotect(delivery.ProtectedCode), delivery.TemplateVersion);
            try { await notifications.QueueAsync(notice, ct).ConfigureAwait(false); }
            catch (RegistrationNotificationUnavailableException) { /* Committed reference-only outbox remains the retry authority. */ }
        }
        return result;
    }
    private static void ClearMaterial(RegistrationOperationRow row)
    { row.ProtectedAddress = null; row.ProtectedCredential = null; row.ProtectedCode = null; row.ProtectedDigest = null; row.NormalizedEmail = string.Empty; }
    private sealed class SnapshotClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
public sealed class RegistrationNotificationUnavailableException : Exception
{
    public RegistrationNotificationUnavailableException() : base("Registration notification queue is unavailable.") { }
}
