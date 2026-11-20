using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

// Only the existing literal finite registry may invoke these cases. No xUnit/default fixture dispatch.
// Restricted values remain local; assertion diagnostics contain only booleans.
internal sealed class RecoveryPasskeyRepairPrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Public = "SYNTHETIC-TEST-ONLY";
    private const string Replacement = "a synthetic passkey recovery replacement passphrase";
    private const string Pointer = "__Host-Tunner.Recovery.Original";
    private static readonly string[] Unresolved = ["ACCESS_RESTORATION", "NOTIFICATION_DELIVERY"];
    private static readonly string[] InvalidAssertions = ["signature", "origin", "rp", "challenge", "type", "uv", "up", "userHandle", "counter", "backup"];
    private static readonly string[] AuthorityChanges = ["policy", "hierarchy", "selection", "native-policy", "blocklist-await", "native-source"];
    private static readonly string[] ForbiddenSelectors = ["accountId", "sessionId", "setReference", "credentialReference", "proofKind"];
    private static readonly bool[] FailureModes = [false, true];

    private sealed class Authority : IRecoveryPasswordPolicySource, IRecoveryCaseHierarchySource, IRegistrationPasswordBlocklist, IPasskeyPolicySource
    {
        internal RecoveryPasswordPolicy Policy { get; set; } = new("fixture-strong-repair-v1", Origin, "fixture-strong-repair", 15, 128,
            10_000, RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5), 32, 100, 100, Unresolved, "fixture-processing", "fixture-retention")
        {
            PasskeyRecoveryProfile = new("fixture-recovery-passkey-v1", "fixture-passkey-v1", "fixture-passkey-assurance",
                new Dictionary<string, string> { [PasskeyPolicy.MethodKey(false, false)] = "fixture-passkey-assurance" })
        };
        internal string NativeVersion { get; set; } = "fixture-passkey-v1";
        internal Guid Reference { get; set; }
        internal Guid? ForcedSelection { get; set; }
        internal string Evidence { get; set; } = "fixture-current-strong-proof";
        internal Action? BeforePolicy { get; set; }
        internal Action? BeforeHierarchy { get; set; }
        internal Func<CancellationToken, Task>? BeforeBlocklist { get; set; }
        public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
        {
            BeforePolicy?.Invoke();
            return new(new(Policy.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", RecoveryCodePrivateIntegrationTests.Profile.Interval, null, [Public]), Policy);
        }
        PasskeyPolicy IPasskeyPolicySource.RequireEffective(DateTimeOffset at) => new(NativeVersion, "authentication.example.test", Origin,
            TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), PasskeyRevocationSessionEffect.PreserveExistingSessions,
            "required", new[] { -7 }, 32, 32768, 4, 32, "fixture-enrollment-v1", "fixture-revocation-v1", "fixture-processing", "fixture-retention", "required", "none",
            new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-passkey-assurance",
                [PasskeyPolicy.AssuranceKey(true, false)] = "fixture-syncable-unbacked-assurance", [PasskeyPolicy.AssuranceKey(true, true)] = "fixture-syncable-backed-assurance" });
        public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at)
        {
            BeforeHierarchy?.Invoke(); var selected = request.Facts.Paths.SingleOrDefault(x => x.Reference == Reference && x.State == "ACTIVE");
            return new(false, false, "fixture-strong-hierarchy-v1", Evidence, request.Facts.Digest(), RecoveryCodePrivateIntegrationTests.Profile.Interval)
            { PasskeySelection = selected is null ? null : new(ForcedSelection ?? selected.Reference, selected.Version, Policy.PasskeyRecoveryProfile!.Version) };
        }
        public async ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken)
        { if (BeforeBlocklist is { } action) await action(cancellationToken); return false; }
        internal void Reset()
        {
            Policy = Policy with { Version = "fixture-strong-repair-v1" }; NativeVersion = "fixture-passkey-v1";
            Evidence = "fixture-current-strong-proof"; ForcedSelection = null; BeforePolicy = null; BeforeHierarchy = null; BeforeBlocklist = null;
        }
        public override string ToString() => nameof(Authority);
    }
    private sealed class Presentation : IRecoveryPasswordPresentationSource
    {
        private static readonly string[] BaseKeys = ["fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value"];
        public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at)
        {
            var follow = policy.UnresolvedDownstreamEffects.Select((reference, i) => new RecoveryPasswordFollowUp(reference, "fixture.follow-label-" + i, "fixture.follow-value-" + i)).ToArray();
            var keys = BaseKeys.Concat(follow.SelectMany(x => new[] { x.LabelKey, x.ValueKey }));
            return new("en", keys.Select(key => new RegistrationPublishedText(key, "fixture-variant", "fixture-text-v1", Public, "en", "PUBLISHED", "Synthetic test only")),
                "fixture-publication-v1", RecoveryCodePrivateIntegrationTests.Profile.Interval, "fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value", follow);
        }
    }
    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Authority authority, SaveChangesInterceptor? observer = null) =>
        PasskeyTests.StartRecoveryFixtureAsync(db, services =>
        {
            services.RemoveAll<IPasskeyPolicySource>();
            services.AddSingleton<IPasskeyPolicySource>(authority); services.AddSingleton<IRecoveryPasswordPolicySource>(authority);
            Assert.True(services.Count(x => x.ServiceType == typeof(IPasskeyPolicySource)) == 1);
            services.AddSingleton<IRecoveryCaseHierarchySource>(authority); services.AddSingleton<IRegistrationPasswordBlocklist>(authority);
            services.AddSingleton<IRecoveryPasswordPresentationSource, Presentation>();
            if (observer is not null) services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(observer));
        });
    private static RecoveryCaseApplication Application(IServiceProvider services) => services.GetRequiredService<RecoveryCaseApplication>();
    private static DefaultHttpContext Http(IServiceProvider services)
    {
        var http = new DefaultHttpContext { RequestServices = services }; http.Request.Scheme = "https"; http.Request.Host = new HostString(new Uri(Origin).Host); return http;
    }
    private sealed record Seed(string Account, Guid Reference, Guid Factor, string EnrollmentCookies, RecoveryEntryCaller Caller)
    { public override string ToString() => nameof(Seed); }
    private static async Task<Seed> SeedAsync(IdentityTestDatabase db, AuthenticationTestHost host, Authority authority, PasskeyAuthenticator key, bool counter = false,
        RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.SEED);
        var account = await host.SeedAsync(db, minor: true); var enrolled = await PasskeyTests.SeedRecoveryCredentialAsync(host, key, checkpoint); authority.Reference = enrolled.Reference;
        if (counter) await PasskeyTests.SeedRecoveryCounterAsync(host, key);
        if (checkpoint is not null) checkpoint.Phase = RecoveryCodeFirstCasePhase.SETUP;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.ACTIVE_SET_STATES);
        var factor = Guid.NewGuid(); var now = host.Clock.GetUtcNow(); await using var context = db.Context();
        context.Add(new TotpAuthenticatorRow { AuthenticatorReference = factor, AccountId = account, Generation = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(),
            SecretStoreReference = "fixture-unavailable-vault", SecretResourceReference = "fixture-factor-" + factor.ToString("N"), SecretVersion = 1,
            SecretBindingVersion = Public, State = "ACTIVE", CreatedAt = now, ActivatedAt = now, PolicyVersion = Public, ProcessingReference = Public, RetentionReference = Public });
        await context.SaveChangesAsync();
        return new(account, authority.Reference, factor, enrolled.Cookies, new(AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N")), new('C', 64), Origin, new('D', 64)));
    }
    private sealed record Baseline(string Hash, string Stamp, uint Counter, long Usage, int Sessions, int Proofs, int Outbox)
    { public override string ToString() => nameof(Baseline); }
    private static async Task<Baseline> Snapshot(IdentityTestDatabase db, AuthenticationTestHost host, Seed seed, PasskeyAuthenticator key)
    {
        await using var context = db.Context(); var user = await context.Users.SingleAsync(x => x.AccountId == seed.Account);
        await using var scope = host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAdapter>>();
        var owner = (await manager.FindByPasskeyIdAsync(key.CredentialId))!; var native = (await manager.GetPasskeyAsync(owner, key.CredentialId))!;
        return new(user.PasswordHash!, user.SecurityStamp!, native.SignCount, await context.Set<PasskeyCredentialRow>().Where(x => x.CredentialReference == seed.Reference).Select(x => x.UsageVersion).SingleAsync(),
            await context.Set<AuthenticationSessionRow>().CountAsync(), await context.Set<StepUpProofRow>().CountAsync(), await context.Set<OutboxMessage>().CountAsync());
    }
    private static async Task Unspent(IdentityTestDatabase db, AuthenticationTestHost host, Seed seed, PasskeyAuthenticator key, Baseline before)
    {
        Assert.True(await Snapshot(db, host, seed, key) == before); await using var context = db.Context();
        Assert.True(!await context.Set<RecoveryResultRow>().AnyAsync() && !await context.Set<RecoveryCodeSetRow>().AnyAsync());
        Assert.True(await context.Set<RecoveryCaseRow>().AllAsync(x => x.ProofKind == "PASSKEY" && x.State == "PENDING_PASSKEY" && x.Version == 1 && x.ResultId == null));
        Assert.True(await context.Set<RecoveryEntryRow>().AllAsync(x => x.State == "OPEN" && x.Version == 1));
        Assert.True(await context.Set<PasskeyCeremonyRow>().Where(x => x.Kind == "RECOVERY").AllAsync(x => x.State == "PENDING" && x.Version == 1 && x.RequestDigest == ""));
    }
    private sealed record Pending(Guid Request, Guid Ceremony, JsonElement Options)
    { public override string ToString() => nameof(Pending); }
    private static async Task<Pending> Begin(AuthenticationTestHost host, Authority authority, RecoveryEntryCaller caller, RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        if (checkpoint is not null) checkpoint.Phase = RecoveryCodeFirstCasePhase.REPLACE;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.START_POST);
        await using var scope = host.Services.CreateAsyncScope(); var app = Application(scope.ServiceProvider); var request = Guid.NewGuid();
        var entry = await app.StartPasswordEntryAsync(caller, request, "person@example.test", authority.Policy.GoalReference);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.START_STATUS);
        Assert.True(entry.ErrorCode is null && entry.Case is { ProofKind: "PASSKEY", State: "PENDING_PASSKEY", Version: 1, SetReference: null });
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_GET);
        var result = await app.StartPasswordPasskeyAsync(caller, request, Http(scope.ServiceProvider));
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_STATUS);
        Assert.True(result.ErrorCode is null && result.Ceremony is { Kind: "RECOVERY", State: "PENDING", SessionId: null, StepUpRequestId: null });
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_BODY);
        return new(request, result.Ceremony!.CeremonyId, JsonSerializer.Deserialize<JsonElement>(result.Ceremony.OptionsJson));
    }
    private static async Task<RecoveryEntryResult> Repair(AuthenticationTestHost host, RecoveryEntryCaller caller, Pending pending, JsonElement credential,
        long version = 1, string? password = Replacement, string? confirmation = Replacement, RecoveryCodeFirstCaseCheckpoint? checkpoint = null,
        CancellationToken ct = default)
    {
        if (checkpoint is not null) checkpoint.Phase = RecoveryCodeFirstCasePhase.REPLACE;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PASSWORD_PROOF_POST);
        await using var scope = host.Services.CreateAsyncScope();
        try
        {
            return await Application(scope.ServiceProvider).RepairPasswordWithPasskeyAsync(caller, pending.Request, version, pending.Ceremony, credential, password, confirmation, Http(scope.ServiceProvider), ct);
        }
        catch (Exception error) when (checkpoint is not null && error is not OutOfMemoryException)
        {
            // First finite case only: closed categories, never exception payloads or recursive inner inspection.
            var postgres = error as PostgresException ?? (error as DbUpdateException)?.InnerException as PostgresException;
            var stage = postgres is not null ? postgres.SqlState switch
            {
                "42703" => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_42703,
                "42P01" => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_42P01,
                "23514" => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_23514,
                "23503" => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_23503,
                "23505" => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_23505,
                "22P02" => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_22P02,
                "42883" => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_42883,
                _ => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SQL_OTHER
            } : error switch
            {
                InvalidOperationException { Message: "Native recovery preparation mutated persisted state." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_PREPARATION_MUTATED,
                InvalidOperationException { Message: "The exact current verified native recovery source is required." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_SOURCE_REQUIRED,
                InvalidOperationException { Message: "Native recovery counter update failed." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_COUNTER_UPDATE,
                InvalidOperationException { Message: "Native recovery credential disappeared." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_CREDENTIAL_MISSING,
                InvalidOperationException { Message: "An exact current recovery credential transition is required." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_PASSWORD_TRANSITION,
                InvalidOperationException { Message: "Strong recovery authority changed after native counter update." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_AFTER_NATIVE_AUTHORITY,
                InvalidOperationException { Message: "Recovery native/password/result effects lost their causal witnesses." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_CAUSAL_WITNESS,
                InvalidOperationException { Message: "Recovery authority changed before final commit." } => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_FINAL_AUTHORITY,
                InvalidOperationException => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_INVALID_OPERATION,
                FormatException => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_FORMAT,
                JsonException => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_JSON,
                System.Security.Cryptography.CryptographicException => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_CRYPTO,
                InvalidCastException => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_INVALID_CAST,
                _ => RecoveryCodeFirstCaseStage.NATIVE_REPAIR_OTHER
            };
            checkpoint.Mark(stage);
            throw new RecoveryCodeFirstCaseFailure(RecoveryCodeFirstCasePhase.REPLACE, stage);
        }
    }

    internal async Task NativeStrongProofRepairsPasswordWithoutCodeCompositionOrLogin()
    {
        var checkpoint = new RecoveryCodeFirstCaseCheckpoint();
        try
        {
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DATABASE);
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        checkpoint.Mark(RecoveryCodeFirstCaseStage.HOST);
        await using var host = await Host(db, authority);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key, checkpoint: checkpoint);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
        var before = await Snapshot(db, host, seed, key);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CONSUMED_PROOFS);
        Assert.True(host.Services.GetService<IRecoveryCodePolicySource>() is null && host.Services.GetService<RecoveryCodeNative>() is null);
        await using (var scope = host.Services.CreateAsyncScope()) Assert.True(scope.ServiceProvider.GetService<RecoveryCodeApplication>() is null);
        var pending = await Begin(host, authority, seed.Caller, checkpoint);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.PROOF_BINDING);
        var assertion = key.Assert(pending.Options, Origin);
        var repaired = await Repair(host, seed.Caller, pending, assertion, checkpoint: checkpoint);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.PROOF_STATUS);
        Assert.True(repaired.ErrorCode is null && repaired.Entry is { State: "REPAIRED", Version: 2 } && repaired.Result is { ProofKind: "PASSKEY", Outcome: "PASSWORD_REPAIRED_NO_SESSION", SetReference: null });
        checkpoint.Phase = RecoveryCodeFirstCasePhase.STATE;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
        var after = await Snapshot(db, host, seed, key);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.ACTIVE_SET_STATES);
        Assert.True(after.Hash != before.Hash && after.Stamp != before.Stamp && after.Counter == 1 && after.Usage == before.Usage + 1 && after.Sessions == before.Sessions && after.Proofs == before.Proofs && after.Outbox == before.Outbox + 1);
        await using var check = db.Context(); var user = await check.Users.SingleAsync(x => x.AccountId == seed.Account); var hasher = AuthenticationPasswordWork.Hasher(host.Policies.Policy);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CAUSAL_RETIREMENT);
        Assert.True(hasher.VerifyHashedPassword(user, user.PasswordHash!, Replacement) != PasswordVerificationResult.Failed && hasher.VerifyHashedPassword(user, user.PasswordHash!, Password) == PasswordVerificationResult.Failed);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BODY);
        Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Active && x.Version == 1 && x.AgeEligibilityStatus == "INELIGIBLE" && x.AgePolicyVersion == "fixture-age"));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_COUNT);
        Assert.True(await check.VerifiedContacts.AllAsync(x => x.State == ContactVerificationState.Verified && x.Version == 1));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BINDING);
        Assert.True(await check.Set<TotpAuthenticatorRow>().AnyAsync(x => x.AuthenticatorReference == seed.Factor && x.State == "ACTIVE" && x.Version == 1 && x.UsageVersion == 0));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.PROOF_BINDING);
        Assert.True(await check.Set<PasskeyCredentialRow>().AnyAsync(x => x.CredentialReference == seed.Reference && x.State == "ACTIVE" && x.Version == 1 && x.UsageVersion == 1));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RETAIN_CODES);
        Assert.True(!await check.Set<RecoveryCodeSetRow>().AnyAsync() && !await check.Set<RecoveryCodeEntryRow>().AnyAsync());
        var result = repaired.Result!;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_BODY);
        Assert.True(result.TargetSessionId is null && result.SessionOutboxEventId is null && result.PasskeyCredentialReference == seed.Reference && result.PasskeyCeremonyId == pending.Ceremony && result.PasskeyMethod == PasskeyPolicy.MethodKey(false, false) && result.PasskeyAssurance == "fixture-passkey-assurance" && result.NativeBeforeDigest != result.NativeAfterDigest && result.SessionEffect == nameof(RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions) && result.UnresolvedEffectsJson == JsonSerializer.Serialize(Unresolved));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.SAFE_RESULT);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventId == result.RepairOutboxEventId && x.OperationId == RecoveryCaseApplication.OperationId(repaired.Case!) && x.CorrelationId == pending.Request.ToString("N")) == 1);
        Assert.True(!await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "RecoveryCompleted"));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CONSUMED_PROOFS);
        Assert.True(await check.Set<AuthenticationSessionRow>().AllAsync(x => x.SecurityStampDigest == AuthenticationIdentity.Digest(before.Stamp)));
        var immutable = false;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
        await using (var mutation = db.Context())
        {
            try { _ = await mutation.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.recovery_results SET native_after_digest={new string('F', 64)} WHERE result_id={result.ResultId}"); }
            catch (PostgresException error) when (error.SqlState == "23514") { immutable = true; }
        }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_STATUS);
        Assert.True(immutable);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_BODY);
        var recoveryCeremony = await check.Set<PasskeyCeremonyRow>().AsNoTracking().SingleAsync(x => x.CeremonyId == pending.Ceremony);
        var enrollmentCeremony = await check.Set<PasskeyCeremonyRow>().AsNoTracking().SingleAsync(x => x.AccountId == seed.Account && x.Kind == "ENROLL");
        Assert.True(recoveryCeremony is { Kind: "RECOVERY", State: "COMPLETED" } && enrollmentCeremony.State == "COMPLETED");
        var originalRecovery = JsonSerializer.Serialize(recoveryCeremony); var originalEnrollment = JsonSerializer.Serialize(enrollmentCeremony);
        var recoveryKindRejected = false; var enrollmentKindRejected = false;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
        await using (var mutation = db.Context())
        {
            try { _ = await mutation.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.passkey_ceremonies SET kind='ENROLL' WHERE ceremony_id={pending.Ceremony}"); }
            catch (PostgresException error) when (error.SqlState == "23514") { recoveryKindRejected = true; }
        }
        await using (var mutation = db.Context())
        {
            try { _ = await mutation.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.passkey_ceremonies SET kind='RECOVERY' WHERE ceremony_id={enrollmentCeremony.CeremonyId}"); }
            catch (PostgresException error) when (error.SqlState == "23514") { enrollmentKindRejected = true; }
        }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_STATUS);
        Assert.True(recoveryKindRejected && enrollmentKindRejected);
        await using (var preserved = db.Context())
        {
            var recovery = await preserved.Set<PasskeyCeremonyRow>().AsNoTracking().SingleAsync(x => x.CeremonyId == pending.Ceremony);
            var enrollment = await preserved.Set<PasskeyCeremonyRow>().AsNoTracking().SingleAsync(x => x.CeremonyId == enrollmentCeremony.CeremonyId);
            Assert.True(JsonSerializer.Serialize(recovery) == originalRecovery && JsonSerializer.Serialize(enrollment) == originalEnrollment);
        }
        checkpoint.Phase = RecoveryCodeFirstCasePhase.LOGIN;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
        using (var oldSession = await host.Get("/authentication/session", seed.EnrollmentCookies)) Assert.True(oldSession.StatusCode == HttpStatusCode.Unauthorized && NoLoginCookie(oldSession));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.OPTIONS_GET);
        var browser = await host.Options();
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
        using var anonymous = await host.Get("/authentication/session", browser.Cookies);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_STATUS);
        Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized);
        checkpoint.Phase = RecoveryCodeFirstCasePhase.STATE;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DISPOSAL);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new RecoveryCodeFirstCaseFailure(checkpoint.Phase, checkpoint.Stage);
        }
    }
    internal async Task NativeStrongProofRejectsSignatureFlagsOriginChallengeAndUserHandle()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); await using var host = await Host(db, authority);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key, counter: true); var before = await Snapshot(db, host, seed, key); var pending = await Begin(host, authority, seed.Caller); Assert.True(before.Counter > 0);
        var valid = key.Assert(pending.Options, Origin);
        Assert.True((await Repair(host, seed.Caller, pending, valid, password: "short", confirmation: "short")).ErrorCode == "VALIDATION_ERROR");
        Assert.True((await Repair(host, seed.Caller, pending, valid, confirmation: "different synthetic confirmation")).ErrorCode == "VALIDATION_ERROR");
        Assert.True((await Repair(host, seed.Caller, pending, valid, version: 2)).ErrorCode == "RECOVERY_PROOF_REJECTED");
        Assert.True((await Repair(host, seed.Caller, pending, JsonSerializer.SerializeToElement(new { id = "invalid*" }))).ErrorCode == "VALIDATION_ERROR");
        await Unspent(db, host, seed, key, before);
        foreach (var failure in InvalidAssertions)
        {
            var credential = key.Assert(pending.Options, failure == "origin" ? "https://foreign.example.test" : Origin, wrongRp: failure == "rp" ? "foreign.example.test" : null,
                challenge: failure == "challenge" ? "wrong-synthetic-challenge" : null, type: failure == "type" ? "webauthn.create" : "webauthn.get", uv: failure != "uv", up: failure != "up", badSignature: failure == "signature",
                wrongUserHandle: failure == "userHandle" ? "d3Jvbmctc3ludGhldGljLXVzZXI" : null, counterOverride: failure == "counter" ? before.Counter : null, backup: failure == "backup");
            var rejected = await Repair(host, seed.Caller, pending, credential); Assert.True(rejected.ErrorCode == "RECOVERY_PROOF_REJECTED" && rejected.Result is null); await Unspent(db, host, seed, key, before);
        }
        var accepted = await Repair(host, seed.Caller, pending, key.Assert(pending.Options, Origin)); Assert.True(accepted.ErrorCode is null && accepted.Result is not null);
    }
    internal async Task OriginalCaseAndCredentialBindingsRejectCrossContextReplay()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); await using var host = await Host(db, authority);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key); var before = await Snapshot(db, host, seed, key);
        var first = await Begin(host, authority, seed.Caller); var second = await Begin(host, authority, seed.Caller); var assertion = key.Assert(first.Options, Origin);
        var callers = new[] { seed.Caller with { BrowserDigest = new('E', 64) }, seed.Caller with { PreviousCookieDigest = new('E', 64) }, seed.Caller with { NetworkDigest = new('E', 64) } };
        foreach (var caller in callers) Assert.True((await Repair(host, caller, first, assertion)).ErrorCode is not null);
        foreach (var substitution in new[] { first with { Request = second.Request }, first with { Ceremony = second.Ceremony }, first with { Ceremony = Guid.NewGuid() } }) Assert.True((await Repair(host, seed.Caller, substitution, assertion)).ErrorCode == "RECOVERY_PROOF_REJECTED");
        Assert.True((await Repair(host, seed.Caller, first, assertion, version: 2)).ErrorCode == "RECOVERY_PROOF_REJECTED");
        using var other = new PasskeyAuthenticator(); Assert.True((await Repair(host, seed.Caller, first, other.Assert(first.Options, Origin))).ErrorCode == "RECOVERY_PROOF_REJECTED");
        await Unspent(db, host, seed, key, before); var accepted = await Repair(host, seed.Caller, first, assertion); Assert.True(accepted.ErrorCode is null && accepted.Result is not null);
        Assert.True((await Repair(host, seed.Caller, second, assertion)).ErrorCode == "RECOVERY_PROOF_REJECTED");
    }
    internal async Task CurrentPolicyHierarchyAndNativeSourceDriftNeverBurnProof()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); await using var host = await Host(db, authority);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key); var before = await Snapshot(db, host, seed, key);
        foreach (var change in AuthorityChanges)
        {
            authority.Reset(); var pending = await Begin(host, authority, seed.Caller); var assertion = key.Assert(pending.Options, Origin); var observed = false;
            await using var scope = host.Services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            if (change == "policy") authority.BeforePolicy = () => { if (context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added)) { observed = true; authority.Policy = authority.Policy with { Version = "fixture-strong-repair-v2" }; } };
            if (change is "hierarchy" or "selection") authority.BeforeHierarchy = () => { if (!context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added)) return; observed = true; if (change == "selection") authority.ForcedSelection = Guid.NewGuid(); else authority.Evidence = "fixture-withdrawn-strong-admission"; };
            if (change == "native-policy") authority.NativeVersion = "fixture-passkey-v2";
            if (change == "blocklist-await") authority.BeforeBlocklist = async ct => { await Task.Yield(); ct.ThrowIfCancellationRequested(); observed = true; authority.Policy = authority.Policy with { Version = "fixture-strong-repair-v2" }; };
            if (change == "native-source") authority.BeforeBlocklist = async ct =>
            {
                ct.ThrowIfCancellationRequested(); await using var driftScope = host.Services.CreateAsyncScope();
                var manager = driftScope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAdapter>>();
                var owner = (await manager.FindByPasskeyIdAsync(key.CredentialId))!; var value = (await manager.GetPasskeyAsync(owner, key.CredentialId))!;
                var changed = await manager.AddOrUpdatePasskeyAsync(owner, new UserPasskeyInfo(value.CredentialId, value.PublicKey, value.CreatedAt,
                    100, value.Transports, value.IsUserVerified, value.IsBackupEligible, value.IsBackedUp, value.AttestationObject, value.ClientDataJson));
                Assert.True(changed.Succeeded); observed = true;
            };
            var rejected = false;
            try { rejected = (await Application(scope.ServiceProvider).RepairPasswordWithPasskeyAsync(seed.Caller, pending.Request, 1, pending.Ceremony, assertion, Replacement, Replacement, Http(scope.ServiceProvider))).ErrorCode is not null; }
            catch (InvalidOperationException) when (observed) { rejected = true; }
            Assert.True(rejected && (change == "native-policy" || observed)); authority.Reset();
            if (change == "native-source") { var current = await Snapshot(db, host, seed, key); Assert.True(current.Counter == 100 && current.Hash == before.Hash && current.Stamp == before.Stamp && current.Usage == before.Usage); before = current; }
            await Unspent(db, host, seed, key, before);
        }
    }
    internal async Task ExactOriginalDuplicateReconcilesAndAlteredAssertionCannotReplay()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); await using var host = await Host(db, authority);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key); var pending = await Begin(host, authority, seed.Caller); var assertion = key.Assert(pending.Options, Origin);
        var completed = await Repair(host, seed.Caller, pending, assertion); Assert.True(completed.ErrorCode is null && completed.Result is not null); var after = await Snapshot(db, host, seed, key);
        var duplicate = await Repair(host, seed.Caller, pending, assertion, version: 2, password: null, confirmation: null); Assert.True(duplicate.ErrorCode is null && duplicate.Reconciled && duplicate.Result?.ResultId == completed.Result!.ResultId);
        await using (var scope = host.Services.CreateAsyncScope()) { var original = await Application(scope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, pending.Request); Assert.True(original.ErrorCode is null && original.Reconciled && original.Result?.ResultId == completed.Result!.ResultId); Assert.True((await Application(scope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, Guid.NewGuid())).ErrorCode is not null); }
        Assert.True((await Repair(host, seed.Caller, pending, key.Assert(pending.Options, Origin))).ErrorCode == "RECOVERY_PROOF_REJECTED"); Assert.True(await Snapshot(db, host, seed, key) == after);
        authority.ForcedSelection = Guid.NewGuid(); await using var changedScope = host.Services.CreateAsyncScope(); Assert.True((await Application(changedScope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, pending.Request)).ErrorCode == "NOT_FOUND_OR_NOT_DISCLOSABLE");
    }
    internal async Task ConcurrentStrongRepairsApplyOneOriginalEffect()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); await using var host = await Host(db, authority);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key); var before = await Snapshot(db, host, seed, key);
        var first = await Begin(host, authority, seed.Caller); var second = await Begin(host, authority, seed.Caller); var firstAssertion = key.Assert(first.Options, Origin); var secondAssertion = key.Assert(second.Options, Origin);
        var results = await Task.WhenAll(Repair(host, seed.Caller, first, firstAssertion), Repair(host, seed.Caller, second, secondAssertion));
        Assert.True(results.Count(x => x.ErrorCode is null && x.Result is not null) == 1 && results.Count(x => x.ErrorCode is not null) == 1);
        var winner = results[0].ErrorCode is null ? 0 : 1; await using (var scope = host.Services.CreateAsyncScope()) { var original = await Application(scope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, winner == 0 ? first.Request : second.Request); Assert.True(original.ErrorCode is null && original.Reconciled && original.Result?.ResultId == results[winner].Result!.ResultId); }
        var after = await Snapshot(db, host, seed, key); Assert.True(after.Counter is 1 or 2 && after.Usage == before.Usage + 1 && after.Outbox == before.Outbox + 1 && after.Sessions == before.Sessions && after.Proofs == before.Proofs);
        await using var context = db.Context(); Assert.True(await context.Set<RecoveryResultRow>().CountAsync() == 1 && await context.Set<RecoveryEntryRow>().CountAsync(x => x.State == "REPAIRED") == 1);
    }
    private sealed class RepairSaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Observed { get; private set; }
        internal bool Saved { get; private set; }
        internal CancellationTokenSource? Cancellation { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observed = Armed && eventData.Context is { } context && context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added && x.Entity.ProofKind == "PASSKEY") && context.ChangeTracker.Entries<RecoveryEntryRow>().Any(x => x.Entity.State == "REPAIRED") && context.ChangeTracker.Entries<PasskeyCredentialRow>().Any(x => x.Entity.UsageVersion == 1) && context.ChangeTracker.Entries<PasskeyCeremonyRow>().Any(x => x.Entity.Kind == "RECOVERY" && x.Entity.State == "COMPLETED");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed) return ValueTask.FromResult(result); Saved = true;
            if (Cancellation is { } cancellation) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("FINITE_STRONG_RECOVERY_SAVE_FAILURE");
        }
        internal void Reset() { Armed = false; Observed = false; Saved = false; Cancellation = null; }
    }
    internal async Task PostSaveFaultAndCancellationRollbackNativeCounterAndPasswordEffects()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var observer = new RepairSaveFailure(); await using var host = await Host(db, authority, observer);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key); var before = await Snapshot(db, host, seed, key); var pending = await Begin(host, authority, seed.Caller); var assertion = key.Assert(pending.Options, Origin);
        foreach (var cancel in FailureModes)
        {
            observer.Reset(); using var cancellation = new CancellationTokenSource(); observer.Armed = true; observer.Cancellation = cancel ? cancellation : null; var interrupted = false;
            try { _ = await Repair(host, seed.Caller, pending, assertion, ct: cancellation.Token); }
            catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { interrupted = true; }
            catch (InvalidOperationException) when (!cancel && observer.Saved) { interrupted = true; }
            Assert.True(interrupted && observer.Observed && observer.Saved); observer.Reset(); await Unspent(db, host, seed, key, before);
        }
        var retry = await Repair(host, seed.Caller, pending, assertion); Assert.True(retry.ErrorCode is null && retry.Result is not null);
    }
    private static string WithoutCookie(string cookies, string name) => string.Join("; ", cookies.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !x.StartsWith(name + "=", StringComparison.Ordinal)));
    private static bool NoLoginCookie(HttpResponseMessage response) => !response.Headers.TryGetValues("Set-Cookie", out var values) || !values.Any(x => x.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal) && x.Split(';', 2)[0].Length > AuthenticationIdentity.CookieName.Length + 1);
    private static async Task<string> RestoreAndCheckOptions(AuthenticationTestHost host, Guid request, JsonElement original, string cookies, string csrf)
    {
        using var restored = await host.Post("/recovery/result", new { requestId = request }, WithoutCookie(cookies, Pointer), csrf); Assert.True(restored.StatusCode == HttpStatusCode.OK && NoLoginCookie(restored)); cookies = Merge(WithoutCookie(cookies, Pointer), restored);
        using var reloaded = await host.Post("/recovery/passkey/options", new { requestId = request }, cookies, csrf); Assert.True(reloaded.StatusCode == HttpStatusCode.OK && NoLoginCookie(reloaded));
        var wire = await reloaded.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(wire.GetProperty("ceremonyId").GetGuid() == original.GetProperty("ceremonyId").GetGuid() && wire.GetProperty("publicKey").GetRawText() == original.GetProperty("publicKey").GetRawText() && wire.GetProperty("expiresAt").GetDateTimeOffset() == original.GetProperty("expiresAt").GetDateTimeOffset()); return Merge(cookies, reloaded);
    }
    internal async Task ActualHttpsStrongRecoveryIsNeutralAndRequiresOriginalBrowserCsrf()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); await using var host = await Host(db, authority);
        using var key = new PasskeyAuthenticator(); var seed = await SeedAsync(db, host, authority, key); var before = await Snapshot(db, host, seed, key); var boot = await host.Options(); var cookies = boot.Cookies; var csrf = boot.Csrf;
        using (var options = await host.Get("/recovery/options", cookies)) { Assert.True(options.StatusCode == HttpStatusCode.OK && NoLoginCookie(options)); var wire = await options.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(wire.GetProperty("passkey").GetProperty("available").GetBoolean()); }
        var unknownRequest = Guid.NewGuid();
        using (var start = await host.Post("/recovery/start", new { requestId = unknownRequest, email = "absent@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf)) { Assert.True(start.StatusCode == HttpStatusCode.OK && NoLoginCookie(start)); cookies = Merge(cookies, start); }
        JsonElement neutral;
        using (var options = await host.Post("/recovery/passkey/options", new { requestId = unknownRequest }, cookies, csrf)) { Assert.True(options.StatusCode == HttpStatusCode.OK && NoLoginCookie(options)); neutral = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options); }
        cookies = await RestoreAndCheckOptions(host, unknownRequest, neutral, cookies, csrf);
        using (var malformed = await host.Post("/recovery/passkey/repair", new { requestId = unknownRequest, expectedVersion = 1, ceremonyId = neutral.GetProperty("ceremonyId").GetGuid(), credential = new { id = "invalid*" }, password = Replacement, passwordConfirmation = Replacement }, cookies, csrf)) Assert.True(malformed.StatusCode == HttpStatusCode.BadRequest && (await malformed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString() == "VALIDATION_ERROR");
        using (var reject = await host.Post("/recovery/passkey/repair", new { requestId = unknownRequest, expectedVersion = 1, ceremonyId = neutral.GetProperty("ceremonyId").GetGuid(), credential = key.Assert(neutral.GetProperty("publicKey"), Origin), password = Replacement, passwordConfirmation = Replacement }, cookies, csrf)) { Assert.True(reject.StatusCode == HttpStatusCode.BadRequest && (await reject.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString() == "RECOVERY_PROOF_REJECTED"); }
        await using (var context = db.Context()) Assert.True(!await context.Set<RecoveryCaseRow>().AnyAsync() && !await context.Set<PasskeyCeremonyRow>().AnyAsync(x => x.Kind == "RECOVERY"));
        var neutralDowngradeBlocked = false;
        await using (var downgrade = db.Context())
        {
            try { _ = await downgrade.Database.ExecuteSqlRawAsync(RecoveryPasskeyMigrationSql.BlockPopulatedDowngrade); }
            catch (PostgresException error) when (error.SqlState == "55000") { neutralDowngradeBlocked = true; }
        }
        Assert.True(neutralDowngradeBlocked);
        await using (var preserved = db.Context())
        {
            var entry = await preserved.Set<RecoveryEntryRow>().SingleAsync(x => x.RequestId == unknownRequest);
            Assert.True(entry.PasskeyCeremonyId == neutral.GetProperty("ceremonyId").GetGuid() && entry.PasskeyOptionsJson == neutral.GetProperty("publicKey").GetRawText() && entry.State == "OPEN" && entry.Version == 1);
            Assert.True(!await preserved.Set<RecoveryCaseRow>().AnyAsync() && !await preserved.Set<PasskeyCeremonyRow>().AnyAsync(x => x.Kind == "RECOVERY"));
        }
        var request = Guid.NewGuid();
        using (var start = await host.Post("/recovery/start", new { requestId = request, email = "person@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf)) { Assert.True(start.StatusCode == HttpStatusCode.OK && NoLoginCookie(start)); cookies = Merge(cookies, start); }
        using (var missing = await host.Post("/recovery/passkey/options", new { requestId = request }, cookies, null)) Assert.True(missing.StatusCode == HttpStatusCode.BadRequest);
        using (var bad = await host.Post("/recovery/passkey/options", new { requestId = request }, cookies, "invalid-synthetic-csrf")) Assert.True(bad.StatusCode == HttpStatusCode.BadRequest);
        using (var origin = await host.Post("/recovery/passkey/options", new { requestId = request }, cookies, csrf, "https://foreign.example.test")) Assert.True(origin.StatusCode == HttpStatusCode.Forbidden);
        using (var browser = await host.Post("/recovery/passkey/options", new { requestId = request }, WithoutCookie(cookies, AuthenticationEndpoints.BrowserCookieName), csrf)) Assert.True(browser.StatusCode == HttpStatusCode.Forbidden);
        using (var pointer = await host.Post("/recovery/passkey/options", new { requestId = request }, WithoutCookie(cookies, Pointer), csrf)) Assert.True(pointer.StatusCode == HttpStatusCode.NotFound);
        using (var query = await host.Post("/recovery/passkey/options?unexpected=true", new { requestId = request }, cookies, csrf)) Assert.True(query.StatusCode == HttpStatusCode.BadRequest);
        foreach (var selector in ForbiddenSelectors) { using var selected = await host.Post("/recovery/passkey/options", new Dictionary<string, object> { ["requestId"] = request, [selector] = Guid.NewGuid() }, cookies, csrf); Assert.True(selected.StatusCode == HttpStatusCode.BadRequest); }
        JsonElement actual;
        using (var options = await host.Post("/recovery/passkey/options", new { requestId = request }, cookies, csrf)) { Assert.True(options.StatusCode == HttpStatusCode.OK && NoLoginCookie(options)); actual = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options); }
        cookies = await RestoreAndCheckOptions(host, request, actual, cookies, csrf); var knownOptions = actual.GetProperty("publicKey"); var neutralOptions = neutral.GetProperty("publicKey");
        Assert.True(actual.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(neutral.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal)) && knownOptions.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(neutralOptions.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal)));
        Assert.True(!knownOptions.TryGetProperty("allowCredentials", out var allow) || allow.GetArrayLength() == 0); Assert.True(!neutralOptions.TryGetProperty("allowCredentials", out var neutralAllow) || neutralAllow.GetArrayLength() == 0);
        Assert.True(knownOptions.GetProperty("challenge").GetString() != neutralOptions.GetProperty("challenge").GetString() && knownOptions.GetProperty("userVerification").GetString() == "required" && neutralOptions.GetProperty("userVerification").GetString() == "required" && knownOptions.GetProperty("rpId").GetString() == neutralOptions.GetProperty("rpId").GetString() && knownOptions.GetProperty("timeout").GetInt32() == neutralOptions.GetProperty("timeout").GetInt32() && actual.GetProperty("expiresAt").GetDateTimeOffset() == neutral.GetProperty("expiresAt").GetDateTimeOffset());
        var ceremony = actual.GetProperty("ceremonyId").GetGuid(); var credential = key.Assert(knownOptions, Origin); var command = new { requestId = request, expectedVersion = 1, ceremonyId = ceremony, credential, password = Replacement, passwordConfirmation = Replacement };
        using (var missing = await host.Post("/recovery/passkey/repair", command, cookies, null)) Assert.True(missing.StatusCode == HttpStatusCode.BadRequest);
        using (var bad = await host.Post("/recovery/passkey/repair", command, cookies, "invalid-synthetic-csrf")) Assert.True(bad.StatusCode == HttpStatusCode.BadRequest);
        using (var origin = await host.Post("/recovery/passkey/repair", command, cookies, csrf, "https://foreign.example.test")) Assert.True(origin.StatusCode == HttpStatusCode.Forbidden);
        using (var browser = await host.Post("/recovery/passkey/repair", command, WithoutCookie(cookies, AuthenticationEndpoints.BrowserCookieName), csrf)) Assert.True(browser.StatusCode == HttpStatusCode.Forbidden);
        using (var pointer = await host.Post("/recovery/passkey/repair", command, WithoutCookie(cookies, Pointer), csrf)) Assert.True(pointer.StatusCode == HttpStatusCode.NotFound);
        using (var query = await host.Post("/recovery/passkey/repair?unexpected=true", command, cookies, csrf)) Assert.True(query.StatusCode == HttpStatusCode.BadRequest);
        using (var malformed = await host.Post("/recovery/passkey/repair", new { requestId = request, expectedVersion = 1, ceremonyId = ceremony, credential = new { id = "invalid*" }, password = Replacement, passwordConfirmation = Replacement }, cookies, csrf)) Assert.True(malformed.StatusCode == HttpStatusCode.BadRequest && (await malformed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString() == "VALIDATION_ERROR");
        using (var wrong = await host.Post("/recovery/passkey/repair", new { requestId = request, expectedVersion = 1, ceremonyId = ceremony, credential = key.Assert(knownOptions, Origin, badSignature: true), password = Replacement, passwordConfirmation = Replacement }, cookies, csrf)) Assert.True(wrong.StatusCode == HttpStatusCode.BadRequest && (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString() == "RECOVERY_PROOF_REJECTED");
        var foreign = await host.Options(); var selectedPointer = cookies.Split(';').Select(x => x.Trim()).Single(x => x.StartsWith(Pointer + "=", StringComparison.Ordinal));
        using (var borrowed = await host.Post("/recovery/passkey/repair", command, foreign.Cookies + "; " + selectedPointer, foreign.Csrf)) Assert.True(borrowed.StatusCode == HttpStatusCode.NotFound);
        foreach (var selector in ForbiddenSelectors) { var selected = new Dictionary<string, object> { ["requestId"] = request, ["expectedVersion"] = 1, ["ceremonyId"] = ceremony, ["credential"] = credential, ["password"] = Replacement, ["passwordConfirmation"] = Replacement, [selector] = Guid.NewGuid() }; using var refused = await host.Post("/recovery/passkey/repair", selected, cookies, csrf); Assert.True(refused.StatusCode == HttpStatusCode.BadRequest); }
        await Unspent(db, host, seed, key, before);
        using (var repaired = await host.Post("/recovery/passkey/repair", command, cookies, csrf)) { Assert.True(repaired.StatusCode == HttpStatusCode.OK && NoLoginCookie(repaired)); cookies = Merge(cookies, repaired); var wire = await repaired.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(wire.GetProperty("operation").GetProperty("status").GetString() == "PASSWORD_REPAIRED_NO_SESSION" && !wire.GetProperty("operation").GetProperty("authenticated").GetBoolean()); }
        using (var original = await host.Post("/recovery/result", new { requestId = request }, WithoutCookie(cookies, Pointer), csrf)) { Assert.True(original.StatusCode == HttpStatusCode.OK && NoLoginCookie(original)); cookies = Merge(cookies, original); }
        using (var session = await host.Get("/authentication/session", cookies)) Assert.True(session.StatusCode == HttpStatusCode.Unauthorized);
        var after = await Snapshot(db, host, seed, key); Assert.True(after.Sessions == before.Sessions && after.Proofs == before.Proofs && after.Outbox == before.Outbox + 1);
    }
}
