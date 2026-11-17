using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Finite private owner tests only. Never output a code, credential, cookie, hash or restricted row.</summary>
// No xUnit/default-fixture dispatch; the existing literal registry supplies the admitted finite owner.
internal sealed partial class RecoveryPasswordRepairPrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Public = "SYNTHETIC-TEST-ONLY";
    private const string Replacement = "a synthetic replacement passphrase for recovery";
    private const string SecondReplacement = "a second synthetic recovery passphrase for original context";
    private const string OriginalPointerCookie = "__Host-Tunner.Recovery.Original";
    private static readonly string[] Unresolved = ["ACCESS_RESTORATION", "NOTIFICATION_DELIVERY"];
    private static readonly bool[] FailureModes = [false, true];
    private static readonly string[] ForbiddenSelectors = ["accountId", "sessionId", "setReference"];
    private static readonly string[] ValidationChanges = ["short", "long", "confirmation", "blocklist", "malformed", "version"];
    private static readonly string[] AuthorityChanges = ["policy", "hierarchy", "selection", "stronger", "blocklist-await", "cookie", "credential"];
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private sealed class Authority : IRecoveryPasswordPolicySource, IRecoveryCaseHierarchySource, IRegistrationPasswordBlocklist
    {
        internal RecoveryPasswordPolicy Policy { get; set; } = new("fixture-password-recovery-v1", Origin, "fixture-repair-password",
            15, 128, 10_000, RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), 32, 100, 100,
            Unresolved, "fixture-processing", "fixture-retention");
        internal Guid SetReference { get; set; }
        internal Guid? ForcedSelection { get; set; }
        internal bool WrongSelectionLifecycle { get; set; }
        internal bool Blocked { get; set; }
        internal bool Allowed { get; set; } = true;
        internal bool StrongUnavailable { get; set; } = true;
        internal string Evidence { get; set; } = "fixture-qualified-lost-strong-authenticator";
        internal Action? BeforePolicy { get; set; }
        internal Action? BeforeHierarchy { get; set; }
        internal Func<CancellationToken, Task>? BeforeBlocklist { get; set; }
        internal int HierarchyReads { get; private set; }
        private static EffectivePolicyInterval Interval => RecoveryCodePrivateIntegrationTests.Profile.Interval;
        public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
        {
            BeforePolicy?.Invoke();
            return new(new(Policy.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", Interval, null, [Public]), Policy);
        }
        public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at)
        {
            HierarchyReads++; BeforeHierarchy?.Invoke();
            var selected = request.Facts.CodeSets?.SingleOrDefault(x => x.SetReference == SetReference && x.State == "ACTIVE");
            return new(Allowed, StrongUnavailable, "fixture-repair-hierarchy-v1", Evidence, request.Facts.Digest(), Interval)
            {
                CodeSelection = selected is null ? null : new(ForcedSelection ?? selected.SetReference, selected.LifecycleVersion + (WrongSelectionLifecycle ? 1 : 0),
                    Policy.RequireExactCodeUsageVersion ? selected.UsageVersion : null)
            };
        }
        public async ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken)
        {
            if (BeforeBlocklist is { } action) await action(cancellationToken);
            return Blocked;
        }
        internal void Reset()
        {
            Policy = Policy with { Version = "fixture-password-recovery-v1" }; Blocked = false; Allowed = true; StrongUnavailable = true;
            Evidence = "fixture-qualified-lost-strong-authenticator"; ForcedSelection = null; WrongSelectionLifecycle = false; BeforePolicy = null; BeforeHierarchy = null; BeforeBlocklist = null;
        }
        public override string ToString() => nameof(Authority);
    }

    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, SaveChangesInterceptor? observer = null,
        Action<IServiceCollection>? configure = null)
    {
        var codes = new RecoveryCodePrivateIntegrationTests.Profile();
        return Start(db, services =>
        {
            services.AddSingleton(codes.Crypto); services.AddSingleton<IRecoveryCodePolicySource>(codes);
            services.AddSingleton<IRecoveryCodeMutationAdmissionSource>(codes); services.AddSingleton<IRecoveryCodePresentationSource>(codes);
            services.AddSingleton<IAssurancePolicySource>(codes); services.AddSingleton<IStepUpPresentationSource>(codes);
            if (observer is not null) services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(observer));
            configure?.Invoke(services);
        });
    }
    private static RecoveryCaseApplication Application(IServiceProvider services, Authority authority) => new(
        services.GetRequiredService<IdentityPersistenceDbContext>(), services.GetRequiredService<RecoveryCodeApplication>(),
        services.GetRequiredService<AuthenticationApplication>(), services.GetRequiredService<TimeProvider>(), hierarchy: authority,
        passwordPolicies: authority, passwordBlocklist: authority,
        authenticationPolicies: services.GetRequiredService<IAuthenticationPolicySource>(), partitions: services.GetRequiredService<AuthenticationPartitionKey>());

    private sealed class Seed(string account, HistoricalSession first, HistoricalSession second, Guid factor, Guid set,
        RecoveryCodePreparedSet prepared) : IDisposable
    {
        internal string Account { get; } = account;
        internal HistoricalSession First { get; } = first;
        internal HistoricalSession Second { get; } = second;
        internal Guid Factor { get; } = factor;
        internal Guid Set { get; } = set;
        internal RecoveryCodeValue Code => prepared.Entries[0].Code;
        internal RecoveryCodeValue SecondCode => prepared.Entries[1].Code;
        internal RecoveryEntryCaller Caller { get; } = new(AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N")),
            AuthenticationIdentity.Digest(first.Cookies[(first.Cookies.IndexOf('=') + 1)..]), Origin, new('D', 64));
        public void Dispose() => prepared.Dispose();
        public override string ToString() => nameof(Seed);
    }
    private static async Task<Seed> SeedAsync(IdentityTestDatabase db, AuthenticationTestHost host, Authority authority, bool minor = false)
    {
        var account = await host.SeedAsync(db, minor: minor);
        var first = await host.SeedHistoricalPasswordSessionAsync(db, account); var second = await host.SeedHistoricalPasswordSessionAsync(db, account);
        var reference = Guid.NewGuid(); var factor = Guid.NewGuid(); var crypto = host.Services.GetRequiredService<RecoveryCodeCryptoProfile>();
        var prepared = host.Services.GetRequiredService<RecoveryCodeNative>().Generate(account, reference);
        try
        {
            var now = host.Clock.GetUtcNow(); await using var context = db.Context();
            // Same explicit issuance-lineage fixture as009/011 containment; real native verifiers, no fabricated proof acceptance.
            var operation = new RecoveryCodeOperationRow
            {
                OperationId = Guid.NewGuid(), RequestId = Guid.NewGuid(), SetReference = reference, AccountId = account, SessionId = first.SessionId,
                Origin = Origin, Kind = "GENERATE", State = "PENDING_PROOF", ExpectedVersion = 1,
                ContextDigest = new('A', 64), PriorSetPlanDigest = new('A', 64), ImpactFactsDigest = new('A', 64), PolicyVersion = Public, PolicyDigest = new('A', 64),
                AuthenticationPolicyVersion = host.Policies.Policy.Version, SecurityStampDigest = first.SecurityStampDigest,
                CryptoProfileVersion = crypto.Version, FormatProfileVersion = "NATIVE_HEX32_V1", DeliveryProfileVersion = Public,
                CodesPerSet = prepared.Entries.Count, MaximumCodesPerSet = prepared.Entries.Count, RetirementPlanJson = "[]", PublishedImpactKey = Public,
                PublicationDigest = new('A', 64), PublicationVersion = Public, PublicationFrom = now, PublicationIncludesFrom = true,
                PublicationUntil = now.AddMinutes(5), AdmissionPolicyVersion = Public, AdmissionEvidenceReference = Public,
                AdmissionFrom = now, AdmissionIncludesFrom = true, AdmissionUntil = now.AddMinutes(5), AssurancePolicyVersion = Public,
                CreatedAt = now, ExpiresAt = now.AddMinutes(5), ResultRecoveryExpiresAt = now.AddMinutes(10), ProcessingReference = Public, RetentionReference = Public
            };
            context.Add(operation); await context.SaveChangesAsync();
            context.Add(new RecoveryCodeSetRow { AccountId = account, SetReference = reference, IssuanceOperationId = operation.OperationId,
                State = "ACTIVE", CreatedAt = now, PolicyVersion = Public, CryptoProfileVersion = crypto.Version, ProcessingReference = Public, RetentionReference = Public });
            context.AddRange(prepared.Entries.Select(entry => new RecoveryCodeEntryRow { AccountId = account, SetReference = reference,
                EntryReference = entry.Reference, Verifier = entry.Verifier, CryptoProfileVersion = crypto.Version }));
            // Public metadata for a separate active factor; no secret value/store is created or read by these tests.
            context.Add(new TotpAuthenticatorRow { AuthenticatorReference = factor, AccountId = account, Generation = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(),
                SecretStoreReference = "fixture-unavailable-vault", SecretResourceReference = "fixture-factor-" + factor.ToString("N"), SecretVersion = 1,
                SecretBindingVersion = Public, State = "ACTIVE", CreatedAt = now, ActivatedAt = now,
                PolicyVersion = Public, ProcessingReference = Public, RetentionReference = Public });
            await context.SaveChangesAsync(); authority.SetReference = reference;
            return new(account, first, second, factor, reference, prepared);
        }
        catch { prepared.Dispose(); throw; }
    }

    private static async Task<RecoveryEntryResult> Begin(AuthenticationTestHost host, Authority authority, RecoveryEntryCaller caller,
        Guid request, string email = "person@example.test")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var result = await Application(scope.ServiceProvider, authority).StartPasswordEntryAsync(caller, request, email, authority.Policy.GoalReference);
        Assert.True(result.ErrorCode is null && result.Entry is { State: "OPEN", Version: 1 } && result.Result is null);
        return result;
    }
    private static async Task<RecoveryEntryResult> Read(AuthenticationTestHost host, Authority authority, RecoveryEntryCaller caller, Guid request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await Application(scope.ServiceProvider, authority).ReadPasswordEntryAsync(caller, request);
    }
    private static async Task<RecoveryEntryResult> Repair(AuthenticationTestHost host, Authority authority, RecoveryEntryCaller caller,
        Guid request, long version, string? code, string? password = Replacement, string? confirmation = Replacement, CancellationToken ct = default)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await Application(scope.ServiceProvider, authority).RepairPasswordAsync(caller, request, version, code, password, confirmation, ct);
    }
    private static async Task Unspent(IdentityTestDatabase db, Seed seed, string? expectedHash = null, string? expectedStamp = null)
    {
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryCodeEntryRow>().Where(x => x.SetReference == seed.Set).AllAsync(x => x.ConsumedAt == null && x.Version == 1));
        Assert.True(await check.Set<RecoveryCodeSetRow>().Where(x => x.SetReference == seed.Set).AllAsync(x => x.State == "ACTIVE" && x.LifecycleVersion == 1 && x.UsageVersion == 0));
        Assert.True(!await check.Set<RecoveryResultRow>().AnyAsync() && !await check.Set<OutboxMessage>().AnyAsync());
        Assert.True(await check.Set<RecoveryCaseRow>().AllAsync(x => x.State == "PENDING_CODE" && x.Version == 1 && x.ResultId == null));
        Assert.True(await check.Set<AuthenticationSessionRow>().AllAsync(x => x.State == "ACTIVE" && x.Version == 1));
        var user = await check.Users.SingleAsync(x => x.AccountId == seed.Account);
        if (expectedHash is not null) Assert.True(user.PasswordHash == expectedHash);
        if (expectedStamp is not null) Assert.True(user.SecurityStamp == expectedStamp);
    }

    internal async Task NeutralEntryOriginalAndCancellationNeverSelectPublicAccountOrConsumeCode()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db); using var seed = await SeedAsync(db, host, authority);
        var knownRequest = Guid.NewGuid(); var unknownRequest = Guid.NewGuid();
        var known = await Begin(host, authority, seed.Caller, knownRequest);
        var unknown = await Begin(host, authority, seed.Caller, unknownRequest, "absent@example.test");
        Assert.True(known.Case is not null && unknown.Case is null && known.Entry!.State == unknown.Entry!.State && known.ErrorCode == unknown.ErrorCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var app = Application(scope.ServiceProvider, authority);
            var duplicate = await app.StartPasswordEntryAsync(seed.Caller, knownRequest, "PERSON@EXAMPLE.TEST", authority.Policy.GoalReference);
            Assert.True(duplicate.ErrorCode is null && duplicate.Entry?.EntryId == known.Entry!.EntryId);
            Assert.True((await app.StartPasswordEntryAsync(seed.Caller, knownRequest, "absent@example.test", authority.Policy.GoalReference)).ErrorCode is not null);
            Assert.True((await app.StartPasswordEntryAsync(seed.Caller, Guid.NewGuid(), "person@example.test", "unqualified-goal")).ErrorCode is not null);
            Assert.True((await app.ReadPasswordEntryAsync(seed.Caller, Guid.NewGuid())).ErrorCode is not null);
        }
        var substitutions = new[] { seed.Caller with { BrowserDigest = new('E', 64) }, seed.Caller with { PreviousCookieDigest = new('E', 64) },
            seed.Caller with { NetworkDigest = new('E', 64) }, seed.Caller with { Origin = "https://foreign.example.test" } };
        foreach (var caller in substitutions)
        {
            var read = await Read(host, authority, caller, knownRequest);
            Assert.True(read.ErrorCode is not null && read.Entry is null && read.Case is null && read.Result is null);
            var refused = false;
            try { refused = (await Repair(host, authority, caller, knownRequest, 1, seed.Code.NativeInput())).ErrorCode is not null; }
            catch (InvalidOperationException error) when (caller.Origin == "https://foreign.example.test" && error.Message == "Current recovery password authority is unavailable.") { refused = true; }
            Assert.True(refused);
        }
        foreach (var selector in ForbiddenSelectors)
        {
            var input = new Dictionary<string, object> { ["requestId"] = Guid.NewGuid(), ["email"] = "person@example.test", ["goalReference"] = authority.Policy.GoalReference, [selector] = Guid.NewGuid() };
            var rejected = false;
            try { _ = JsonSerializer.Deserialize<RecoveryEntryStartCommand>(JsonSerializer.Serialize(input), WireJson); }
            catch (JsonException) { rejected = true; }
            Assert.True(rejected);
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var app = Application(scope.ServiceProvider, authority);
            foreach (var request in new[] { knownRequest, unknownRequest })
            {
                var cancelled = await app.CancelPasswordEntryAsync(seed.Caller, request);
                var repeated = await app.CancelPasswordEntryAsync(seed.Caller, request);
                Assert.True(cancelled.ErrorCode is null && cancelled.Entry is { State: "CANCELLED", Version: 2 } && cancelled.Result is null);
                Assert.True(repeated.ErrorCode is null && repeated.Entry?.EntryId == cancelled.Entry!.EntryId);
                Assert.True((await app.RepairPasswordAsync(seed.Caller, request, 2, seed.Code.NativeInput(), Replacement, Replacement)).ErrorCode is not null);
            }
        }
        await Unspent(db, seed);
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryEntryRow>().CountAsync() == 2 && await check.Set<RecoveryCaseRow>().CountAsync() == 1);
        Assert.True(await check.Accounts.CountAsync() == 1 && await check.Users.CountAsync() == 1 && await check.VerifiedContacts.CountAsync() == 1);
    }

    internal async Task ExactCodeRepairsNativePasswordWithoutLoginOrUnrelatedStateMutation()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db); using var seed = await SeedAsync(db, host, authority, minor: true);
        authority.Policy = authority.Policy with { RequireExactCodeUsageVersion = true };
        var foreign = await host.SeedAsync(db, foreignEmail: true); var foreignSession = await host.SeedHistoricalPasswordSessionAsync(db, foreign);
        string priorHash; string priorStamp;
        await using (var before = db.Context()) { var user = await before.Users.SingleAsync(x => x.AccountId == seed.Account); priorHash = user.PasswordHash!; priorStamp = user.SecurityStamp!; }
        var request = Guid.NewGuid(); var started = await Begin(host, authority, seed.Caller, request);
        var repaired = await Repair(host, authority, seed.Caller, request, started.Entry!.Version, seed.Code.NativeInput());
        Assert.True(repaired.ErrorCode is null && repaired.Entry is { State: "REPAIRED", Version: 2 } && repaired.Result?.Outcome == "PASSWORD_REPAIRED_NO_SESSION");
        await using (var check = db.Context())
        {
            var user = await check.Users.SingleAsync(x => x.AccountId == seed.Account);
            var native = AuthenticationPasswordWork.Hasher(host.Policies.Policy);
            Assert.True(user.PasswordHash != priorHash && user.SecurityStamp != priorStamp && native.VerifyHashedPassword(user, user.PasswordHash!, Replacement) != PasswordVerificationResult.Failed);
            Assert.True(native.VerifyHashedPassword(user, user.PasswordHash!, Password) == PasswordVerificationResult.Failed);
            Assert.True(await check.Set<AuthenticationSessionRow>().CountAsync() == 3);
            Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Active && x.Version == 1));
            Assert.True(await check.Accounts.AnyAsync(x => x.AccountId == seed.Account && x.AgeEligibilityStatus == "INELIGIBLE" && x.AgePolicyVersion == "fixture-age"));
            Assert.True(await check.VerifiedContacts.AllAsync(x => x.State == ContactVerificationState.Verified && x.Version == 1));
            Assert.True(await check.Set<TotpAuthenticatorRow>().AnyAsync(x => x.AuthenticatorReference == seed.Factor && x.State == "ACTIVE" && x.Version == 1 && x.UsageVersion == 0));
            Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 1);
            Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.State == "ACTIVE" && x.LifecycleVersion == 1 && x.UsageVersion == 1));
            var result = repaired.Result!;
            Assert.True(result.TargetSessionId is null && result.SessionOutboxEventId is null && result.RepairOutboxEventId is not null && result.SessionEffect == nameof(RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions));
            Assert.True(result.PriorPasswordDigest == AuthenticationIdentity.Digest(priorHash) && result.RepairedPasswordDigest == AuthenticationIdentity.Digest(user.PasswordHash!));
            Assert.True(result.PriorSecurityStampDigest == AuthenticationIdentity.Digest(priorStamp) && result.RepairedSecurityStampDigest == AuthenticationIdentity.Digest(user.SecurityStamp!));
            Assert.True(await check.Set<OutboxMessage>().AnyAsync(x => x.EventId == result.RepairOutboxEventId && x.OperationId == RecoveryCaseApplication.OperationId(repaired.Case!) && x.CorrelationId == request.ToString("N")));
            Assert.True(!await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "RecoveryCompleted"));
        }
        var updateRejected = false;
        await using (var mutation = db.Context())
        {
            try { _ = await mutation.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.recovery_results SET repaired_security_stamp_digest={new string('F', 64)} WHERE result_id={repaired.Result!.ResultId}"); }
            catch (PostgresException error) when (error.SqlState == "23514") { updateRejected = true; }
        }
        var downgradeRejected = false;
        await using (var downgrade = db.Context())
        {
            try { _ = await downgrade.Database.ExecuteSqlRawAsync(RecoveryPasswordMigrationSql.BlockPopulatedDowngrade); }
            catch (PostgresException error) when (error.SqlState == "55000") { downgradeRejected = true; }
        }
        Assert.True(updateRejected && downgradeRejected);
        await using (var preserved = db.Context())
        {
            Assert.True(await preserved.Set<RecoveryCaseRow>().AnyAsync(x => x.CaseId == repaired.Case!.CaseId && x.State == "REPAIRED" && x.ResultId == repaired.Result!.ResultId));
            Assert.True(await preserved.Set<RecoveryResultRow>().AnyAsync(x => x.ResultId == repaired.Result!.ResultId && x.Outcome == "PASSWORD_REPAIRED_NO_SESSION"));
            var user = await preserved.Users.SingleAsync(x => x.AccountId == seed.Account);
            Assert.True(AuthenticationPasswordWork.Hasher(host.Policies.Policy).VerifyHashedPassword(user, user.PasswordHash!, Replacement) != PasswordVerificationResult.Failed);
        }
        var original = await Read(host, authority, seed.Caller, request);
        Assert.True(original.ErrorCode is null && original.Reconciled && original.Result?.ResultId == repaired.Result!.ResultId);
        authority.ForcedSelection = Guid.NewGuid();
        var foreignChoice = await Read(host, authority, seed.Caller, request);
        Assert.True(foreignChoice.ErrorCode is not null && foreignChoice.Result is null);
        authority.Reset(); authority.WrongSelectionLifecycle = true;
        var changedLifecycle = await Read(host, authority, seed.Caller, request);
        Assert.True(changedLifecycle.ErrorCode is not null && changedLifecycle.Result is null);
        authority.Reset();
        Assert.True((await Read(host, authority, seed.Caller, request)).Result?.ResultId == repaired.Result!.ResultId);
        using (var first = await host.Get("/authentication/session", seed.First.Cookies)) Assert.True(first.StatusCode == HttpStatusCode.Unauthorized);
        using (var second = await host.Get("/authentication/session", seed.Second.Cookies)) Assert.True(second.StatusCode == HttpStatusCode.Unauthorized);
        using (var unrelated = await host.Get("/authentication/session", foreignSession.Cookies)) Assert.True(unrelated.StatusCode == HttpStatusCode.OK);
        await using var final = db.Context(); Assert.True(await final.Set<AuthenticationSessionRow>().CountAsync() == 3);
    }

    internal async Task ValidationAndAuthorityDriftLeaveOriginalCodeAndCredentialUnchanged()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db); using var seed = await SeedAsync(db, host, authority);
        string priorHash; string priorStamp;
        await using (var before = db.Context()) { var user = await before.Users.SingleAsync(x => x.AccountId == seed.Account); priorHash = user.PasswordHash!; priorStamp = user.SecurityStamp!; }
        var validationRequest = Guid.NewGuid(); await Begin(host, authority, seed.Caller, validationRequest);
        foreach (var change in ValidationChanges)
        {
            authority.Reset(); authority.Blocked = change == "blocklist";
            var password = change == "short" ? "short" : change == "long" ? new string('X', 129) : Replacement;
            var result = await Repair(host, authority, seed.Caller, validationRequest, change == "version" ? 2 : 1,
                change == "malformed" ? "invalid" : seed.Code.NativeInput(), password, change == "confirmation" ? "different" : password);
            Assert.True(result.ErrorCode is not null && result.Result is null);
            await Unspent(db, seed, priorHash, priorStamp);
        }
        foreach (var change in AuthorityChanges)
        {
            authority.Reset(); var request = Guid.NewGuid(); await Begin(host, authority, seed.Caller, request);
            await using var scope = host.Services.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            var lateObserved = false;
            if (change == "policy") authority.BeforePolicy = () =>
            {
                if (!database.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added)) return;
                lateObserved = true; authority.Policy = authority.Policy with { Version = "fixture-password-recovery-v2" };
            };
            if (change is "hierarchy" or "selection") authority.BeforeHierarchy = () =>
            {
                if (!database.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added)) return;
                lateObserved = true;
                if (change == "selection") authority.ForcedSelection = Guid.NewGuid(); else authority.Evidence = "fixture-hierarchy-withdrawal";
            };
            if (change == "stronger") authority.StrongUnavailable = false;
            if (change == "blocklist-await") authority.BeforeBlocklist = async ct => { await Task.Yield(); ct.ThrowIfCancellationRequested(); lateObserved = true; authority.Policy = authority.Policy with { Version = "fixture-password-recovery-v2" }; };
            if (change == "credential")
            {
                await using var drift = db.Context(); var user = await drift.Users.SingleAsync(x => x.AccountId == seed.Account);
                user.PasswordHash = AuthenticationPasswordWork.Hasher(host.Policies.Policy).HashPassword(user, "a separate synthetic credential transition");
                user.SecurityStamp = Guid.NewGuid().ToString("N"); user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                await drift.SaveChangesAsync(); priorHash = user.PasswordHash!; priorStamp = user.SecurityStamp!;
            }
            var caller = change == "cookie" ? seed.Caller with { PreviousCookieDigest = new('E', 64) } : seed.Caller;
            var refused = await Application(scope.ServiceProvider, authority).RepairPasswordAsync(caller, request, 1, seed.Code.NativeInput(), Replacement, Replacement);
            Assert.True(refused.ErrorCode is not null && refused.Result is null && (change is not ("policy" or "hierarchy" or "selection" or "blocklist-await") || lateObserved));
            authority.Reset(); await Unspent(db, seed, priorHash, priorStamp);
        }
    }

    internal async Task CompetingPasswordRepairsConsumeOneCodeAndRecoverOnlyOriginalResult()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db); using var seed = await SeedAsync(db, host, authority);
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); await Begin(host, authority, seed.Caller, first); await Begin(host, authority, seed.Caller, second);
        var results = await Task.WhenAll(Repair(host, authority, seed.Caller, first, 1, seed.Code.NativeInput()), Repair(host, authority, seed.Caller, second, 1, seed.Code.NativeInput()));
        Assert.True(results.Count(x => x.ErrorCode is null && x.Result is not null) == 1 && results.Count(x => x.ErrorCode is not null) == 1);
        var winnerIndex = results[0].ErrorCode is null ? 0 : 1; var winnerRequest = winnerIndex == 0 ? first : second;
        var winner = results[winnerIndex]; var read = await Read(host, authority, seed.Caller, winnerRequest);
        Assert.True(read.ErrorCode is null && read.Reconciled && read.Result?.ResultId == winner.Result!.ResultId);
        var duplicate = await Repair(host, authority, seed.Caller, winnerRequest, 2, null, null, null);
        Assert.True(duplicate.ErrorCode is null && duplicate.Reconciled && duplicate.Result?.ResultId == winner.Result!.ResultId);
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryResultRow>().CountAsync() == 1 && await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 1);
        Assert.True(await check.Set<RecoveryEntryRow>().CountAsync(x => x.State == "REPAIRED") == 1 && await check.Set<AuthenticationSessionRow>().CountAsync() == 2);
        Assert.True(await check.Set<OutboxMessage>().CountAsync() == 1);
    }

    private sealed class RepairSaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool ProvisionalObserved { get; private set; }
        internal bool SavedObserved { get; private set; }
        internal CancellationTokenSource? Cancellation { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            ProvisionalObserved = Armed && context is not null && context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added && x.Entity.Outcome == "PASSWORD_REPAIRED_NO_SESSION") &&
                context.ChangeTracker.Entries<RecoveryEntryRow>().Any(x => x.Entity.State == "REPAIRED" && x.Entity.Version == 2) &&
                context.ChangeTracker.Entries<RecoveryCodeEntryRow>().Any(x => x.Entity.ConsumedAt != null && x.Entity.Version == 2) &&
                context.ChangeTracker.Entries<IdentityUserAdapter>().Any(x => x.State == EntityState.Modified);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ProvisionalObserved) return ValueTask.FromResult(result);
            SavedObserved = true;
            if (Cancellation is { } cancellation) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("FINITE_PASSWORD_REPAIR_SAVE_FAILURE");
        }
        internal void Reset() { Armed = false; ProvisionalObserved = false; SavedObserved = false; Cancellation = null; }
    }
    internal async Task PostSaveFaultAndCancellationRollbackPasswordEntryCaseCodeResultAndOutbox()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); var observer = new RepairSaveFailure(); await using var host = await Host(db, observer); using var seed = await SeedAsync(db, host, authority);
        string hash; string stamp;
        await using (var before = db.Context()) { var user = await before.Users.SingleAsync(x => x.AccountId == seed.Account); hash = user.PasswordHash!; stamp = user.SecurityStamp!; }
        Guid original = default;
        foreach (var cancel in FailureModes)
        {
            observer.Reset(); var request = Guid.NewGuid(); await Begin(host, authority, seed.Caller, request); if (!cancel) original = request;
            using var cancellation = new CancellationTokenSource(); observer.Armed = true; observer.Cancellation = cancel ? cancellation : null;
            var interrupted = false;
            try { _ = await Repair(host, authority, seed.Caller, request, 1, seed.Code.NativeInput(), ct: cancellation.Token); }
            catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { interrupted = true; }
            catch (InvalidOperationException) when (!cancel && observer.SavedObserved) { interrupted = true; }
            Assert.True(interrupted && observer.ProvisionalObserved && observer.SavedObserved);
            observer.Reset(); await Unspent(db, seed, hash, stamp);
            await using var check = db.Context(); Assert.True(await check.Set<RecoveryEntryRow>().AllAsync(x => x.State == "OPEN" && x.Version == 1));
        }
        var retry = await Repair(host, authority, seed.Caller, original, 1, seed.Code.NativeInput());
        Assert.True(retry.ErrorCode is null && retry.Result?.Outcome == "PASSWORD_REPAIRED_NO_SESSION");
    }

    private sealed class Presentation : IRecoveryPasswordPresentationSource
    {
        private static readonly string[] BaseKeys = ["fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value"];
        public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at)
        {
            var followUps = policy.UnresolvedDownstreamEffects.Select((reference, index) =>
                new RecoveryPasswordFollowUp(reference, "fixture.follow-label-" + index, "fixture.follow-value-" + index)).ToArray();
            var keys = BaseKeys.Concat(followUps.SelectMany(x => new[] { x.LabelKey, x.ValueKey }));
            return new("en", keys.Select(key => new RegistrationPublishedText(key, "fixture-variant", "fixture-text-v1", Public,
                "en", "PUBLISHED", "Synthetic test only")), "fixture-publication-v1", RecoveryCodePrivateIntegrationTests.Profile.Interval,
                "fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value", followUps);
        }
    }
    private static string WithoutCookie(string cookies, string name) => string.Join("; ", cookies.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim()).Where(x => !x.StartsWith(name + "=", StringComparison.Ordinal)));
    private static bool NoNewUserCookie(HttpResponseMessage response) => !response.Headers.TryGetValues("Set-Cookie", out var values) ||
        !values.Any(value => value.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal) &&
            value.Split(';', 2)[0].Length > AuthenticationIdentity.CookieName.Length + 1);

    internal async Task ActualHttpsRecoveryRequiresOriginalBrowserCsrfAndBoundedPublicCommandsWithoutLogin()
    {
        var checkpoint = new RecoveryCodeFirstCaseCheckpoint();
        try
        {
            await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.HOST);
            await using (var absent = await Host(db))
            {
                using var options = await absent.Get("/authentication/options");
                Assert.True(options.StatusCode == HttpStatusCode.OK);
                var original = await options.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(!original.TryGetProperty("recoveryAvailable", out _));
            }
            var authority = new Authority();
            await using var host = await Host(db, configure: services =>
            {
                services.AddSingleton<IRecoveryPasswordPolicySource>(authority); services.AddSingleton<IRecoveryCaseHierarchySource>(authority);
                services.AddSingleton<IRegistrationPasswordBlocklist>(authority); services.AddSingleton<IRecoveryPasswordPresentationSource, Presentation>();
            });
            checkpoint.Mark(RecoveryCodeFirstCaseStage.SEED);
            using var seed = await SeedAsync(db, host, authority);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.OPTIONS_GET);
            using var bootstrap = await host.Get("/authentication/options"); Assert.True(bootstrap.StatusCode == HttpStatusCode.OK);
            var auth = await bootstrap.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(auth.GetProperty("recoveryAvailable").GetBoolean() && NoNewUserCookie(bootstrap));
            var cookies = Merge(null, bootstrap); var csrf = auth.GetProperty("csrfToken").GetString()!;
            using (var options = await host.Get("/recovery/options", cookies))
            {
                Assert.True(options.StatusCode == HttpStatusCode.OK && NoNewUserCookie(options));
                var wire = await options.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(wire.GetProperty("available").GetBoolean() && wire.GetProperty("repair").GetProperty("available").GetBoolean());
                Assert.True(wire.GetProperty("goals")[0].GetProperty("reference").GetString() == authority.Policy.GoalReference);
            }
            checkpoint.Mark(RecoveryCodeFirstCaseStage.START_POST);
            var request = Guid.NewGuid(); var start = new { requestId = request, email = "person@example.test", goalReference = authority.Policy.GoalReference };
            using (var missing = await host.Post("/recovery/start", start, cookies, null)) Assert.True(missing.StatusCode == HttpStatusCode.BadRequest);
            using (var wrong = await host.Post("/recovery/start", start, cookies, "invalid-synthetic-csrf")) Assert.True(wrong.StatusCode == HttpStatusCode.BadRequest);
            using (var origin = await host.Post("/recovery/start", start, cookies, csrf, "https://foreign.example.test")) Assert.True(origin.StatusCode == HttpStatusCode.Forbidden);
            using (var query = await host.Post("/recovery/start?unexpected=true", start, cookies, csrf)) Assert.True(query.StatusCode == HttpStatusCode.BadRequest);
            using (var oversized = await host.Post("/recovery/start", new { requestId = request, email = new string('X', 5000) + "@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf))
                Assert.True(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge);
            using (var missingBrowser = await host.Post("/recovery/start", start, WithoutCookie(cookies, AuthenticationEndpoints.BrowserCookieName), csrf)) Assert.True(missingBrowser.StatusCode == HttpStatusCode.Forbidden);
            foreach (var selector in ForbiddenSelectors)
            {
                var invalid = new Dictionary<string, object> { ["requestId"] = request, ["email"] = "person@example.test", ["goalReference"] = authority.Policy.GoalReference, [selector] = Guid.NewGuid() };
                using var rejected = await host.Post("/recovery/start", invalid, cookies, csrf); Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest);
            }
            await using (var clean = db.Context()) Assert.True(!await clean.Set<RecoveryEntryRow>().AnyAsync() && !await clean.Set<RecoveryCaseRow>().AnyAsync());
            var unknownRequest = Guid.NewGuid();
            using (var unknown = await host.Post("/recovery/start", new { requestId = unknownRequest, email = "absent@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf))
            {
                Assert.True(unknown.StatusCode == HttpStatusCode.OK && NoNewUserCookie(unknown)); cookies = Merge(cookies, unknown);
                var wire = (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation");
                Assert.True(wire.GetProperty("requestId").GetGuid() == unknownRequest && wire.GetProperty("status").GetString() == "AWAITING_PROOF" && !wire.GetProperty("authenticated").GetBoolean());
            }
            string unknownProofError;
            using (var wrongProof = await host.Post("/recovery/repair", new { requestId = unknownRequest, expectedVersion = 1, code = "invalid", password = Replacement, passwordConfirmation = Replacement }, cookies, csrf))
            {
                Assert.True(wrongProof.StatusCode == HttpStatusCode.BadRequest);
                unknownProofError = (await wrongProof.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString()!;
            }
            using (var cancelled = await host.Post("/recovery/cancel", new { requestId = unknownRequest }, cookies, csrf))
            {
                Assert.True(cancelled.StatusCode == HttpStatusCode.OK && NoNewUserCookie(cancelled)); cookies = Merge(cookies, cancelled);
                Assert.True((await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation").GetProperty("status").GetString() == "CANCELLED");
            }
            using (var repeated = await host.Post("/recovery/cancel", new { requestId = unknownRequest }, cookies, csrf)) Assert.True(repeated.StatusCode == HttpStatusCode.OK);
            using (var started = await host.Post("/recovery/start", start, cookies, csrf))
            {
                Assert.True(started.StatusCode == HttpStatusCode.OK && NoNewUserCookie(started)); cookies = Merge(cookies, started);
                var safe = await started.Content.ReadAsStringAsync(); var wire = JsonSerializer.Deserialize<JsonElement>(safe).GetProperty("operation");
                Assert.True(wire.GetProperty("status").GetString() == "AWAITING_PROOF" && wire.GetProperty("requestId").GetGuid() == request && !wire.GetProperty("authenticated").GetBoolean());
                Assert.True(!safe.Contains(seed.Account, StringComparison.Ordinal) && !safe.Contains("person@example.test", StringComparison.OrdinalIgnoreCase));
            }
            checkpoint.Mark(RecoveryCodeFirstCaseStage.PROOF_BINDING);
            var repair = new { requestId = request, expectedVersion = 1, code = seed.Code.NativeInput(), password = Replacement, passwordConfirmation = Replacement };
            using (var wrongProof = await host.Post("/recovery/repair", new { requestId = request, expectedVersion = 1, code = "invalid", password = Replacement, passwordConfirmation = Replacement }, cookies, csrf))
            {
                Assert.True(wrongProof.StatusCode == HttpStatusCode.BadRequest);
                Assert.True((await wrongProof.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString() == unknownProofError && unknownProofError == "RECOVERY_PROOF_REJECTED");
            }
            using (var originalId = await host.Post("/recovery/result", new { requestId = Guid.NewGuid() }, cookies, csrf)) Assert.True(originalId.StatusCode == HttpStatusCode.NotFound);
            using (var repairId = await host.Post("/recovery/repair", new { requestId = Guid.NewGuid(), expectedVersion = 1, code = seed.Code.NativeInput(), password = Replacement, passwordConfirmation = Replacement }, cookies, csrf)) Assert.True(repairId.StatusCode == HttpStatusCode.NotFound);
            using (var oversized = await host.Post("/recovery/repair", new { requestId = request, expectedVersion = 1, code = seed.Code.NativeInput(), password = new string('X', 7000), passwordConfirmation = new string('X', 7000) }, cookies, csrf)) Assert.True(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge);
            var foreign = await host.Options();
            var pointer = cookies.Split(';').Select(x => x.Trim()).Single(x => x.StartsWith(OriginalPointerCookie + "=", StringComparison.Ordinal));
            using (var other = await host.Post("/recovery/result", new { requestId = request }, foreign.Cookies, foreign.Csrf)) Assert.True(other.StatusCode == HttpStatusCode.NotFound);
            using (var borrowed = await host.Post("/recovery/repair", repair, foreign.Cookies + "; " + pointer, foreign.Csrf)) Assert.True(borrowed.StatusCode == HttpStatusCode.NotFound);
            var lostPointer = WithoutCookie(cookies, OriginalPointerCookie);
            using (var missing = await host.Post("/recovery/repair", repair, lostPointer, csrf)) Assert.True(missing.StatusCode == HttpStatusCode.NotFound);
            using (var restored = await host.Post("/recovery/result", new { requestId = request }, lostPointer, csrf))
            {
                Assert.True(restored.StatusCode == HttpStatusCode.OK && NoNewUserCookie(restored)); cookies = Merge(lostPointer, restored);
                Assert.True((await restored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation").GetProperty("requestId").GetGuid() == request);
            }
            await Unspent(db, seed);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.PASSWORD_PROOF_POST);
            using (var completed = await host.Post("/recovery/repair", repair, cookies, csrf))
            {
                Assert.True(completed.StatusCode == HttpStatusCode.OK && NoNewUserCookie(completed)); cookies = Merge(cookies, completed);
                var safe = await completed.Content.ReadAsStringAsync(); var operation = JsonSerializer.Deserialize<JsonElement>(safe).GetProperty("operation");
                Assert.True(operation.GetProperty("status").GetString() == "PASSWORD_REPAIRED_NO_SESSION" && !operation.GetProperty("authenticated").GetBoolean());
                Assert.True(!safe.Contains(Replacement, StringComparison.Ordinal) && !safe.Contains(seed.Code.NativeInput(), StringComparison.Ordinal));
            }
            checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
            using (var original = await host.Post("/recovery/result", new { requestId = request }, cookies, csrf))
            {
                Assert.True(original.StatusCode == HttpStatusCode.OK && NoNewUserCookie(original));
                Assert.True((await original.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation").GetProperty("requestId").GetGuid() == request);
            }
            using (var session = await host.Get("/authentication/session", cookies)) Assert.True(session.StatusCode == HttpStatusCode.Unauthorized);
            await using (var firstEffect = db.Context()) Assert.True(await firstEffect.Set<AuthenticationSessionRow>().CountAsync() == 2);
            // A separately seeded legitimate current session is test setup, never a grant inferred from recovery.
            checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
            var current = await host.SeedHistoricalPasswordSessionAsync(db, seed.Account);
            using (var authenticated = await host.Get("/authentication/session", current.Cookies)) Assert.True(authenticated.StatusCode == HttpStatusCode.OK);
            var signedBrowser = await host.Options(current.Cookies); var signedCookies = signedBrowser.Cookies; var signedCsrf = signedBrowser.Csrf; var signedRequest = Guid.NewGuid();
            using (var started = await host.Post("/recovery/start", new { requestId = signedRequest, email = "person@example.test", goalReference = authority.Policy.GoalReference }, signedCookies, signedCsrf))
            {
                Assert.True(started.StatusCode == HttpStatusCode.OK && NoNewUserCookie(started)); signedCookies = Merge(signedCookies, started);
            }
            using (var repaired = await host.Post("/recovery/repair", new { requestId = signedRequest, expectedVersion = 1, code = seed.SecondCode.NativeInput(), password = SecondReplacement, passwordConfirmation = SecondReplacement }, signedCookies, signedCsrf))
            {
                Assert.True(repaired.StatusCode == HttpStatusCode.OK && NoNewUserCookie(repaired)); signedCookies = Merge(signedCookies, repaired);
                Assert.True(!(await repaired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation").GetProperty("authenticated").GetBoolean());
            }
            var refreshed = await host.Options(signedCookies); signedCookies = refreshed.Cookies; signedCsrf = refreshed.Csrf;
            using (var original = await host.Post("/recovery/result", new { requestId = signedRequest }, signedCookies, signedCsrf))
            {
                Assert.True(original.StatusCode == HttpStatusCode.OK && NoNewUserCookie(original)); signedCookies = Merge(signedCookies, original);
                var operation = (await original.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation");
                Assert.True(operation.GetProperty("requestId").GetGuid() == signedRequest && operation.GetProperty("status").GetString() == "PASSWORD_REPAIRED_NO_SESSION" && !operation.GetProperty("authenticated").GetBoolean());
            }
            using (var invalidated = await host.Get("/authentication/session", signedCookies)) { Assert.True(invalidated.StatusCode == HttpStatusCode.Unauthorized); signedCookies = Merge(signedCookies, invalidated); }
            refreshed = await host.Options(signedCookies); signedCookies = refreshed.Cookies; signedCsrf = refreshed.Csrf;
            using (var original = await host.Post("/recovery/result", new { requestId = signedRequest }, signedCookies, signedCsrf)) Assert.True(original.StatusCode == HttpStatusCode.OK && NoNewUserCookie(original));
            checkpoint.Mark(RecoveryCodeFirstCaseStage.ACTIVE_SET_STATES);
            await using var check = db.Context();
            Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 2 && await check.Set<RecoveryResultRow>().CountAsync() == 2);
            Assert.True(await check.Set<AuthenticationSessionRow>().CountAsync() == 3 && !await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "RecoveryCompleted"));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Existing fixed public checkpoint contract only; never retain or forward the underlying exception.
            throw new RecoveryCodeFirstCaseFailure(checkpoint.Phase, checkpoint.Stage);
        }
    }
}
