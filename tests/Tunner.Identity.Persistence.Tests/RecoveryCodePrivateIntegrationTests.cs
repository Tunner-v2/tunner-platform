using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

// Fixed first-case checkpoints only. No exception payload or cross-request cache is retained.
internal enum RecoveryCodeFirstCasePhase { SETUP, LOGIN, GENERATE, SAFE_READ, REPLACE, STATE, FEDERATION_MFA }
internal enum RecoveryCodeFirstCaseStage
{
    DATABASE, HOST, SEED, OPTIONS_GET, OPTIONS_STATUS, OPTIONS_BODY, SIGN_IN_POST, SIGN_IN_STATUS,
    START_POST, START_STATUS, START_BODY, CURRENT_OPTIONS_GET, CURRENT_OPTIONS_STATUS, CURRENT_OPTIONS_BODY,
    CAN_CONTINUE, PROOF_OPTIONS_GET, PROOF_OPTIONS_STATUS, PROOF_OPTIONS_BODY, PROOF_BINDING, PASSWORD_PROOF_POST,
    PROOF_STATUS, RESULT_COOKIE, COOKIE_MAX_AGE, DELIVERY_BODY, DELIVERY_BINDING, DELIVERY_COUNT, RETAIN_CODES,
    RESULT_POST, RESULT_STATUS, RESULT_BODY, SAFE_RESULT, CHECK_CONTEXT, ACTIVE_SET_STATES, CAUSAL_RETIREMENT,
    CONSUMED_PROOFS, DISPOSAL,
    NATIVE_REPAIR_SQL_42703, NATIVE_REPAIR_SQL_42P01, NATIVE_REPAIR_SQL_23514, NATIVE_REPAIR_SQL_23503,
    NATIVE_REPAIR_SQL_23505, NATIVE_REPAIR_SQL_22P02, NATIVE_REPAIR_SQL_42883, NATIVE_REPAIR_SQL_OTHER,
    NATIVE_REPAIR_PREPARATION_MUTATED, NATIVE_REPAIR_SOURCE_REQUIRED, NATIVE_REPAIR_COUNTER_UPDATE,
    NATIVE_REPAIR_CREDENTIAL_MISSING, NATIVE_REPAIR_PASSWORD_TRANSITION, NATIVE_REPAIR_AFTER_NATIVE_AUTHORITY,
    NATIVE_REPAIR_CAUSAL_WITNESS, NATIVE_REPAIR_FINAL_AUTHORITY, NATIVE_REPAIR_INVALID_OPERATION,
    NATIVE_REPAIR_FORMAT, NATIVE_REPAIR_JSON, NATIVE_REPAIR_CRYPTO, NATIVE_REPAIR_INVALID_CAST, NATIVE_REPAIR_OTHER,
    FEDERATION_MFA_DATABASE, FEDERATION_MFA_HOST, FEDERATION_MFA_SEED, FEDERATION_MFA_LINK,
    FEDERATION_MFA_LOGIN, FEDERATION_MFA_ENROLL, FEDERATION_MFA_BASELINE, FEDERATION_MFA_VERIFIED,
    FEDERATION_MFA_PRIMARY_COMPLETE, FEDERATION_MFA_PENDING_ORIGINAL, FEDERATION_MFA_PENDING_RESULT,
    FEDERATION_MFA_PENDING_ROWS, FEDERATION_MFA_CODE, FEDERATION_MFA_COMPLETE, FEDERATION_MFA_COMPLETE_RESULT,
    FEDERATION_MFA_ORIGINAL, FEDERATION_MFA_ORIGINAL_RESULT, FEDERATION_MFA_RETRY, FEDERATION_MFA_RETRY_RESULT,
    FEDERATION_MFA_FINAL_ROWS, FEDERATION_MFA_FINAL_COUNTS, FEDERATION_MFA_FINAL_PENDING, FEDERATION_MFA_FINAL_SESSION,
    FEDERATION_MFA_DISPOSAL, FEDERATION_MFA_CLOCK_OUT_OF_BOUNDS, FEDERATION_MFA_CODE_REPLAYED,
    FEDERATION_MFA_VERIFICATION_BUDGET, FEDERATION_MFA_AUTHENTICATION_FAILED, FEDERATION_MFA_VERSION_CONFLICT,
    FEDERATION_MFA_DEPENDENCY_UNAVAILABLE, FEDERATION_MFA_RATE_LIMITED, FEDERATION_MFA_VALIDATION_ERROR, FEDERATION_MFA_OTHER_ERROR,
    COHORT_SQL_DISK_FULL, COHORT_SQL_CONNECTION_LIMIT, COHORT_SQL_ADMIN_SHUTDOWN, COHORT_SQL_OTHER, COHORT_DATABASE_TRANSPORT,
    COHORT_SQL_OBJECT_IN_USE, COHORT_POSTGRES_NOT_READY, COHORT_DATABASE_LEASE_INVALID,
    COHORT_DATABASE_OWNER_UNVERIFIED, COHORT_DATABASE_RELEASE_UNVERIFIED
}
internal sealed class RecoveryCodeFirstCaseCheckpoint
{
    internal RecoveryCodeFirstCasePhase Phase { get; set; } = RecoveryCodeFirstCasePhase.SETUP;
    internal RecoveryCodeFirstCaseStage Stage { get; private set; } = RecoveryCodeFirstCaseStage.DATABASE;
    internal void Mark(RecoveryCodeFirstCaseStage stage) => Stage = stage;
    public override string ToString() => nameof(RecoveryCodeFirstCaseCheckpoint);
}
internal sealed class RecoveryCodeFirstCaseFailure(RecoveryCodeFirstCasePhase phase, RecoveryCodeFirstCaseStage stage)
    : Exception("RECOVERY_FIRST_CASE_FAILED")
{
    internal string FailureCode { get; } = Enum.IsDefined(phase) && Enum.IsDefined(stage)
        ? "FIRST_CASE_" + Enum.GetName(phase) + "_" + Enum.GetName(stage) : "CASE_FAILED";
    internal static RecoveryCodeFirstCaseFailure? DatabaseFailure(Exception error)
    {
        var databaseError = error is DbUpdateException { InnerException: { } inner } ? inner : error;
        var diagnostic = databaseError switch
        {
            Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.DiskFull } => RecoveryCodeFirstCaseStage.COHORT_SQL_DISK_FULL,
            Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.TooManyConnections } => RecoveryCodeFirstCaseStage.COHORT_SQL_CONNECTION_LIMIT,
            Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.AdminShutdown } => RecoveryCodeFirstCaseStage.COHORT_SQL_ADMIN_SHUTDOWN,
            Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ObjectInUse } => RecoveryCodeFirstCaseStage.COHORT_SQL_OBJECT_IN_USE,
            Npgsql.PostgresException => RecoveryCodeFirstCaseStage.COHORT_SQL_OTHER,
            Npgsql.NpgsqlException => RecoveryCodeFirstCaseStage.COHORT_DATABASE_TRANSPORT,
            InvalidOperationException { Message: "RECOVERY_POSTGRES_NOT_READY" } => RecoveryCodeFirstCaseStage.COHORT_POSTGRES_NOT_READY,
            InvalidOperationException { Message: "RECOVERY_DATABASE_LEASE_INVALID" } => RecoveryCodeFirstCaseStage.COHORT_DATABASE_LEASE_INVALID,
            InvalidOperationException { Message: "RECOVERY_DATABASE_OWNER_UNVERIFIED" } => RecoveryCodeFirstCaseStage.COHORT_DATABASE_OWNER_UNVERIFIED,
            InvalidOperationException { Message: "RECOVERY_DATABASE_RELEASE_UNVERIFIED" } => RecoveryCodeFirstCaseStage.COHORT_DATABASE_RELEASE_UNVERIFIED,
            _ => (RecoveryCodeFirstCaseStage?)null
        };
        return diagnostic is { } selected ? new(RecoveryCodeFirstCasePhase.STATE, selected) : null;
    }
    public override string ToString() => nameof(RecoveryCodeFirstCaseFailure);
}

/// <summary>Private in-process native cohort. Never output a response/code/verifier/context/buffer value.
/// Execute only after root commits source, generates migration and qualifies the private native topology.</summary>

public sealed class RecoveryCodePrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    internal sealed class Profile : IRecoveryCodePolicySource, IRecoveryCodeMutationAdmissionSource, IRecoveryCodePresentationSource, IAssurancePolicySource, IStepUpPresentationSource
    {
        // All values below are complete finite SYNTHETIC-TEST-ONLY inputs, never production policy defaults.
        internal RecoveryCodeCryptoProfile Crypto { get; } = RecoveryCodeNativeTests.Profile();
        internal RecoveryCodePolicy Policy { get; set; }
        internal bool Published { get; set; } = true;
        internal string RequiredProofAssurance { get; set; } = "fixture-proof";
        internal string PublicationVersion { get; set; } = "fixture-recovery-publication-v1";
        internal int PublicationReads { get; private set; }
        internal bool RetireFirstOnly { get; set; }
        internal string FinalAdmissionChange { get; set; } = "unchanged";
        internal int FinalAdmissionReads { get; private set; }
        internal Func<RecoveryCodeMutationAdmissionRequest, CancellationToken, Task>? BeforeAdmission { get; set; }
        internal bool FinalFactsOriginal { get; private set; }
        internal EffectivePolicyInterval PublicationInterval { get; set; } = Interval;
        internal static EffectivePolicyInterval Interval => new(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true, new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false);
        internal Profile() => Policy = new("fixture-recovery-v1", Origin, Crypto, "NATIVE_HEX32_V1", "fixture-recovery-delivery-v1", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
            2, 8, RecoveryCodeGenerationAdmission.WithinApprovedActiveSetCapacity, RecoveryCodeReplacementMechanism.RetireExactExpectedSetsAtIssueCommit,
            RecoveryCodeSessionEffect.NoSessionMutation, RecoveryCodeDeliveryMechanism.OneVolatileViewWithOptionalSingleLocalDownload, "fixture-recovery-proof", "fixture-recovery-proof",
            20, 100, "fixture-processing", "fixture-retention");
        public RecoveryCodePolicyVersion RequireEffective(DateTimeOffset at) => new(new(Policy.Version, "ACTIVE", "RECOVERY_CODES", Interval, null, ["SYNTHETIC-TEST-ONLY"]), Policy);
        public async ValueTask<RecoveryCodeMutationAdmission> RequireAsync(RecoveryCodeMutationAdmissionRequest request, CancellationToken ct)
        {
            if (BeforeAdmission is { } barrier) await barrier(request, ct);
            return Decide(request);
        }
        private RecoveryCodeMutationAdmission Decide(RecoveryCodeMutationAdmissionRequest request)
        {
            var candidates = RetireFirstOnly ? request.ActiveSets.Take(1) : request.ActiveSets;
            var targets = request.Kind == "REPLACE" ? candidates.Select(x => new RecoveryCodeRetirementTarget(x.SetReference, x.LifecycleVersion)).ToArray() : [];
            var allowed = request.Kind == "GENERATE" ? request.ActiveSets.Count < Policy.MaximumActiveSets : targets.Length != 0;
            return new(request.AccountId, request.AccountVersion, request.Kind, request.FactsDigest, allowed, targets, "fixture-recovery-admission", "fixture-evidence", Interval);
        }
        public RecoveryCodeMutationAdmission RequireCurrent(RecoveryCodeMutationAdmissionRequest request)
        {
            FinalAdmissionReads++;
            FinalFactsOriginal = request.PolicyVersion == Policy.Version && request.ActiveSets.Count == 2 &&
                request.ActiveSets.All(x => x.LifecycleVersion == 1 && x.UsageVersion == 0 && x.RemainingCodeCount == Crypto.CodesPerSet);
            var decision = Decide(request);
            if (FinalAdmissionChange == "missing") throw new InvalidOperationException("Synthetic final admission is unavailable.");
            var targets = FinalAdmissionChange == "target"
                ? request.ActiveSets.Skip(1).Take(1).Select(x => new RecoveryCodeRetirementTarget(x.SetReference, x.LifecycleVersion)).ToArray()
                : decision.RetirementTargets.ToArray();
            return new(request.AccountId, request.AccountVersion, request.Kind, request.FactsDigest,
                decision.Allowed && FinalAdmissionChange != "withdrawal", targets,
                FinalAdmissionChange == "policy" ? "fixture-recovery-admission-v2" : decision.PolicyVersion,
                FinalAdmissionChange == "evidence" ? "fixture-evidence-v2" : decision.EvidenceReference,
                FinalAdmissionChange == "interval" ? new(Interval.From, true, request.EffectiveAt, false) : Interval);
        }
        public RecoveryCodePresentation RequirePublished(RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts, DateTimeOffset at)
        {
            PublicationReads++;
            if (!Published || !PublicationInterval.Contains(at)) throw new InvalidOperationException("Synthetic publication is unavailable.");
            var key = facts.Kind == "GENERATE" ? "fixture.recovery.generateImpact" : "fixture.recovery.replaceImpact";
            var keys = RecoveryCodePresentation.RequiredKeys.Append(key);
            return new("en", keys.Select(x => new RegistrationPublishedText(x, "fixture-variant", "fixture-text-version", "SYNTHETIC-TEST-ONLY", "en", "PUBLISHED", "Fixture only")), key, facts.FactsDigest, PublicationVersion, PublicationInterval);
        }
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset at, string? expectedVersion = null)
        {
            if (expectedVersion is not null && expectedVersion != "fixture-recovery-proof") throw new AssurancePolicyVersionConflictException();
            return new(new("fixture-recovery-proof", "ACTIVE", scope, Interval, null, ["SYNTHETIC-TEST-ONLY"]), RequiredProofAssurance, TimeSpan.FromMinutes(5), new Dictionary<string, string> { ["pwd"] = "fixture-proof" });
        }
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy) => new StepUpBrowserTests.Presentation().RequirePublished(policy);
    }
    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Profile profile, DbTransactionInterceptor? commitObserver = null) => Start(db, services =>
    {
        services.AddSingleton(profile.Crypto); services.AddSingleton<IRecoveryCodePolicySource>(profile); services.AddSingleton<IRecoveryCodeMutationAdmissionSource>(profile);
        services.AddSingleton<IRecoveryCodePresentationSource>(profile); services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
        if (commitObserver is not null) services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(commitObserver));
    });
    private sealed class Pending(Guid request, Guid operation, Guid set, string cookies, string csrf) // Safe refs only; no values in auto-record printing.
    {
        internal Guid Request { get; } = request; internal Guid Operation { get; } = operation; internal Guid Set { get; } = set;
        internal string Cookies { get; set; } = cookies; internal string Csrf { get; set; } = csrf;
        public override string ToString() => nameof(Pending);
    }
    private sealed class Winning(Pending pending, RecoveryCodeValue[] codes) : IDisposable
    {
        internal Pending Pending { get; } = pending; internal RecoveryCodeValue Code => Codes[0];
        internal RecoveryCodeValue[] Codes { get; } = codes;
        public void Dispose() { foreach (var code in Codes) code.Dispose(); }
        public override string ToString() => nameof(Winning);
    }
    private static async Task<string> Login(AuthenticationTestHost host, string email = "person@example.test", RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.OPTIONS_GET);
        var boot = await host.Options();
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.SIGN_IN_POST);
        using var response = await host.Post("/authentication/sign-in", Command(boot.Key, email: email), boot.Cookies, boot.Csrf);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.SIGN_IN_STATUS);
        Assert.True(response.StatusCode == HttpStatusCode.OK); return Merge(boot.Cookies, response);
    }
    private static async Task<Pending> Begin(AuthenticationTestHost host, string cookies, string kind = "GENERATE", RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.OPTIONS_GET);
        using var options = await host.Get("/account/recovery-codes/options", cookies);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.OPTIONS_STATUS);
        Assert.True(options.StatusCode == HttpStatusCode.OK);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.OPTIONS_BODY);
        using var doc = await options.Content.ReadFromJsonAsync<JsonDocument>(); var body = doc!.RootElement;
        var request = body.GetProperty("ownerRequestId").GetGuid(); var version = body.GetProperty("expectedVersion").GetInt64(); var csrf = body.GetProperty("csrfToken").GetString()!;
        cookies = Merge(cookies, options);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.START_POST);
        using var response = await host.Post("/account/recovery-codes/" + kind.ToLowerInvariant() + "/start", new { ownerRequestId = request, expectedVersion = version }, cookies, csrf);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.START_STATUS);
        Assert.True(response.StatusCode == HttpStatusCode.OK);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.START_BODY);
        using var started = await response.Content.ReadFromJsonAsync<JsonDocument>(); var root = started!.RootElement;
        var pending = new Pending(request, Guid.ParseExact(root.GetProperty("operationReference").GetString()!, "N"), root.GetProperty("setReference").GetGuid(), Merge(cookies, response), csrf);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.CURRENT_OPTIONS_GET);
        using var current = await host.Get("/account/recovery-codes/options", pending.Cookies);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.CURRENT_OPTIONS_STATUS);
        Assert.True(current.StatusCode == HttpStatusCode.OK);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.CURRENT_OPTIONS_BODY);
        using var currentDoc = await current.Content.ReadFromJsonAsync<JsonDocument>();
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.CAN_CONTINUE);
        Assert.True(currentDoc!.RootElement.GetProperty("recovery").GetProperty("impact").GetProperty("canContinue").GetBoolean());
        pending.Cookies = Merge(pending.Cookies, current); pending.Csrf = currentDoc.RootElement.GetProperty("csrfToken").GetString()!;
        return pending;
    }
    private static async Task<Guid> PreparePasswordRequest(AuthenticationTestHost host, Pending pending, RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_GET);
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_STATUS);
        Assert.True(options.StatusCode == HttpStatusCode.OK);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_OPTIONS_BODY);
        using var doc = await options.Content.ReadFromJsonAsync<JsonDocument>(); var root = doc!.RootElement;
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_BINDING);
        Assert.True(root.GetProperty("operationReference").GetString() == pending.Operation.ToString("N"));
        pending.Cookies = Merge(pending.Cookies, options); pending.Csrf = root.GetProperty("csrfToken").GetString()!;
        return root.GetProperty("requestId").GetGuid();
    }
    private static async Task<HttpResponseMessage> Prove(AuthenticationTestHost host, Pending pending, RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        var request = await PreparePasswordRequest(host, pending, checkpoint);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PASSWORD_PROOF_POST);
        return await host.Post("/authentication/step-up/password", new { requestId = request, password = Password }, pending.Cookies, pending.Csrf);
    }
    private static async Task<Winning> Generate(AuthenticationTestHost host, string cookies, string kind = "GENERATE", RecoveryCodeFirstCaseCheckpoint? checkpoint = null)
    {
        var pending = await Begin(host, cookies, kind, checkpoint); using var response = await Prove(host, pending, checkpoint);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.PROOF_STATUS);
        Assert.True(response.StatusCode == HttpStatusCode.OK);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.RESULT_COOKIE);
        var resultCookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList())
            .SingleOrDefault(x => x.Name.Value == RecoveryCodeStepUpOwner.ResultCookieName);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.COOKIE_MAX_AGE);
        Assert.True(resultCookie?.MaxAge == TimeSpan.FromMinutes(10)); // Explicit finite profile, measured against the controlled host clock.
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BODY);
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(); var root = doc!.RootElement;
        var delivery = root.GetProperty("recoveryCodeFirstDelivery");
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BINDING);
        Assert.True(delivery.GetProperty("ownerRequestId").GetGuid() == pending.Request && delivery.GetProperty("setReference").GetGuid() == pending.Set);
        checkpoint?.Mark(RecoveryCodeFirstCaseStage.DELIVERY_COUNT);
        Assert.True(delivery.GetProperty("codes").GetArrayLength() == 2 && !root.GetProperty("signInRequired").GetBoolean());
        pending.Cookies = Merge(pending.Cookies, response);
        var retained = new List<RecoveryCodeValue>();
        try
        {
            checkpoint?.Mark(RecoveryCodeFirstCaseStage.RETAIN_CODES);
            foreach (var value in delivery.GetProperty("codes").EnumerateArray()) retained.Add(new(value.GetString()!.ToCharArray()));
            var result = new Winning(pending, retained.ToArray()); retained.Clear(); return result;
        }
        finally { foreach (var value in retained) value.Dispose(); }
    }

    public async Task WinningDeliveryIsAbsentFromEverySafeOriginalReadAndReplacementRetiresExactPriorSet()
    {
        var checkpoint = new RecoveryCodeFirstCaseCheckpoint();
        try
        {
            checkpoint.Mark(RecoveryCodeFirstCaseStage.DATABASE);
            await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
            checkpoint.Mark(RecoveryCodeFirstCaseStage.HOST);
            await using var host = await Host(db, profile);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.SEED);
            await host.SeedAsync(db);
            checkpoint.Phase = RecoveryCodeFirstCasePhase.LOGIN;
            var cookies = await Login(host, checkpoint: checkpoint);
            checkpoint.Phase = RecoveryCodeFirstCasePhase.GENERATE;
            using var first = await Generate(host, cookies, checkpoint: checkpoint);
            checkpoint.Phase = RecoveryCodeFirstCasePhase.SAFE_READ;
            checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
            using var read = await host.Post("/account/recovery-codes/result", new { ownerRequestId = first.Pending.Request, operationReference = first.Pending.Operation.ToString("N") }, first.Pending.Cookies, first.Pending.Csrf);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_STATUS);
            Assert.True(read.StatusCode == HttpStatusCode.OK);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_BODY);
            using var result = await read.Content.ReadFromJsonAsync<JsonDocument>();
            checkpoint.Mark(RecoveryCodeFirstCaseStage.SAFE_RESULT);
            Assert.True(result!.RootElement.GetProperty("codesUnavailable").GetBoolean() && !result.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _));
            checkpoint.Phase = RecoveryCodeFirstCasePhase.REPLACE;
            using var second = await Generate(host, first.Pending.Cookies, "REPLACE", checkpoint);
            checkpoint.Phase = RecoveryCodeFirstCasePhase.STATE;
            checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
            await using var check = db.Context();
            checkpoint.Mark(RecoveryCodeFirstCaseStage.ACTIVE_SET_STATES);
            Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.State == "ACTIVE") == 1 && await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.State == "REPLACED") == 1);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.CAUSAL_RETIREMENT);
            Assert.True(await check.Set<RecoveryCodeSetRow>().AnyAsync(x => x.SetReference == first.Pending.Set && x.RetiredByOperationId == second.Pending.Operation && x.LifecycleVersion == 2 && x.UsageVersion == 0));
            checkpoint.Mark(RecoveryCodeFirstCaseStage.CONSUMED_PROOFS);
            Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == 2);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.DISPOSAL);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new RecoveryCodeFirstCaseFailure(checkpoint.Phase, checkpoint.Stage);
        }
    }

    public async Task PublicationChangeAfterOwnerWorkBeforeFinalDecisionRollsBackCodeSetProofConsumeAndOutbox(string change)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var proofExpiry = change == "proof-expiry";
        if (proofExpiry) profile.Policy = profile.Policy with { OperationLifetime = TimeSpan.FromMinutes(10) };
        await using var host = await Host(db, profile);
        if (proofExpiry) host.Policies.Policy = host.Policies.Policy with { IdleLifetime = TimeSpan.FromMinutes(10) };
        await host.SeedAsync(db);
        var pending = await Begin(host, await Login(host)); var readsBeforeProof = profile.PublicationReads; var injected = false;
        host.Access.BeforeDecision = request =>
        {
            if (injected || profile.PublicationReads <= readsBeforeProof || request.Operation != "SESSION_USE") return;
            injected = true;
            if (proofExpiry) host.Clock.Advance(TimeSpan.FromMinutes(6));
            else if (change == "withdrawal") profile.Published = false;
            else if (change == "version") profile.PublicationVersion = "fixture-recovery-publication-v2";
            else profile.PublicationInterval = new(Profile.Interval.From, true, host.Clock.GetUtcNow(), false);
        };
        using var response = await Prove(host, pending);
        Assert.True(injected && response.StatusCode != HttpStatusCode.OK);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(); Assert.True(!body!.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _));
        await using var check = db.Context();
        if (proofExpiry)
        {
            var at = host.Clock.GetUtcNow(); var session = await check.Set<AuthenticationSessionRow>().SingleAsync();
            var operation = await check.Set<RecoveryCodeOperationRow>().SingleAsync(); var proof = await check.Set<StepUpProofRow>().SingleAsync();
            Assert.True(session.State == "ACTIVE" && !AuthenticationApplication.Expired(session, host.Policies.Policy, at) && operation.ExpiresAt > at);
            Assert.True(proof.ExpiresAt - proof.AuthenticatedAt == TimeSpan.FromMinutes(5) && proof.ExpiresAt <= at);
            var expectedInterval = Profile.Interval;
            Assert.True(profile.Published && profile.PublicationVersion == "fixture-recovery-publication-v1" &&
                profile.PublicationInterval.From == expectedInterval.From && profile.PublicationInterval.IncludesFrom == expectedInterval.IncludesFrom &&
                profile.PublicationInterval.Until == expectedInterval.Until && profile.PublicationInterval.IncludesUntil == expectedInterval.IncludesUntil &&
                profile.FinalAdmissionChange == "unchanged");
        }
        Assert.True(!await check.Set<RecoveryCodeSetRow>().AnyAsync() && !await check.Set<RecoveryCodeEntryRow>().AnyAsync());
        Assert.True(await check.Set<RecoveryCodeOperationRow>().AllAsync(x => x.State == "PENDING_PROOF"));
        Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == 0);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "RecoveryCodeSetGenerated") == 0);
    }

    public async Task OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift(string change)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile { RetireFirstOnly = true };
        await using var host = await Host(db, profile); await host.SeedAsync(db);
        using var first = await Generate(host, await Login(host)); using var second = await Generate(host, first.Pending.Cookies);
        var pending = await Begin(host, second.Pending.Cookies, "REPLACE");
        var readsBeforeProof = profile.PublicationReads; var admissionsBeforeProof = profile.FinalAdmissionReads; var injected = false;
        host.Access.BeforeDecision = request =>
        {
            if (injected || profile.PublicationReads <= readsBeforeProof || request.Operation != "SESSION_USE") return;
            injected = true; profile.FinalAdmissionChange = change;
        };
        using var response = await Prove(host, pending); using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(injected && profile.FinalAdmissionReads > admissionsBeforeProof && profile.FinalFactsOriginal);
        var positive = change == "unchanged";
        Assert.True(positive ? response.StatusCode == HttpStatusCode.OK : response.StatusCode != HttpStatusCode.OK);
        Assert.True(body!.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out var delivery) == positive);
        if (positive) Assert.True(delivery.GetProperty("ownerRequestId").GetGuid() == pending.Request && delivery.GetProperty("setReference").GetGuid() == pending.Set && delivery.GetProperty("codes").GetArrayLength() == profile.Crypto.CodesPerSet);
        await using var check = db.Context();
        var operation = await check.Set<RecoveryCodeOperationRow>().SingleAsync(x => x.OperationId == pending.Operation);
        Assert.True(positive ? operation.State == "COMPLETED" && operation.Version == 2 : operation.State == "PENDING_PROOF" && operation.Version == 1 && operation.ProofReference == null && operation.CompletedAt == null && operation.FirstResponseCommittedAt == null);
        Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync() == (positive ? 3 : 2));
        Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.State == "ACTIVE") == 2);
        Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.State == "REPLACED") == (positive ? 1 : 0));
        Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync() == (positive ? 6 : 4));
        Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == (positive ? 3 : 2));
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "RecoveryCodeSetGenerated") == 2);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "RecoveryCodeSetReplaced") == (positive ? 1 : 0));
        if (!positive)
        {
            Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.State == "ACTIVE" && x.LifecycleVersion == 1 && x.UsageVersion == 0 && x.RetiredAt == null && x.RetiredByOperationId == null));
            Assert.True(await check.Set<StepUpProofRow>().AnyAsync(x => x.OperationId == "OP-RECOVERY-CODE-" + pending.Operation.ToString("N") && x.State == "ACTIVE"));
        }
        using var read = await host.Post("/account/recovery-codes/result", new { ownerRequestId = pending.Request, operationReference = pending.Operation.ToString("N") }, pending.Cookies, pending.Csrf);
        Assert.True(read.StatusCode == HttpStatusCode.OK); using var safe = await read.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(!safe!.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _));
    }

    private sealed class AccountOwner(Guid set, TimeProvider clock) : IRecoveryCodeConsumptionOwner
    {
        internal bool RejectAfterMutation { get; set; }
        internal bool ThrowAfterMutation { get; set; }
        internal CancellationTokenSource? CancelAfterMutation { get; set; }
        internal bool ProvisionalClaimObserved { get; private set; }
        internal bool FinalCurrent { get; set; } = true;
        internal IdentityPersistenceDbContext? ForeignDatabase { get; set; }
        internal bool WrongRequest { get; set; }
        internal RecoveryCodeUsageAdmission UsageAdmission { get; set; } = RecoveryCodeUsageAdmission.IndependentUnusedEntry;
        internal long? ExpectedUsageVersion { get; set; }
        internal bool KeepAccountActive { get; set; }
        internal Func<CancellationToken, Task>? AfterAdmission { get; set; }
        internal int Admissions { get; private set; }
        internal int Applications { get; private set; }
        private AccountRow? account;
        private Guid eventId;
        private bool priorObserved;
        public async Task<RecoveryCodeConsumptionAdmission?> AdmitAsync(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, CancellationToken ct)
        {
            Admissions++;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"fixture-recovery-owner:" + request.AccountId},0))", ct);
            account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM accounts.accounts WHERE account_id={request.AccountId} FOR NO KEY UPDATE").SingleAsync(ct);
            var prior = await db.Set<RecoveryCodeEntryRow>().AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == request.AccountId && x.ConsumedByOperationId == request.OwnerOperationId, ct);
            priorObserved = prior is not null && await db.Set<OutboxMessage>().AnyAsync(x => x.OperationId == request.OwnerOperationId && x.CorrelationId == request.CorrelationId, ct);
            var bound = WrongRequest ? new RecoveryCodeConsumptionRequest(request.AccountId, request.OwnerOperationId, request.CorrelationId, request.ContextDigest, request.Origin, request.NetworkDigest) : request;
            if (AfterAdmission is { } barrier) await barrier(ct);
            return new(ForeignDatabase ?? db, db.Database.CurrentTransaction!, bound, account, set, 1, UsageAdmission, ExpectedUsageVersion,
                "fixture-recovery-owner-v1", "fixture-owner-evidence", Profile.Interval, prior?.ConsumptionOutcomeReference);
        }
        public Task<RecoveryCodeConsumptionOwnerOutcome> ApplyAsync(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, RecoveryCodeConsumptionAdmission admission, CancellationToken ct)
        {
            Applications++;
            account!.State = KeepAccountActive ? AccountState.Active : AccountState.Restricted; account.Version++; account.RecordedAtUtc = clock.GetUtcNow(); account.OperationId = request.OwnerOperationId;
            account.CorrelationId = request.CorrelationId; account.EvidenceReference = "fixture-owner-result"; eventId = Guid.NewGuid();
            db.Add(new OutboxMessage(eventId, "1.0", "FixtureRecoveryOwnerApplied", "1", clock.GetUtcNow(), "tunner/identity", account.AccountId, "RESTRICTED_SECURITY",
                JsonSerializer.Serialize(new { reference = request.OwnerOperationId }), account.Version.ToString(CultureInfo.InvariantCulture), request.OwnerOperationId, request.CorrelationId));
            if (ThrowAfterMutation || CancelAfterMutation is not null)
            {
                ProvisionalClaimObserved = db.Database.CurrentTransaction is not null &&
                    db.ChangeTracker.Entries<RecoveryCodeEntryRow>().Any(x => x.Entity.ConsumedByOperationId == request.OwnerOperationId && x.Entity.ConsumedAt != null && x.Entity.Version == 2 && x.State == EntityState.Modified) &&
                    db.ChangeTracker.Entries<OutboxMessage>().Any(x => x.Entity.EventId == eventId && x.State == EntityState.Added);
                if (CancelAfterMutation is { } cancellation) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
                throw new PrivateOwnerFault();
            }
            return Task.FromResult(new RecoveryCodeConsumptionOwnerOutcome(db, db.Database.CurrentTransaction!, admission, !RejectAfterMutation, "fixture-owner-result", account.Version, account.State,
                "fixture-recovery-owner-v1", Profile.Interval, [eventId]));
        }
        public bool CurrentAt(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, RecoveryCodeConsumptionAdmission admission, RecoveryCodeConsumptionOwnerOutcome? outcome, DateTimeOffset at) =>
            FinalCurrent && Profile.Interval.Contains(at) && account is not null && ReferenceEquals(account, admission.Account) && account.AccountId == request.AccountId &&
            (outcome is null ? priorObserved && account.OperationId == request.OwnerOperationId && account.CorrelationId == request.CorrelationId :
                account.Version == outcome.AccountVersion && account.OperationId == request.OwnerOperationId && account.CorrelationId == request.CorrelationId &&
                db.ChangeTracker.Entries<OutboxMessage>().Any(x => x.Entity.EventId == eventId && x.State == EntityState.Added));
    }
    private static RecoveryCodeConsumptionRequest ConsumeRequest(string account) { var op = "OP-fixture-recovery-" + Guid.NewGuid().ToString("N"); return new(account, op, Guid.NewGuid().ToString("N"), AuthenticationIdentity.Digest(op), Origin, new('B', 64)); }

    public async Task SameCodeTwoOwnerOperationsCommitOnlyOneRealAccountMutationAndOutbox()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host));
        async Task<RecoveryCodeConsumptionResult> Consume()
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), new AccountOwner(winning.Pending.Set, host.Clock));
        }
        var results = await Task.WhenAll(Consume(), Consume()); Assert.True(results.Count(x => x.Consumed) == 1 && results.Count(x => x.ErrorCode == "RECOVERY_CODE_REJECTED") == 1);
        await using var check = db.Context(); Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 1);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "FixtureRecoveryOwnerApplied") == 1);
        Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.LifecycleVersion == 1 && x.UsageVersion == 1));
    }

    public async Task OwnerRejectionOrFinalDriftRollsBackClaimActualAccountEffectAndOutbox(bool drift)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var owner = new AccountOwner(winning.Pending.Set, host.Clock) { RejectAfterMutation = !drift, FinalCurrent = !drift };
            var result = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), owner);
            Assert.True(!result.Consumed && result.ErrorCode == "VERSION_CONFLICT");
        }
        await using var check = db.Context(); Assert.True(await check.Set<RecoveryCodeEntryRow>().AllAsync(x => x.ConsumedAt == null && x.Version == 1));
        Assert.True(await check.Accounts.AllAsync(x => x.Version == 1 && x.State == AccountState.Active));
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "FixtureRecoveryOwnerApplied") == 0);
        await using var retry = host.Services.CreateAsyncScope();
        var accepted = await retry.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), new AccountOwner(winning.Pending.Set, host.Clock));
        Assert.True(accepted.Consumed);
    }

    public async Task ConsumeRejectsNestedForeignOrWrongOwnerTransactionBeforeClaim()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host));
        await using (var nested = host.Services.CreateAsyncScope())
        {
            var database = nested.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>(); await using var tx = await database.Database.BeginTransactionAsync();
            var owner = new AccountOwner(winning.Pending.Set, host.Clock); var rejected = false;
            try { _ = await nested.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), owner); }
            catch (InvalidOperationException) { rejected = true; }
            Assert.True(rejected && owner.Admissions == 0);
        }
        await using var foreign = db.Context();
        foreach (var wrongRequest in new[] { false, true })
        {
            await using var scope = host.Services.CreateAsyncScope(); var owner = new AccountOwner(winning.Pending.Set, host.Clock) { ForeignDatabase = wrongRequest ? null : foreign, WrongRequest = wrongRequest }; var rejected = false;
            try { _ = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), owner); }
            catch (InvalidOperationException) { rejected = true; }
            Assert.True(rejected);
        }
        await using var check = db.Context(); Assert.True(await check.Set<RecoveryCodeEntryRow>().AllAsync(x => x.ConsumedAt == null));
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "FixtureRecoveryOwnerApplied") == 0);
    }

    public async Task SameOwnerDuplicateReadsOnlyOriginalSafeResultWithoutSecondCodeAcceptance()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host)); var request = ConsumeRequest(account);
        await using (var first = host.Services.CreateAsyncScope())
            Assert.True((await first.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(request, winning.Code.NativeInput(), new AccountOwner(winning.Pending.Set, host.Clock))).Consumed);
        await using (var second = host.Services.CreateAsyncScope())
        {
            var result = await second.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(request, null, new AccountOwner(winning.Pending.Set, host.Clock));
            Assert.True(result.Reconciled && !result.Consumed && result.ErrorCode is null);
        }
        await using (var wrongSet = host.Services.CreateAsyncScope())
        {
            var result = await wrongSet.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(request, null, new AccountOwner(Guid.NewGuid(), host.Clock));
            Assert.True(!result.Reconciled && !result.Consumed && result.ErrorCode == "VERSION_CONFLICT");
        }
        await using var check = db.Context(); Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 1);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "FixtureRecoveryOwnerApplied") == 1);
    }


    // Fixed private fixtures only. Existing owner declares each usage-admission choice explicitly.

    public async Task DifferentCodesLinearizeUnderSelectedOwnerUsageAdmissionWithoutASurvivingSession(bool exactUsage, bool sameOperation)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host));
        using (var signedOut = await host.Post("/authentication/sign-out", new { }, winning.Pending.Cookies, winning.Pending.Csrf)) Assert.True(signedOut.StatusCode == HttpStatusCode.OK);
        var firstRequest = ConsumeRequest(account); var secondRequest = sameOperation ? firstRequest : ConsumeRequest(account);
        async Task<RecoveryCodeConsumptionResult> Consume(RecoveryCodeConsumptionRequest request, RecoveryCodeValue code)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var owner = new AccountOwner(winning.Pending.Set, host.Clock) { UsageAdmission = exactUsage ? RecoveryCodeUsageAdmission.ExactUsageSnapshot : RecoveryCodeUsageAdmission.IndependentUnusedEntry, ExpectedUsageVersion = exactUsage ? 0 : null };
            return await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(request, code.NativeInput(), owner);
        }
        var results = await Task.WhenAll(Consume(firstRequest, winning.Codes[0]), Consume(secondRequest, winning.Codes[1]));
        var expected = exactUsage || sameOperation ? 1 : 2;
        Assert.True(results.Count(x => x.Consumed) == expected);
        Assert.True(sameOperation ? results.Count(x => x.Reconciled && !x.Consumed) == 1 : results.Count(x => !x.Consumed) == 2 - expected);
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == expected);
        Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt == null && x.Version == 1) == 2 - expected);
        Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.State == "ACTIVE" && x.LifecycleVersion == 1 && x.UsageVersion == expected));
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "FixtureRecoveryOwnerApplied") == expected);
        Assert.True(!await check.Set<AuthenticationSessionRow>().AnyAsync(x => x.State == "ACTIVE"));
        Assert.True(await check.Set<AuthenticationSessionRow>().CountAsync() == 1);
    }

    public async Task MalformedFailuresPersistSelectedAccountAndNetworkAttemptLimits(bool networkLimited)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        profile.Policy = profile.Policy with { ConsumptionAccountAttemptLimit = networkLimited ? 100 : 2, ConsumptionNetworkAttemptLimit = networkLimited ? 2 : 100 };
        await using var host = await Host(db, profile); var account = await host.SeedAsync(db); using var winning = await Generate(host, await Login(host));
        // Three distinct fixed candidates and two held codes guarantee one wrong canonical input.
        var wrongCanonical = new[] { new string('0', 32), new string('1', 32), new string('2', 32) }
            .First(candidate => winning.Codes.All(code => !code.Span.SequenceEqual(candidate.AsSpan())));
        foreach (var input in new[] { "not-a-code", wrongCanonical })
        {
            await using var scope = host.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), input, new AccountOwner(winning.Pending.Set, host.Clock));
            Assert.True(!result.Consumed && result.ErrorCode == "RECOVERY_CODE_REJECTED");
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), new AccountOwner(winning.Pending.Set, host.Clock));
            Assert.True(!result.Consumed && result.ErrorCode == "RATE_OR_ABUSE_LIMITED");
        }
        await using var check = db.Context();
        Assert.True(await check.Set<AuthenticationAbuseRow>().AnyAsync(x => x.Partition == "recovery-code:A:" + account && x.Count == 2));
        Assert.True(await check.Set<AuthenticationAbuseRow>().AnyAsync(x => x.Partition == "recovery-code:N:" + new string('B', 64) && x.Count == (networkLimited ? 2 : 3)));
        Assert.True(await check.Set<RecoveryCodeEntryRow>().AllAsync(x => x.ConsumedAt == null && x.Version == 1));
        Assert.True(!await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "FixtureRecoveryOwnerApplied"));
        Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Active && x.Version == 1));
    }

    public async Task ConcurrentReplacementsOfSameOriginalFactsReleaseOnlyOneGeneration()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host));
        var first = await Begin(host, winning.Pending.Cookies, "REPLACE"); var second = await Begin(host, winning.Pending.Cookies, "REPLACE");
        var firstProof = await PreparePasswordRequest(host, first); var secondProof = await PreparePasswordRequest(host, second);
        var firstTask = host.Post("/authentication/step-up/password", new { requestId = firstProof, password = Password }, first.Cookies, first.Csrf);
        var secondTask = host.Post("/authentication/step-up/password", new { requestId = secondProof, password = Password }, second.Cookies, second.Csrf);
        try
        {
            var replies = await Task.WhenAll(firstTask, secondTask);
            var disclosures = 0;
            foreach (var reply in replies) { using var body = await reply.Content.ReadFromJsonAsync<JsonDocument>(); if (body!.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _)) { Assert.True(reply.StatusCode == HttpStatusCode.OK); disclosures++; } }
            Assert.True(disclosures == 1);
            await using var check = db.Context();
            Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync() == 2);
            Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.State == "ACTIVE") == 1);
            Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.SetReference == winning.Pending.Set && x.State == "REPLACED" && x.LifecycleVersion == 2) == 1);
            Assert.True(await check.Set<RecoveryCodeOperationRow>().CountAsync(x => x.Kind == "REPLACE" && x.State == "COMPLETED") == 1);
            Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "RecoveryCodeSetReplaced") == 1);
            Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == 2);
        }
        finally
        {
            if (firstTask.IsCompletedSuccessfully) (await firstTask).Dispose();
            if (secondTask.IsCompletedSuccessfully) (await secondTask).Dispose();
        }
    }

    private static async Task WaitForActualDatabaseLockWait(IdentityTestDatabase db)
    {
        await using var check = db.Context();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var blocked = await check.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
            if (blocked) return;
            await Task.Delay(25);
        }
        Assert.Fail("Expected private fixture lock barrier was not reached.");
    }

    public async Task ConsumeAndReplacementRespectBothActualLockOrderings(bool replaceFirst)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host)); var pending = await Begin(host, winning.Pending.Cookies, "REPLACE"); var proofRequest = await PreparePasswordRequest(host, pending);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RecoveryCodeConsumptionResult>? consumeTask = null; Task<HttpResponseMessage>? proofTask = null; RecoveryCodeConsumptionResult? consumed = null; HttpResponseMessage? proofReply = null;
        try
        {
        async Task<RecoveryCodeConsumptionResult> Consume(AccountOwner owner)
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), owner);
        }
        Task<HttpResponseMessage> ProveHeld() => host.Post("/authentication/step-up/password", new { requestId = proofRequest, password = Password }, pending.Cookies, pending.Csrf);
        try
        {
            if (replaceFirst)
            {
                profile.BeforeAdmission = async (request, ct) => { if (request.Kind == "REPLACE") { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
                proofTask = ProveHeld(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                consumeTask = Consume(new AccountOwner(winning.Pending.Set, host.Clock) { KeepAccountActive = true });
            }
            else
            {
                consumeTask = Consume(new AccountOwner(winning.Pending.Set, host.Clock) { KeepAccountActive = true, AfterAdmission = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); } });
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); proofTask = ProveHeld();
            }
            await WaitForActualDatabaseLockWait(db);
        }
        finally
        {
            release.TrySetResult(); profile.BeforeAdmission = null;
            try { if (proofTask is not null) proofReply = await proofTask; }
            finally { if (consumeTask is not null) consumed = await consumeTask; }
        }
        using (proofReply)
        {
            Assert.True(proofReply is not null && consumed is not null);
            using var body = await proofReply!.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.True(body!.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _) == replaceFirst);
            Assert.True(replaceFirst ? !consumed!.Consumed && consumed.ErrorCode == "RECOVERY_CODE_REJECTED" : consumed!.Consumed);
            await using var check = db.Context();
            var original = await check.Set<RecoveryCodeSetRow>().SingleAsync(x => x.SetReference == winning.Pending.Set);
            Assert.True(replaceFirst ? original.State == "REPLACED" && original.LifecycleVersion == 2 && original.UsageVersion == 0 : original.State == "ACTIVE" && original.LifecycleVersion == 1 && original.UsageVersion == 1);
            Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "FixtureRecoveryOwnerApplied") == (replaceFirst ? 0 : 1));
            Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "RecoveryCodeSetReplaced") == (replaceFirst ? 1 : 0));
        }
        if (!replaceFirst)
        {
            using var fresh = await Generate(host, winning.Pending.Cookies, "REPLACE");
            await using var check = db.Context();
            Assert.True(await check.Set<RecoveryCodeSetRow>().AnyAsync(x => x.SetReference == winning.Pending.Set && x.State == "REPLACED" && x.UsageVersion == 1));
            Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.SetReference == winning.Pending.Set && x.ConsumedAt != null) == 1);
        }
        }
        finally { proofReply?.Dispose(); }
    }


    private static async Task<StepUpCaller> TrustedSignIn(AuthenticationTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var attempt = await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().SignInAsync(new(new string('A', 64), new string('B', 64), Origin, null), Command(Guid.NewGuid()), CancellationToken.None);
        Assert.True(attempt.ErrorCode is null && attempt.Grant is not null);
        return StepUpCaller.FromPrincipal(attempt.Grant!.Ticket.Principal, Origin, new('B', 64));
    }
    private static async Task<UserStepUpOperationContext> StartTrustedRecovery(AuthenticationTestHost host, StepUpCaller caller)
    {
        await using var scope = host.Services.CreateAsyncScope(); var application = scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
        var started = await application.StartAsync(caller, "GENERATE", new RecoveryCodeStartCommand { OwnerRequestId = Guid.NewGuid(), ExpectedVersion = 1 }, CancellationToken.None);
        Assert.True(started.ErrorCode is null && started.Operation is not null);
        var context = await application.ResolveAsync(caller, started.Operation!.OperationId, started.Operation.RequestId, CancellationToken.None);
        Assert.True(context is not null); return context!;
    }
    private static async Task<StepUpIssueResult> IssueTrustedRecoveryProof(AuthenticationTestHost host, StepUpCaller caller, UserStepUpOperationContext context, string password = Password)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StepUpApplication>().IssuePasswordAsync(caller, context.Binding, context.ExpectedPolicyVersion, password);
    }

    public async Task RecoveryOwnerCannotRunWithoutFreshSufficientUnconsumedProof(string state)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        if (state == "insufficient") profile.RequiredProofAssurance = "fixture-stronger-proof";
        await using var host = await Host(db, profile); await host.SeedAsync(db); var caller = await TrustedSignIn(host); var context = await StartTrustedRecovery(host, caller);
        StepUpProofGrant? grant = null;
        if (state != "absent")
        {
            var issued = await IssueTrustedRecoveryProof(host, caller, context, state == "wrong-password" ? "Incorrect synthetic password" : Password);
            Assert.True(state is "wrong-password" or "insufficient" ? issued.Grant is null && issued.ErrorCode is not null : issued.Grant is not null && issued.ErrorCode is null);
            grant = issued.Grant;
        }
        if (state == "expired") host.Clock.Advance(TimeSpan.FromMinutes(6));
        if (state == "consumed")
        {
            await using var firstScope = host.Services.CreateAsyncScope(); var firstApp = firstScope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
            using var first = await firstScope.ServiceProvider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, context.Binding, grant!.Key, ct => firstApp.ApplyAsync(caller, context, ct));
            Assert.True(first.Applied && first.HasRecoveryDelivery);
        }
        var callback = false;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var application = scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
            using var rejected = await scope.ServiceProvider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, context.Binding, grant?.Key ?? new string('C', 64), ct => { callback = true; return application.ApplyAsync(caller, context, ct); });
            Assert.True(!rejected.Applied && !rejected.HasRecoveryDelivery && !callback);
        }
        await using var check = db.Context(); var expected = state == "consumed" ? 1 : 0;
        Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync() == expected);
        Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync() == expected * 2);
        Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == expected);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "RecoveryCodeSetGenerated") == expected);
    }

    public async Task RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback(string change)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); await host.SeedAsync(db);
        var original = await TrustedSignIn(host); var context = await StartTrustedRecovery(host, original); var issued = await IssueTrustedRecoveryProof(host, original, context);
        Assert.True(issued.ErrorCode is null && issued.Grant is not null); var binding = context.Binding; var caller = original;
        if (change == "session") caller = await TrustedSignIn(host);
        if (change is "account" or "origin")
        {
            var account = change == "account" ? "ACC-unrelated-fixture-owner" : original.AccountId; var origin = change == "origin" ? "https://different.example.test" : original.Origin;
            var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, account), new System.Security.Claims.Claim(AuthenticationIdentity.SessionClaim, original.SessionId.ToString("N")), new System.Security.Claims.Claim(AuthenticationIdentity.OriginClaim, origin) };
            caller = StepUpCaller.FromPrincipal(new(new System.Security.Claims.ClaimsIdentity(claims, AuthenticationIdentity.Scheme)), origin, original.NetworkDigest);
        }
        var attempted = new StepUpOperation(change == "operation" ? "OP-other-fixture" : binding.OperationId, change == "correlation" ? "other-fixture-correlation" : binding.CorrelationId,
            new(change == "scope" ? "OTHER_FIXTURE_OPERATION" : binding.Scope.Operation, change == "context" ? "OTHER_FIXTURE_CONTEXT" : binding.Scope.Context),
            change == "resource" ? Guid.NewGuid().ToString("N") : binding.ResourceId, change == "version" ? binding.ExpectedVersion + 1 : binding.ExpectedVersion, change == "digest" ? new('E', 64) : binding.ContextDigest);
        var callback = false; var rejected = false;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var application = scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
            try
            {
                using var result = await scope.ServiceProvider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, attempted, change == "key" ? new('C', 64) : issued.Grant!.Key, ct => { callback = true; return application.ApplyAsync(original, context, ct); });
                rejected = !result.Applied && !result.HasRecoveryDelivery;
            }
            catch (ArgumentException) when (change == "origin") { rejected = true; }
        }
        Assert.True(rejected && !callback);
        await using var check = db.Context();
        Assert.True(!await check.Set<RecoveryCodeSetRow>().AnyAsync() && !await check.Set<RecoveryCodeEntryRow>().AnyAsync());
        Assert.True(await check.Set<StepUpProofRow>().AllAsync(x => x.State == "ACTIVE"));
        Assert.True(!await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "RecoveryCodeSetGenerated"));
    }

    public async Task RecoveryBffRejectsCrossOwnerCsrfOriginAndUnmappedAccountSelectorsWithoutEnumeration()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile);
        await host.SeedAsync(db); await host.SeedAsync(db, foreignEmail: true); var cookies = await Login(host); var pending = await Begin(host, cookies);
        using (var anonymous = await host.Get("/account/recovery-codes/options")) Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized);
        using (var missingCsrf = await host.Post("/account/recovery-codes/generate/start", new { ownerRequestId = Guid.NewGuid(), expectedVersion = 1 }, cookies, null)) Assert.True(missingCsrf.StatusCode == HttpStatusCode.BadRequest);
        using (var wrongOrigin = await host.Post("/account/recovery-codes/generate/start", new { ownerRequestId = Guid.NewGuid(), expectedVersion = 1 }, pending.Cookies, pending.Csrf, "https://different.example.test")) Assert.True(wrongOrigin.StatusCode == HttpStatusCode.Forbidden);
        using (var extra = await host.RawPost("/account/recovery-codes/generate/start", "{\"ownerRequestId\":\"" + Guid.NewGuid() + "\",\"expectedVersion\":1,\"accountId\":\"ACC-unrelated-fixture\"}", pending.Cookies, pending.Csrf)) Assert.True(extra.StatusCode == HttpStatusCode.BadRequest);
        using (var query = await host.Get("/account/recovery-codes/options?accountId=ACC-unrelated-fixture", pending.Cookies)) Assert.True(query.StatusCode == HttpStatusCode.BadRequest);
        foreach (var email in new[] { "person@example.test", "foreign@example.test" })
        {
            var otherCookies = await Login(host, email); using var options = await host.Get("/account/recovery-codes/options", otherCookies); Assert.True(options.StatusCode == HttpStatusCode.OK);
            using var doc = await options.Content.ReadFromJsonAsync<JsonDocument>(); otherCookies = Merge(otherCookies, options); var csrf = doc!.RootElement.GetProperty("csrfToken").GetString();
            using var existing = await host.Post("/account/recovery-codes/result", new { ownerRequestId = pending.Request, operationReference = pending.Operation.ToString("N") }, otherCookies, csrf);
            using var absent = await host.Post("/account/recovery-codes/result", new { ownerRequestId = Guid.NewGuid(), operationReference = Guid.NewGuid().ToString("N") }, otherCookies, csrf);
            Assert.True(existing.StatusCode == HttpStatusCode.NotFound && absent.StatusCode == existing.StatusCode);
            using var existingBody = await existing.Content.ReadFromJsonAsync<JsonDocument>(); using var absentBody = await absent.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.True(existingBody!.RootElement.GetProperty("error_code").GetString() == "NOT_FOUND_OR_NOT_DISCLOSABLE" && absentBody!.RootElement.GetProperty("error_code").GetString() == "NOT_FOUND_OR_NOT_DISCLOSABLE");
            Assert.True(!existingBody.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _) && !absentBody.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _));
        }
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "ACC-unrelated-fixture"), new System.Security.Claims.Claim(AuthenticationIdentity.SessionClaim, Guid.NewGuid().ToString("N")), new System.Security.Claims.Claim(AuthenticationIdentity.OriginClaim, Origin) };
        foreach (var scheme in new[] { "Admin", "Support", string.Empty })
        {
            var rejected = false; try { _ = StepUpCaller.FromPrincipal(new(new System.Security.Claims.ClaimsIdentity(claims, scheme)), Origin, new('B', 64)); } catch (ArgumentException) { rejected = true; }
            Assert.True(rejected);
        }
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryCodeOperationRow>().CountAsync() == 1);
        Assert.True(!await check.Set<RecoveryCodeSetRow>().AnyAsync() && !await check.Set<RecoveryCodeEntryRow>().AnyAsync() && !await check.Set<StepUpProofRow>().AnyAsync());
        Assert.True(!await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "RecoveryCodeSetGenerated"));
    }

    public async Task StableGenerateOrReplaceRequestRacesAndChangedBindingAreIdempotent(bool replace)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); await host.SeedAsync(db);
        using var previous = replace ? await Generate(host, await Login(host)) : null;
        var caller = await TrustedSignIn(host); var ownerRequest = Guid.NewGuid(); var kind = replace ? "REPLACE" : "GENERATE";
        async Task<RecoveryCodeOperationResult> StartOriginal(string requestedKind, long expectedVersion)
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().StartAsync(caller, requestedKind, new RecoveryCodeStartCommand { OwnerRequestId = ownerRequest, ExpectedVersion = expectedVersion }, CancellationToken.None);
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RecoveryCodeOperationResult>? first = null, second = null; RecoveryCodeOperationResult[] starts = [];
        try
        {
            profile.BeforeAdmission = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
            first = StartOriginal(kind, 1); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); second = StartOriginal(kind, 1);
            await WaitForActualDatabaseLockWait(db); release.TrySetResult(); starts = await Task.WhenAll(first, second);
        }
        finally
        {
            release.TrySetResult(); profile.BeforeAdmission = null;
            if (first is not null) try { await first; } catch (Exception error) when (error is not OutOfMemoryException) { }
            if (second is not null) try { await second; } catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        Assert.True(starts.Length == 2 && starts[0].ErrorCode is null && starts[0].Operation is not null && starts[1].ErrorCode is null && starts[1].Operation is not null);
        var original = starts[0].Operation!;
        Assert.True(starts[1].Operation!.OperationId == original.OperationId && starts[1].Operation!.SetReference == original.SetReference && starts[1].Operation!.ContextDigest == original.ContextDigest);
        var changedVersion = await StartOriginal(kind, 2); var changedKind = await StartOriginal(replace ? "GENERATE" : "REPLACE", 1);
        Assert.True(changedVersion.ErrorCode == "VERSION_CONFLICT" && changedKind.ErrorCode == "VERSION_CONFLICT");
        UserStepUpOperationContext context;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var resolved = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ResolveAsync(caller, original.OperationId, ownerRequest, CancellationToken.None);
            Assert.True(resolved is not null); context = resolved!;
        }
        var proof = await IssueTrustedRecoveryProof(host, caller, context); Assert.True(proof.ErrorCode is null && proof.Grant is not null);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var binding = context.Binding; var changed = new StepUpOperation(binding.OperationId, binding.CorrelationId, binding.Scope, binding.ResourceId, binding.ExpectedVersion, new('E', 64)); var callback = false;
            using var rejected = await scope.ServiceProvider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, changed, proof.Grant!.Key, _ => { callback = true; return Task.FromResult(UserStepUpOperationApplyOutcome.Conflict()); });
            Assert.True(!rejected.Applied && !rejected.HasRecoveryDelivery && !callback);
        }
        async Task<StepUpExecutionResult> Execute()
        {
            await using var scope = host.Services.CreateAsyncScope(); var recovery = scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
            return await scope.ServiceProvider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, context.Binding, proof.Grant!.Key, ct => recovery.ApplyAsync(caller, context, ct));
        }
        Task<StepUpExecutionResult>? firstExecution = null, secondExecution = null;
        try
        {
            firstExecution = Execute(); secondExecution = Execute(); var executions = await Task.WhenAll(firstExecution, secondExecution);
            Assert.True(executions.Count(x => x.Applied) == 1 && executions.Count(x => x.HasRecoveryDelivery) == 1 && executions.All(x => x.Applied || !x.HasRecoveryDelivery));
        }
        finally
        {
            // Retain and settle both owned tasks even if only one completed successfully with a disposable payload.
            if (firstExecution is not null) try { await firstExecution; } catch (Exception error) when (error is not OutOfMemoryException) { }
            if (secondExecution is not null) try { await secondExecution; } catch (Exception error) when (error is not OutOfMemoryException) { }
            if (firstExecution?.IsCompletedSuccessfully == true) (await firstExecution).Dispose();
            if (secondExecution?.IsCompletedSuccessfully == true) (await secondExecution).Dispose();
        }
        var safe = await StartOriginal(kind, 1); Assert.True(safe.ErrorCode is null && safe.Operation is { State: "COMPLETED" } completed && completed.OperationId == original.OperationId);
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryCodeOperationRow>().CountAsync(x => x.RequestId == ownerRequest) == 1);
        Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.IssuanceOperationId == original.OperationId) == 1);
        Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.SetReference == original.SetReference) == profile.Crypto.CodesPerSet);
        Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.OperationId == context.Binding.OperationId && x.State == "CONSUMED") == 1);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.OperationId == context.Binding.OperationId && x.EventType == (replace ? "RecoveryCodeSetReplaced" : "RecoveryCodeSetGenerated")) == 1);
    }

    public async Task ConsumeOwnerThrowOrCancellationRollsBackProvisionalClaim(bool cancel)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
        using var winning = await Generate(host, await Login(host)); using var cancellation = new CancellationTokenSource();
        var owner = new AccountOwner(winning.Pending.Set, host.Clock) { ThrowAfterMutation = !cancel, CancelAfterMutation = cancel ? cancellation : null }; var caught = false; var returned = false;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            try { _ = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), owner, cancellation.Token); returned = true; }
            catch (PrivateOwnerFault) when (!cancel) { caught = true; }
            catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { caught = true; }
        }
        Assert.True(caught && !returned && owner.ProvisionalClaimObserved);
        await using (var check = db.Context())
        {
            Assert.True(await check.Set<RecoveryCodeEntryRow>().AllAsync(x => x.ConsumedAt == null && x.Version == 1 && x.ConsumedByOperationId == null));
            Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.UsageVersion == 0 && x.LifecycleVersion == 1));
            Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Active && x.Version == 1));
            Assert.True(!await check.Set<OutboxMessage>().AnyAsync(x => x.EventType == "FixtureRecoveryOwnerApplied"));
        }
        await using var retry = host.Services.CreateAsyncScope();
        var accepted = await retry.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), winning.Code.NativeInput(), new AccountOwner(winning.Pending.Set, host.Clock));
        Assert.True(accepted.Consumed && !accepted.Reconciled && accepted.ErrorCode is null && accepted.OutcomeReference == "fixture-owner-result");
    }

    // The existing EF post-COMMIT observer is a simulated observation failure, not a physical network fault.
    private sealed class PostCommitObserverFailure(bool issuance) : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Fired { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Armed || Fired || eventData.Context is not IdentityPersistenceDbContext db) return Task.CompletedTask;
            var matches = issuance ? db.ChangeTracker.Entries<RecoveryCodeOperationRow>().Any(x => x.Entity.State == "COMPLETED") :
                db.ChangeTracker.Entries<RecoveryCodeEntryRow>().Any(x => x.Entity.ConsumedAt != null && x.Entity.ConsumptionOutcomeReference == "fixture-owner-result");
            if (!matches) return Task.CompletedTask;
            Fired = true; throw new PrivateCommitObservationFault();
        }
    }
    private sealed class PrivateCommitObservationFault : Exception;
    private sealed class PrivateOwnerFault : Exception;

    public async Task IssuancePostCommitObserverFailureReturnsNoPayloadAndOnlySafeOriginal(bool replace)
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var fault = new PostCommitObserverFailure(true);
        await using var host = await Host(db, profile, fault); await host.SeedAsync(db); using var previous = replace ? await Generate(host, await Login(host)) : null;
        var caller = await TrustedSignIn(host); var kind = replace ? "REPLACE" : "GENERATE"; var request = Guid.NewGuid(); UserStepUpOperationContext context;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var application = scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
            var started = await application.StartAsync(caller, kind, new RecoveryCodeStartCommand { OwnerRequestId = request, ExpectedVersion = 1 }, CancellationToken.None);
            Assert.True(started.ErrorCode is null && started.Operation is not null);
            var resolved = await application.ResolveAsync(caller, started.Operation!.OperationId, request, CancellationToken.None); Assert.True(resolved is not null); context = resolved!;
        }
        var proof = await IssueTrustedRecoveryProof(host, caller, context); Assert.True(proof.ErrorCode is null && proof.Grant is not null);
        fault.Armed = true; var caught = false; StepUpExecutionResult? returned = null;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var application = scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
            try { returned = await scope.ServiceProvider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, context.Binding, proof.Grant!.Key, ct => application.ApplyAsync(caller, context, ct)); }
            catch (PrivateCommitObservationFault) { caught = true; }
            finally { returned?.Dispose(); }
        }
        Assert.True(fault.Fired && caught && returned is null); // The application caller has no committed outcome/payload.
        await using (var check = db.Context())
        {
            Assert.True(await check.Set<RecoveryCodeOperationRow>().CountAsync(x => x.RequestId == request && x.State == "COMPLETED" && x.FirstResponseCommittedAt != null) == 1);
            Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.IssuanceOperationId == Guid.ParseExact(context.Binding.CorrelationId, "N")) == 1);
            Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.OperationId == context.Binding.OperationId && x.State == "CONSUMED") == 1);
            Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.OperationId == context.Binding.OperationId && x.EventType == (replace ? "RecoveryCodeSetReplaced" : "RecoveryCodeSetGenerated")) == 1);
        }
        await using var retry = host.Services.CreateAsyncScope(); var recovery = retry.ServiceProvider.GetRequiredService<RecoveryCodeApplication>();
        var safe = await recovery.ReadAsync(caller, Guid.ParseExact(context.Binding.CorrelationId, "N"), request, CancellationToken.None);
        Assert.True(safe is { State: "COMPLETED", FirstResponseCommittedAt: not null });
        var callback = false;
        using var repeated = await retry.ServiceProvider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, context.Binding, proof.Grant!.Key, ct => { callback = true; return recovery.ApplyAsync(caller, context, ct); });
        Assert.True(!repeated.Applied && !repeated.HasRecoveryDelivery && !callback);
    }

    public async Task ConsumePostCommitObserverFailureReconcilesOnlyOriginalSafeOutcome()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var fault = new PostCommitObserverFailure(false);
        await using var host = await Host(db, profile, fault); var account = await host.SeedAsync(db); using var winning = await Generate(host, await Login(host)); var request = ConsumeRequest(account);
        fault.Armed = true; var caught = false; RecoveryCodeConsumptionResult? returned = null;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            try { returned = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(request, winning.Code.NativeInput(), new AccountOwner(winning.Pending.Set, host.Clock)); }
            catch (PrivateCommitObservationFault) { caught = true; }
        }
        Assert.True(fault.Fired && caught && returned is null); // Actual outcome is unknown to this failed application call.
        await using (var check = db.Context())
        {
            Assert.True(await check.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedByOperationId == request.OwnerOperationId && x.ConsumedAt != null && x.Version == 2) == 1);
            Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(x => x.UsageVersion == 1 && x.LifecycleVersion == 1));
            Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Restricted && x.Version == 2 && x.OperationId == request.OwnerOperationId && x.CorrelationId == request.CorrelationId));
            var intent = await check.Set<OutboxMessage>().SingleAsync(x => x.EventType == "FixtureRecoveryOwnerApplied");
            using var payload = JsonDocument.Parse(intent.Payload);
            Assert.True(intent.Source == "tunner/identity" && intent.OperationId == request.OwnerOperationId && intent.CorrelationId == request.CorrelationId &&
                payload.RootElement.EnumerateObject().Count() == 1 && payload.RootElement.GetProperty("reference").GetString() == request.OwnerOperationId);
            await using var verifierScope = host.Services.CreateAsyncScope(); var native = verifierScope.ServiceProvider.GetRequiredService<RecoveryCodeNative>();
            var stored = await check.Set<RecoveryCodeEntryRow>().ToArrayAsync(); var verifierSafe = stored.All(x => native.AdmittedVerifier(x.Verifier));
            Assert.True(verifierSafe && !await check.UserTokens.AnyAsync(x => x.Name == "RecoveryCodes"));
        }
        await using (var retry = host.Services.CreateAsyncScope())
        {
            var safe = await retry.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(request, null, new AccountOwner(winning.Pending.Set, host.Clock));
            Assert.True(safe.Reconciled && !safe.Consumed && safe.ErrorCode is null && safe.OutcomeReference == "fixture-owner-result");
        }
        foreach (var change in new[] { "correlation", "context" })
        {
            var substituted = new RecoveryCodeConsumptionRequest(request.AccountId, request.OwnerOperationId, change == "correlation" ? Guid.NewGuid().ToString("N") : request.CorrelationId,
                change == "context" ? new('F', 64) : request.ContextDigest, request.Origin, request.NetworkDigest);
            await using var scope = host.Services.CreateAsyncScope();
            var conflict = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(substituted, null, new AccountOwner(winning.Pending.Set, host.Clock));
            Assert.True(!conflict.Consumed && !conflict.Reconciled && conflict.ErrorCode == "VERSION_CONFLICT");
        }
        await using var wrong = host.Services.CreateAsyncScope();
        var rejected = await wrong.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), null, new AccountOwner(winning.Pending.Set, host.Clock));
        Assert.True(!rejected.Consumed && !rejected.Reconciled && rejected.ErrorCode == "RECOVERY_CODE_REJECTED");
        await using var final = db.Context(); Assert.True(await final.Set<RecoveryCodeEntryRow>().CountAsync(x => x.ConsumedAt != null) == 1 && await final.Set<OutboxMessage>().CountAsync(x => x.EventType == "FixtureRecoveryOwnerApplied") == 1);
    }


    public async Task OrdinaryAuthenticatedHostDoesNotRegisterRecoveryRoutesOrOwnerByDefault()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); await using var host = await Start(db); await host.SeedAsync(db); var cookies = await Login(host);
        using var response = await host.Get("/account/recovery-codes/options", cookies);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound && host.Services.GetService<RecoveryCodeApplication>() is null);
    }

    public async Task CorruptedPersistedVerifierFailsClosedBeforeAnyOwnerApplication()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        await using var host = await Host(db, profile); var account = await host.SeedAsync(db); var pending = await Begin(host, await Login(host));
        // Deliberate stored fault fixture: same real owner/planned-set FKs and native profile,
        // exact candidate count, invalid nonfunctional84-character verifier. SQL guards stay enabled.
        await using (var seed = db.Context())
        {
            seed.Add(new RecoveryCodeSetRow { AccountId = account, SetReference = pending.Set, IssuanceOperationId = pending.Operation,
                State = "ACTIVE", CreatedAt = host.Clock.GetUtcNow(), PolicyVersion = profile.Policy.Version, CryptoProfileVersion = profile.Crypto.Version,
                ProcessingReference = profile.Policy.ProcessingReference, RetentionReference = profile.Policy.RetentionReference });
            for (var index = 0; index < profile.Crypto.CodesPerSet; index++)
                seed.Add(new RecoveryCodeEntryRow { AccountId = account, SetReference = pending.Set, EntryReference = Guid.NewGuid(),
                    Verifier = new('!', 84), CryptoProfileVersion = profile.Crypto.Version });
            await seed.SaveChangesAsync();
        }
        var owner = new AccountOwner(pending.Set, host.Clock); var rejected = false;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            try { _ = await scope.ServiceProvider.GetRequiredService<RecoveryCodeApplication>().ExecuteConsumptionOutcomeAsync(ConsumeRequest(account), new string('0', 32), owner); }
            catch (InvalidOperationException) { rejected = true; }
        }
        Assert.True(rejected && owner.Admissions == 1 && owner.Applications == 0);
        await using var check = db.Context(); var entries = await check.Set<RecoveryCodeEntryRow>().ToArrayAsync();
        using var native = new RecoveryCodeNative(profile.Crypto);
        Assert.True(entries.Length == profile.Crypto.CodesPerSet && entries.All(entry => entry.AccountId == account && entry.SetReference == pending.Set &&
            entry.CryptoProfileVersion == profile.Crypto.Version && !native.AdmittedVerifier(entry.Verifier) && entry.Version == 1 && entry.ConsumedAt == null && entry.ConsumedByOperationId == null));
        Assert.True(await check.Set<RecoveryCodeSetRow>().AllAsync(set => set.State == "ACTIVE" && set.LifecycleVersion == 1 && set.UsageVersion == 0));
        Assert.True(await check.Accounts.AllAsync(row => row.State == AccountState.Active && row.Version == 1));
        Assert.True(!await check.Set<OutboxMessage>().AnyAsync(row => row.EventType == "FixtureRecoveryOwnerApplied"));
        Assert.True(await check.Set<RecoveryCodeOperationRow>().AllAsync(operation => operation.State == "PENDING_PROOF" && operation.Version == 1 && operation.ProofReference == null));
        // Failure-counter policy is not inferred from a corrupted dependency row.
    }
}
