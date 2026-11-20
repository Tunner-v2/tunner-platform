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

/// <summary>Actual private owner transactions; never output codes, verifiers, credentials, tickets or cookies.</summary>
// No xUnit/default-fixture dispatch. The existing fixed private registry supplies the finite database owner.
internal sealed class RecoveryCasePrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Public = "SYNTHETIC-TEST-ONLY";
    private static readonly string[] Unresolved = ["CREDENTIAL_REPAIR", "ACCESS_RESTORATION", "NOTIFICATION_DELIVERY"];
    private static readonly string[] AuthorityChanges = ["unknown", "stronger", "denied", "facts", "policy", "hierarchy"];
    private static readonly bool[] FailureModes = [false, true];

    private sealed class Authority : IRecoveryCasePolicySource, IRecoveryCaseHierarchySource
    {
        internal RecoveryCasePolicy Policy { get; set; } = new("fixture-case-v1", Origin,
            RecoveryCaseGoal.ContainSelectedTunnerSession, RecoveryCaseSessionEffect.RevokeExactSelectedSession,
            RecoveryCodeUsageAdmission.IndependentUnusedEntry, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), 16,
            Unresolved, "fixture-processing", "fixture-retention");
        internal bool Unknown { get; set; }
        internal bool Allowed { get; set; } = true;
        internal bool StrongUnavailable { get; set; } = true;
        internal bool WrongFacts { get; set; }
        internal string Evidence { get; set; } = "fixture-explicit-stronger-unavailability";
        internal Action? BeforePolicy { get; set; }
        internal Action? BeforeHierarchy { get; set; }
        internal int HierarchyReads { get; private set; }
        private static EffectivePolicyInterval Interval => RecoveryCodePrivateIntegrationTests.Profile.Interval;
        public RecoveryCasePolicyVersion RequireEffective(DateTimeOffset at)
        {
            BeforePolicy?.Invoke();
            return new(new(Policy.Version, "ACTIVE", "ACCOUNT_RECOVERY", Interval, null, [Public]), Policy);
        }
        public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at)
        {
            HierarchyReads++; BeforeHierarchy?.Invoke();
            if (Unknown) throw new InvalidOperationException("FINITE_HIERARCHY_UNAVAILABLE");
            return new(Allowed, StrongUnavailable, "fixture-hierarchy-v1", Evidence,
                WrongFacts ? new('0', 64) : request.Facts.Digest(), Interval);
        }
        internal void Reset()
        {
            Unknown = false; Allowed = true; StrongUnavailable = true; WrongFacts = false;
            Evidence = "fixture-explicit-stronger-unavailability"; BeforePolicy = null; BeforeHierarchy = null;
            Policy = Policy with { Version = "fixture-case-v1" };
        }
        public override string ToString() => nameof(Authority);
    }

    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, RecoveryCodePrivateIntegrationTests.Profile codes,
        SaveChangesInterceptor? observer = null) => Start(db, services =>
    {
        services.AddSingleton(codes.Crypto); services.AddSingleton<IRecoveryCodePolicySource>(codes);
        services.AddSingleton<IRecoveryCodeMutationAdmissionSource>(codes); services.AddSingleton<IRecoveryCodePresentationSource>(codes);
        services.AddSingleton<IAssurancePolicySource>(codes); services.AddSingleton<IStepUpPresentationSource>(codes);
        if (observer is not null) services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(observer));
    });
    private static RecoveryCaseApplication Application(IServiceProvider services, Authority authority) => new(
        services.GetRequiredService<IdentityPersistenceDbContext>(), services.GetRequiredService<RecoveryCodeApplication>(),
        services.GetRequiredService<AuthenticationApplication>(), services.GetRequiredService<TimeProvider>(), authority, authority);

    private sealed class Seed(string account, Guid target, Guid other, Guid set, RecoveryCodePreparedSet prepared) : IDisposable
    {
        internal string Account { get; } = account;
        internal Guid Target { get; } = target;
        internal Guid Other { get; } = other;
        internal Guid Set { get; } = set;
        internal RecoveryCodeValue Code => prepared.Entries[0].Code;
        internal RecoveryCaseCaller Caller { get; } = new(account, new('C', 64), Origin, new('D', 64));
        internal RecoveryCaseStartCommand Command(Guid? target = null) => new(Guid.NewGuid(), target ?? Target, 1, 1, Set, 1, null);
        public void Dispose() => prepared.Dispose();
        public override string ToString() => nameof(Seed);
    }

    private static async Task<Seed> SeedAsync(IdentityTestDatabase db, AuthenticationTestHost host, bool minor = false)
    {
        var account = await host.SeedAsync(db, minor: minor);
        var target = await host.SeedHistoricalPasswordSessionAsync(db, account);
        var other = await host.SeedHistoricalPasswordSessionAsync(db, account);
        var reference = Guid.NewGuid();
        var crypto = host.Services.GetRequiredService<RecoveryCodeCryptoProfile>();
        var prepared = host.Services.GetRequiredService<RecoveryCodeNative>().Generate(account, reference);
        try
        {
            // Existing SQL fixture's issuance-lineage seed, with actual native owner-bound verifiers.
            // Issuance/proof acceptance remains covered by the original009 cases, not fabricated by this seed.
            var now = host.Clock.GetUtcNow();
            await using var context = db.Context();
            var operation = new RecoveryCodeOperationRow
            {
                OperationId = Guid.NewGuid(), RequestId = Guid.NewGuid(), SetReference = reference, AccountId = account, SessionId = target.SessionId,
                Origin = Origin, Kind = "GENERATE", State = "PENDING_PROOF", ExpectedVersion = 1,
                ContextDigest = new('A', 64), PriorSetPlanDigest = new('A', 64), ImpactFactsDigest = new('A', 64), PolicyVersion = Public, PolicyDigest = new('A', 64),
                AuthenticationPolicyVersion = host.Policies.Policy.Version, SecurityStampDigest = target.SecurityStampDigest,
                CryptoProfileVersion = crypto.Version, FormatProfileVersion = "NATIVE_HEX32_V1", DeliveryProfileVersion = Public,
                CodesPerSet = prepared.Entries.Count, MaximumCodesPerSet = prepared.Entries.Count, RetirementPlanJson = "[]", PublishedImpactKey = Public,
                PublicationDigest = new('A', 64), PublicationVersion = Public, PublicationFrom = now, PublicationIncludesFrom = true,
                PublicationUntil = now.AddMinutes(5), AdmissionPolicyVersion = Public, AdmissionEvidenceReference = Public,
                AdmissionFrom = now, AdmissionIncludesFrom = true, AdmissionUntil = now.AddMinutes(5), AssurancePolicyVersion = Public,
                CreatedAt = now, ExpiresAt = now.AddMinutes(5), ResultRecoveryExpiresAt = now.AddMinutes(10),
                ProcessingReference = Public, RetentionReference = Public
            };
            context.Add(operation); await context.SaveChangesAsync();
            context.Add(new RecoveryCodeSetRow { AccountId = account, SetReference = reference, IssuanceOperationId = operation.OperationId,
                State = "ACTIVE", CreatedAt = now, PolicyVersion = Public, CryptoProfileVersion = crypto.Version, ProcessingReference = Public, RetentionReference = Public });
            context.AddRange(prepared.Entries.Select(entry => new RecoveryCodeEntryRow { AccountId = account, SetReference = reference,
                EntryReference = entry.Reference, Verifier = entry.Verifier, CryptoProfileVersion = crypto.Version }));
            await context.SaveChangesAsync();
            return new(account, target.SessionId, other.SessionId, reference, prepared);
        }
        catch { prepared.Dispose(); throw; }
    }
    private static async Task<RecoveryCaseResult> Begin(AuthenticationTestHost host, Authority authority, RecoveryCaseCaller caller, RecoveryCaseStartCommand command)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var result = await Application(scope.ServiceProvider, authority).StartAsync(caller, command);
        Assert.True(result.ErrorCode is null && result.Case is { State: "PENDING_CODE", Version: 1 } && result.Result is null);
        return result;
    }
    private static async Task<RecoveryCaseResult> Consume(AuthenticationTestHost host, Authority authority, RecoveryCaseCaller caller,
        Guid request, string? input, CancellationToken ct = default)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await Application(scope.ServiceProvider, authority).ConsumeCodeAsync(caller, request, input, ct);
    }
    private static async Task Untouched(IdentityTestDatabase db, Seed seed)
    {
        await using var check = db.Context();
        Assert.True(await check.Set<AuthenticationSessionRow>().Where(x => x.AccountId == seed.Account).AllAsync(x => x.State == "ACTIVE" && x.Version == 1));
        Assert.True(await check.Set<RecoveryCodeEntryRow>().Where(x => x.SetReference == seed.Set).AllAsync(x => x.ConsumedAt == null && x.Version == 1));
        Assert.True(await check.Set<RecoveryCodeSetRow>().Where(x => x.SetReference == seed.Set).AllAsync(x => x.State == "ACTIVE" && x.LifecycleVersion == 1 && x.UsageVersion == 0));
        Assert.True(!await check.Set<RecoveryResultRow>().AnyAsync() && !await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "TunnerSessionRevoked"));
        Assert.True(await check.Set<RecoveryCaseRow>().AllAsync(x => x.State == "PENDING_CODE" && x.Version == 1 && x.ResultId == null));
    }

    internal async Task OriginalCaseReplayRejectsSubstitutedContextAndRecoversOnlyExactContainedResult()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db, new()); using var seed = await SeedAsync(db, host);
        authority.Policy = authority.Policy with { UsageAdmission = RecoveryCodeUsageAdmission.ExactUsageSnapshot };
        var command = seed.Command() with { ExpectedUsageVersion = 0 }; var first = await Begin(host, authority, seed.Caller, command);
        var duplicate = await Begin(host, authority, seed.Caller, command);
        Assert.True(duplicate.Case!.CaseId == first.Case!.CaseId);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var app = Application(scope.ServiceProvider, authority);
            Assert.True((await app.StartAsync(seed.Caller, command with { TargetSessionId = seed.Other })).ErrorCode is not null);
            var substitutions = new[] { seed.Caller with { OriginalBrowserDigest = new('E', 64) }, seed.Caller with { NetworkDigest = new('E', 64) },
                seed.Caller with { AccountId = "ACC-foreign-synthetic" }, seed.Caller with { Origin = "https://foreign.example.test" } };
            foreach (var caller in substitutions)
            {
                var readRejected = false;
                try
                {
                    var hidden = await app.ReadOriginalAsync(caller, command.RequestId);
                    readRejected = hidden.ErrorCode is not null && hidden.Case is null && hidden.Result is null;
                }
                catch (InvalidOperationException error) when (caller.Origin == "https://foreign.example.test" &&
                    error.Message == "Current recovery authority is unavailable.") { readRejected = true; }
                Assert.True(readRejected);
                Assert.True((await app.ConsumeCodeAsync(caller, command.RequestId, seed.Code.NativeInput())).ErrorCode is not null);
            }
        }
        await Untouched(db, seed);
        var accepted = await Consume(host, authority, seed.Caller, command.RequestId, seed.Code.NativeInput());
        Assert.True(accepted.ErrorCode is null && !accepted.Reconciled && accepted.Result?.Outcome == "CONTAINED_PENDING_RESTORATION");
        var replay = await Consume(host, authority, seed.Caller, command.RequestId, null);
        Assert.True(replay.ErrorCode is null && replay.Reconciled && replay.Result?.ResultId == accepted.Result!.ResultId);
        await using var read = host.Services.CreateAsyncScope();
        var original = await Application(read.ServiceProvider, authority).ReadOriginalAsync(seed.Caller, command.RequestId);
        Assert.True(original.Result?.ResultId == accepted.Result!.ResultId && original.Case?.CaseId == first.Case!.CaseId);
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryCaseRow>().CountAsync() == 1 && await check.Set<RecoveryResultRow>().CountAsync() == 1);
        Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 1);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked") == 1);
    }

    internal async Task CompetingRecoveryCasesCannotConsumeOneCodeOrRevokeTwoSessions()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db, new()); using var seed = await SeedAsync(db, host);
        var first = seed.Command(); var second = seed.Command(seed.Other);
        await Begin(host, authority, seed.Caller, first); await Begin(host, authority, seed.Caller, second);
        var results = await Task.WhenAll(Consume(host, authority, seed.Caller, first.RequestId, seed.Code.NativeInput()),
            Consume(host, authority, seed.Caller, second.RequestId, seed.Code.NativeInput()));
        Assert.True(results.Count(x => x.ErrorCode is null && x.Result is not null) == 1 && results.Count(x => x.ErrorCode is not null) == 1);
        await using var check = db.Context();
        Assert.True(await check.Set<AuthenticationSessionRow>().CountAsync(x => x.State == "REVOKED") == 1);
        Assert.True(await check.Set<AuthenticationSessionRow>().CountAsync(x => x.State == "ACTIVE") == 1);
        Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 1);
        Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.UsageVersion == 1 && x.LifecycleVersion == 1));
        Assert.True(await check.Set<RecoveryResultRow>().CountAsync() == 1 && await check.Set<RecoveryCaseRow>().CountAsync(x => x.State == "CONTAINED") == 1);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked") == 1);
    }

    internal async Task ContainmentPreservesOtherSessionsCredentialsAndMinorAccountWithoutRestoration()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db, new()); using var seed = await SeedAsync(db, host, minor: true);
        var foreign = await host.SeedAsync(db, foreignEmail: true); var foreignSession = await host.SeedHistoricalPasswordSessionAsync(db, foreign);
        string? password; string? stamp;
        await using (var before = db.Context()) { var user = await before.Users.SingleAsync(x => x.AccountId == seed.Account); password = user.PasswordHash; stamp = user.SecurityStamp; }
        var command = seed.Command(); await Begin(host, authority, seed.Caller, command);
        var applied = await Consume(host, authority, seed.Caller, command.RequestId, seed.Code.NativeInput());
        Assert.True(applied.ErrorCode is null && applied.Case is { State: "CONTAINED", Version: 2 } && applied.Result is not null);
        await using var check = db.Context(); var result = await check.Set<RecoveryResultRow>().SingleAsync();
        var target = await check.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == seed.Target);
        Assert.True(target.State == "REVOKED" && target.Version == 2 && target.ProtectedKey.Length == 0 && target.ProtectedTicket.Length == 0);
        Assert.True(await check.Set<AuthenticationSessionRow>().Where(x => x.SessionId == seed.Other || x.SessionId == foreignSession.SessionId).AllAsync(x => x.State == "ACTIVE" && x.Version == 1));
        Assert.True(await check.Set<AuthenticationSessionRow>().CountAsync() == 3);
        Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Active && x.Version == 1));
        Assert.True(await check.Accounts.AnyAsync(x => x.AccountId == seed.Account && x.AgeEligibilityStatus == "INELIGIBLE"));
        var userAfter = await check.Users.SingleAsync(x => x.AccountId == seed.Account);
        Assert.True(userAfter.PasswordHash == password && userAfter.SecurityStamp == stamp && userAfter.EmailConfirmed);
        Assert.True(await check.VerifiedContacts.AllAsync(x => x.State == ContactVerificationState.Verified && x.Version == 1));
        Assert.True(result.Outcome == "CONTAINED_PENDING_RESTORATION" && result.TargetSessionId == seed.Target && result.PriorSessionVersion == 1 && result.EndedSessionVersion == 2);
        var contained = applied.Case!;
        Assert.True(result.UnresolvedEffectsJson == contained.UnresolvedEffectsJson && result.PolicySnapshot == contained.PolicySnapshot && result.ContextDigest == contained.ContextDigest);
        Assert.True(await check.Set<OutboxMessage>().AnyAsync(x => x.EventId == result.SessionOutboxEventId && x.EventType == "TunnerSessionRevoked" &&
            x.OperationId == RecoveryCaseApplication.OperationId(contained) && x.CorrelationId == command.RequestId.ToString("N")));
        Assert.True(!await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "RecoveryCompleted"));
        var downgradeRejected = false;
        await using (var downgrade = db.Context())
        {
            try { _ = await downgrade.Database.ExecuteSqlRawAsync(RecoveryCaseMigrationSql.BlockPopulatedDowngrade); }
            catch (PostgresException error) when (error.SqlState == "23514") { downgradeRejected = true; }
        }
        Assert.True(downgradeRejected);
        var deletionStaged = false; var deletionRejected = false;
        await using (var deletion = db.Context())
        {
            await using var transaction = await deletion.Database.BeginTransactionAsync();
            try
            {
                deletionStaged = await deletion.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.recovery_results WHERE result_id={result.ResultId}") == 1;
                await transaction.CommitAsync();
            }
            catch (PostgresException error) when (error.SqlState == "23503") { deletionRejected = true; }
        }
        Assert.True(deletionStaged && deletionRejected);
        await using var preserved = db.Context();
        Assert.True(await preserved.Set<RecoveryCaseRow>().CountAsync(x => x.CaseId == contained.CaseId && x.State == "CONTAINED" && x.ResultId == result.ResultId) == 1);
        Assert.True(await preserved.Set<RecoveryResultRow>().CountAsync(x => x.ResultId == result.ResultId && x.Outcome == "CONTAINED_PENDING_RESTORATION") == 1);
    }

    internal async Task UnknownHigherTierOrLatePolicyAndHierarchyDriftCannotConsumeOrContain()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); await using var host = await Host(db, new()); using var seed = await SeedAsync(db, host);
        foreach (var change in AuthorityChanges)
        {
            authority.Reset(); var command = seed.Command(); await Begin(host, authority, seed.Caller, command);
            await using var scope = host.Services.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            var lateObserved = false; var reads = authority.HierarchyReads;
            if (change == "unknown") authority.Unknown = true;
            if (change == "stronger") authority.StrongUnavailable = false;
            if (change == "denied") authority.Allowed = false;
            if (change == "facts") authority.WrongFacts = true;
            if (change == "policy") authority.BeforePolicy = () =>
            {
                if (!database.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added)) return;
                lateObserved = true; authority.Policy = authority.Policy with { Version = "fixture-case-v2" };
            };
            if (change == "hierarchy") authority.BeforeHierarchy = () =>
            {
                if (!database.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added)) return;
                lateObserved = true; authority.Evidence = "fixture-hierarchy-withdrawn";
            };
            var rejected = false;
            try { var result = await Application(scope.ServiceProvider, authority).ConsumeCodeAsync(seed.Caller, command.RequestId, seed.Code.NativeInput()); rejected = result.ErrorCode is not null && result.Result is null; }
            catch (InvalidOperationException) when (change is "unknown" or "stronger" or "denied" or "facts") { rejected = true; }
            Assert.True(rejected && authority.HierarchyReads > reads && (change is not ("policy" or "hierarchy") || lateObserved));
            authority.Reset(); await Untouched(db, seed);
        }
    }

    private sealed class OwnerSaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool ProvisionalMutationObserved { get; private set; }
        internal bool SavedMutationObserved { get; private set; }
        internal CancellationTokenSource? Cancellation { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            ProvisionalMutationObserved = Armed && context is not null &&
                context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added) &&
                context.ChangeTracker.Entries<RecoveryCodeEntryRow>().Any(x => x.Entity.ConsumedAt != null && x.Entity.Version == 2) &&
                context.ChangeTracker.Entries<AuthenticationSessionRow>().Any(x => x.Entity.State == "REVOKED" && x.Entity.Version == 2);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ProvisionalMutationObserved) return ValueTask.FromResult(result);
            SavedMutationObserved = true;
            if (Cancellation is { } cancellation) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("FINITE_RECOVERY_OWNER_SAVE_FAILURE");
        }
        internal void Reset() { Armed = false; ProvisionalMutationObserved = false; SavedMutationObserved = false; Cancellation = null; }
    }

    internal async Task SavedOwnerFailureAndCancellationRollbackCodeSessionCaseResultAndOutbox()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var authority = new Authority(); var observer = new OwnerSaveFailure();
        await using var host = await Host(db, new(), observer); using var seed = await SeedAsync(db, host);
        Guid original = default;
        foreach (var cancel in FailureModes)
        {
            observer.Reset(); var command = seed.Command(); await Begin(host, authority, seed.Caller, command);
            if (!cancel) original = command.RequestId;
            using var cancellation = new CancellationTokenSource(); observer.Armed = true; observer.Cancellation = cancel ? cancellation : null;
            var interrupted = false;
            try { _ = await Consume(host, authority, seed.Caller, command.RequestId, seed.Code.NativeInput(), cancellation.Token); }
            catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { interrupted = true; }
            catch (InvalidOperationException) when (!cancel && observer.SavedMutationObserved) { interrupted = true; }
            Assert.True(interrupted && observer.ProvisionalMutationObserved && observer.SavedMutationObserved);
            observer.Reset(); await Untouched(db, seed);
        }
        var retried = await Consume(host, authority, seed.Caller, original, seed.Code.NativeInput());
        Assert.True(retried.ErrorCode is null && retried.Result?.Outcome == "CONTAINED_PENDING_RESTORATION");
    }
}
