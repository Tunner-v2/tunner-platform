using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Private in-process native cohort. Never output a response/code/verifier/context/buffer value.
/// Execute only after root commits source, generates migration and qualifies the private native topology.</summary>
[Trait("Boundary", "PrivateRecoveryIntegration")]
public sealed class RecoveryCodePrivateIntegrationTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    internal sealed class Profile : IRecoveryCodePolicySource, IRecoveryCodeMutationAdmissionSource, IRecoveryCodePresentationSource, IAssurancePolicySource, IStepUpPresentationSource
    {
        // All values below are complete finite SYNTHETIC-TEST-ONLY inputs, never production policy defaults.
        internal RecoveryCodeCryptoProfile Crypto { get; } = RecoveryCodeNativeTests.Profile();
        internal RecoveryCodePolicy Policy { get; set; }
        internal bool Published { get; set; } = true;
        internal string PublicationVersion { get; set; } = "fixture-recovery-publication-v1";
        internal int PublicationReads { get; private set; }
        internal bool RetireFirstOnly { get; set; }
        internal string FinalAdmissionChange { get; set; } = "unchanged";
        internal int FinalAdmissionReads { get; private set; }
        internal bool FinalFactsOriginal { get; private set; }
        internal EffectivePolicyInterval PublicationInterval { get; set; } = Interval;
        internal static EffectivePolicyInterval Interval => new(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true, new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false);
        internal Profile() => Policy = new("fixture-recovery-v1", Origin, Crypto, "NATIVE_HEX32_V1", "fixture-recovery-delivery-v1", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
            2, 8, RecoveryCodeGenerationAdmission.WithinApprovedActiveSetCapacity, RecoveryCodeReplacementMechanism.RetireExactExpectedSetsAtIssueCommit,
            RecoveryCodeSessionEffect.NoSessionMutation, RecoveryCodeDeliveryMechanism.OneVolatileViewWithOptionalSingleLocalDownload, "fixture-recovery-proof", "fixture-recovery-proof",
            20, 100, "fixture-processing", "fixture-retention");
        public RecoveryCodePolicyVersion RequireEffective(DateTimeOffset at) => new(new(Policy.Version, "ACTIVE", "RECOVERY_CODES", Interval, null, ["SYNTHETIC-TEST-ONLY"]), Policy);
        public ValueTask<RecoveryCodeMutationAdmission> RequireAsync(RecoveryCodeMutationAdmissionRequest request, CancellationToken ct) => ValueTask.FromResult(Decide(request));
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
            return new(new("fixture-recovery-proof", "ACTIVE", scope, Interval, null, ["SYNTHETIC-TEST-ONLY"]), "fixture-proof", TimeSpan.FromMinutes(5), new Dictionary<string, string> { ["pwd"] = "fixture-proof" });
        }
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy) => new StepUpBrowserTests.Presentation().RequirePublished(policy);
    }
    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Profile profile) => Start(db, services =>
    {
        services.AddSingleton(profile.Crypto); services.AddSingleton<IRecoveryCodePolicySource>(profile); services.AddSingleton<IRecoveryCodeMutationAdmissionSource>(profile);
        services.AddSingleton<IRecoveryCodePresentationSource>(profile); services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
    });
    private sealed class Pending(Guid request, Guid operation, Guid set, string cookies, string csrf) // Safe refs only; no values in auto-record printing.
    {
        internal Guid Request { get; } = request; internal Guid Operation { get; } = operation; internal Guid Set { get; } = set;
        internal string Cookies { get; set; } = cookies; internal string Csrf { get; set; } = csrf;
        public override string ToString() => nameof(Pending);
    }
    private sealed class Winning(Pending pending, RecoveryCodeValue code) : IDisposable
    {
        internal Pending Pending { get; } = pending; internal RecoveryCodeValue Code { get; } = code;
        public void Dispose() => Code.Dispose(); public override string ToString() => nameof(Winning);
    }
    private static async Task<string> Login(AuthenticationTestHost host)
    {
        var boot = await host.Options(); using var response = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.True(response.StatusCode == HttpStatusCode.OK); return Merge(boot.Cookies, response);
    }
    private static async Task<Pending> Begin(AuthenticationTestHost host, string cookies, string kind = "GENERATE")
    {
        using var options = await host.Get("/account/recovery-codes/options", cookies); Assert.True(options.StatusCode == HttpStatusCode.OK);
        using var doc = await options.Content.ReadFromJsonAsync<JsonDocument>(); var body = doc!.RootElement;
        var request = body.GetProperty("ownerRequestId").GetGuid(); var version = body.GetProperty("expectedVersion").GetInt64(); var csrf = body.GetProperty("csrfToken").GetString()!;
        cookies = Merge(cookies, options);
        using var response = await host.Post("/account/recovery-codes/" + kind.ToLowerInvariant() + "/start", new { ownerRequestId = request, expectedVersion = version }, cookies, csrf);
        Assert.True(response.StatusCode == HttpStatusCode.OK); using var started = await response.Content.ReadFromJsonAsync<JsonDocument>(); var root = started!.RootElement;
        var pending = new Pending(request, Guid.ParseExact(root.GetProperty("operationReference").GetString()!, "N"), root.GetProperty("setReference").GetGuid(), Merge(cookies, response), csrf);
        using var current = await host.Get("/account/recovery-codes/options", pending.Cookies); Assert.True(current.StatusCode == HttpStatusCode.OK);
        using var currentDoc = await current.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(currentDoc!.RootElement.GetProperty("recovery").GetProperty("impact").GetProperty("canContinue").GetBoolean());
        pending.Cookies = Merge(pending.Cookies, current); pending.Csrf = currentDoc.RootElement.GetProperty("csrfToken").GetString()!;
        return pending;
    }
    private static async Task<HttpResponseMessage> Prove(AuthenticationTestHost host, Pending pending)
    {
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies); Assert.True(options.StatusCode == HttpStatusCode.OK);
        using var doc = await options.Content.ReadFromJsonAsync<JsonDocument>(); var root = doc!.RootElement;
        Assert.True(root.GetProperty("operationReference").GetString() == pending.Operation.ToString("N"));
        pending.Cookies = Merge(pending.Cookies, options); pending.Csrf = root.GetProperty("csrfToken").GetString()!;
        return await host.Post("/authentication/step-up/password", new { requestId = root.GetProperty("requestId").GetGuid(), password = Password }, pending.Cookies, pending.Csrf);
    }
    private static async Task<Winning> Generate(AuthenticationTestHost host, string cookies, string kind = "GENERATE")
    {
        var pending = await Begin(host, cookies, kind); using var response = await Prove(host, pending);
        Assert.True(response.StatusCode == HttpStatusCode.OK);
        var resultCookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList())
            .SingleOrDefault(x => x.Name.Value == RecoveryCodeStepUpOwner.ResultCookieName);
        Assert.True(resultCookie?.MaxAge == TimeSpan.FromMinutes(10)); // Explicit finite profile, measured against the controlled host clock.
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(); var root = doc!.RootElement;
        var delivery = root.GetProperty("recoveryCodeFirstDelivery");
        Assert.True(delivery.GetProperty("ownerRequestId").GetGuid() == pending.Request && delivery.GetProperty("setReference").GetGuid() == pending.Set);
        Assert.True(delivery.GetProperty("codes").GetArrayLength() == 2 && !root.GetProperty("signInRequired").GetBoolean());
        pending.Cookies = Merge(pending.Cookies, response);
        return new(pending, new(delivery.GetProperty("codes")[0].GetString()!.ToCharArray()));
    }

    [Fact]
    public async Task WinningDeliveryIsAbsentFromEverySafeOriginalReadAndReplacementRetiresExactPriorSet()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await Host(db, profile); await host.SeedAsync(db);
        var cookies = await Login(host); using var first = await Generate(host, cookies);
        using var read = await host.Post("/account/recovery-codes/result", new { ownerRequestId = first.Pending.Request, operationReference = first.Pending.Operation.ToString("N") }, first.Pending.Cookies, first.Pending.Csrf);
        Assert.True(read.StatusCode == HttpStatusCode.OK); using var result = await read.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(result!.RootElement.GetProperty("codesUnavailable").GetBoolean() && !result.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _));
        using var second = await Generate(host, first.Pending.Cookies, "REPLACE");
        await using var check = db.Context();
        Assert.True(await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.State == "ACTIVE") == 1 && await check.Set<RecoveryCodeSetRow>().CountAsync(x => x.State == "REPLACED") == 1);
        Assert.True(await check.Set<RecoveryCodeSetRow>().AnyAsync(x => x.SetReference == first.Pending.Set && x.RetiredByOperationId == second.Pending.Operation && x.LifecycleVersion == 2 && x.UsageVersion == 0));
        Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == 2);
    }

    [Theory]
    [InlineData("withdrawal")]
    [InlineData("version")]
    [InlineData("interval")]
    public async Task PublicationChangeAfterOwnerWorkBeforeFinalDecisionRollsBackCodeSetProofConsumeAndOutbox(string change)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await Host(db, profile); await host.SeedAsync(db);
        var pending = await Begin(host, await Login(host)); var readsBeforeProof = profile.PublicationReads; var injected = false;
        host.Access.BeforeDecision = request =>
        {
            if (injected || profile.PublicationReads <= readsBeforeProof || request.Operation != "SESSION_USE") return;
            injected = true;
            if (change == "withdrawal") profile.Published = false;
            else if (change == "version") profile.PublicationVersion = "fixture-recovery-publication-v2";
            else profile.PublicationInterval = new(Profile.Interval.From, true, host.Clock.GetUtcNow(), false);
        };
        using var response = await Prove(host, pending);
        Assert.True(injected && response.StatusCode != HttpStatusCode.OK);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(); Assert.True(!body!.RootElement.TryGetProperty("recoveryCodeFirstDelivery", out _));
        await using var check = db.Context();
        Assert.True(!await check.Set<RecoveryCodeSetRow>().AnyAsync() && !await check.Set<RecoveryCodeEntryRow>().AnyAsync());
        Assert.True(await check.Set<RecoveryCodeOperationRow>().AllAsync(x => x.State == "PENDING_PROOF"));
        Assert.True(await check.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == 0);
        Assert.True(await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "RecoveryCodeSetGenerated") == 0);
    }

    [Theory]
    [InlineData("withdrawal")]
    [InlineData("policy")]
    [InlineData("evidence")]
    [InlineData("target")]
    [InlineData("interval")]
    [InlineData("missing")]
    [InlineData("unchanged")]
    public async Task OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift(string change)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile { RetireFirstOnly = true };
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
        internal bool FinalCurrent { get; set; } = true;
        internal IdentityPersistenceDbContext? ForeignDatabase { get; set; }
        internal bool WrongRequest { get; set; }
        internal int Admissions { get; private set; }
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
            return new(ForeignDatabase ?? db, db.Database.CurrentTransaction!, bound, account, set, 1, RecoveryCodeUsageAdmission.IndependentUnusedEntry, null,
                "fixture-recovery-owner-v1", "fixture-owner-evidence", Profile.Interval, prior?.ConsumptionOutcomeReference);
        }
        public Task<RecoveryCodeConsumptionOwnerOutcome> ApplyAsync(IdentityPersistenceDbContext db, RecoveryCodeConsumptionRequest request, RecoveryCodeConsumptionAdmission admission, CancellationToken ct)
        {
            account!.State = AccountState.Restricted; account.Version++; account.RecordedAtUtc = clock.GetUtcNow(); account.OperationId = request.OwnerOperationId;
            account.CorrelationId = request.CorrelationId; account.EvidenceReference = "fixture-owner-result"; eventId = Guid.NewGuid();
            db.Add(new OutboxMessage(eventId, "1.0", "FixtureRecoveryOwnerApplied", "1", clock.GetUtcNow(), "tunner/identity", account.AccountId, "RESTRICTED_SECURITY",
                JsonSerializer.Serialize(new { reference = request.OwnerOperationId }), account.Version.ToString(CultureInfo.InvariantCulture), request.OwnerOperationId, request.CorrelationId));
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

    [Fact]
    public async Task SameCodeTwoOwnerOperationsCommitOnlyOneRealAccountMutationAndOutbox()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerRejectionOrFinalDriftRollsBackClaimActualAccountEffectAndOutbox(bool drift)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
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

    [Fact]
    public async Task ConsumeRejectsNestedForeignOrWrongOwnerTransactionBeforeClaim()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
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

    [Fact]
    public async Task SameOwnerDuplicateReadsOnlyOriginalSafeResultWithoutSecondCodeAcceptance()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await Host(db, profile); var account = await host.SeedAsync(db);
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

    [Fact]
    public async Task OrdinaryAuthenticatedHostDoesNotRegisterRecoveryRoutesOrOwnerByDefault()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db); var cookies = await Login(host);
        using var response = await host.Get("/account/recovery-codes/options", cookies);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound && host.Services.GetService<RecoveryCodeApplication>() is null);
    }
}
