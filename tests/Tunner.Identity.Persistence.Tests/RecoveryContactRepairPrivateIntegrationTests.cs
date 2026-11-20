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

// Existing finite literal dispatch only. All codes, destinations, rows and credentials stay in this private process.
internal sealed partial class RecoveryContactRepairPrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Public = "SYNTHETIC-TEST-ONLY";
    private const string Replacement = "a synthetic verified contact recovery replacement passphrase";
    private const string Pointer = "__Host-Tunner.Recovery.Original";
    private static readonly string[] Unresolved = ["ACCESS_RESTORATION", "NOTIFICATION_DELIVERY"];
    private static readonly string[] AdmissionChanges = ["stronger", "codes", "risk-case", "risk-context"];
    private static readonly string[] Changes = ["risk", "hierarchy", "policy", "notification", "blocklist-await", "stronger", "codes", "risk-case", "risk-context", "contact"];
    private static readonly string[] Selectors = ["accountId", "sessionId", "contactReference", "destination", "riskScore", "proofKind"];
    private static readonly bool[] FailureModes = [false, true];
    private sealed class Authority : IRecoveryPasswordPolicySource, IRecoveryCaseHierarchySource, IRegistrationPasswordBlocklist,
        IRecoveryContactRiskSource, IRecoveryContactNotificationSource
    {
        internal RecoveryPasswordPolicy Policy { get; set; } = new("fixture-contact-repair-v1", Origin, "fixture-contact-password", 15, 128, 10_000,
            RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
            32, 100, 100, Unresolved, "fixture-processing", "fixture-retention")
        { ContactRecoveryProfile = new("fixture-contact-v1", 6, TimeSpan.FromMinutes(1), 3, TimeSpan.FromMinutes(2), "fixture-risk-v1", "fixture-notification-contract-v1") };
        internal bool RiskAllowed { get; set; } = true;
        internal bool StrongUnavailable { get; set; } = true;
        internal bool CodesUnavailable { get; set; } = true;
        internal bool ForeignRiskCase { get; set; }
        internal bool ForeignRiskContext { get; set; }
        internal string HierarchyEvidence { get; set; } = "fixture-qualified-unavailable-strong-and-codes";
        internal string TemplateVersion { get; set; } = "fixture-contact-template-v1";
        internal Action? BeforePolicy { get; set; }
        internal Action? BeforeRisk { get; set; }
        internal Action? BeforeHierarchy { get; set; }
        internal Action? BeforeNotification { get; set; }
        internal Func<CancellationToken, Task>? BeforeBlocklist { get; set; }
        private static EffectivePolicyInterval Interval => RecoveryCodePrivateIntegrationTests.Profile.Interval;
        public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
        { BeforePolicy?.Invoke(); return new(new(Policy.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", Interval, null, [Public]), Policy); }
        public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at)
        {
            BeforeHierarchy?.Invoke(); var contact = request.VerifiedContact;
            return new(false, StrongUnavailable, "fixture-contact-hierarchy-v1", HierarchyEvidence, request.Facts.Digest(), Interval)
            { ContactSelection = contact is null ? null : new(contact.ContactReference, contact.Version, Policy.ContactRecoveryProfile!.Version, CodesUnavailable) };
        }
        public RecoveryContactRiskDecision RequireCurrent(RecoveryContactRiskRequest request, DateTimeOffset at)
        {
            BeforeRisk?.Invoke(); return new(ForeignRiskCase ? Guid.Empty : request.CaseId, request.RequestId, request.AccountId,
                ForeignRiskContext ? (request.ContextDigest[0] == '0' ? "1" : "0") + request.ContextDigest[1..] : request.ContextDigest, request.Contact.ContactReference,
                request.Contact.Version, request.Contact.DestinationDigest, request.Facts.Digest(), request.ExpectedRiskPolicyVersion, "fixture-risk-decision",
                RiskAllowed ? "ALLOW" : "DENY", "fixture-risk-evidence", Interval);
        }
        public RecoveryContactNotificationDecision RequireCurrent(RecoveryContactNotificationRequest request, DateTimeOffset at)
        {
            BeforeNotification?.Invoke(); return new(request.CaseId, request.ChallengeId, request.AccountId, request.ContactReference, request.ContactVersion,
                request.DestinationDigest, request.ContextDigest, request.ContractReference, "MANDATORY_SECURITY", "EMAIL", TemplateVersion,
                "fixture-code-variable", "en", "fixture-current-verified-recipient", "fixture-resolved-local-route", Public,
                request.ProcessingReference, request.RetentionReference, Interval);
        }
        public async ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken)
        { if (BeforeBlocklist is { } action) await action(cancellationToken); return false; }
        internal void Reset()
        {
            Policy = Policy with { Version = "fixture-contact-repair-v1" }; RiskAllowed = true;
            StrongUnavailable = true; CodesUnavailable = true; ForeignRiskCase = false; ForeignRiskContext = false;
            HierarchyEvidence = "fixture-qualified-unavailable-strong-and-codes"; TemplateVersion = "fixture-contact-template-v1";
            BeforePolicy = null; BeforeRisk = null; BeforeHierarchy = null; BeforeNotification = null; BeforeBlocklist = null;
        }
        public override string ToString() => nameof(Authority);
    }
    private sealed class Presentation : IRecoveryPasswordPresentationSource
    {
        private static readonly string[] Keys = ["fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value"];
        public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at)
        {
            var follow = policy.UnresolvedDownstreamEffects.Select((reference, i) => new RecoveryPasswordFollowUp(reference, "fixture.follow-label-" + i, "fixture.follow-value-" + i)).ToArray();
            var keys = Keys.Concat(follow.SelectMany(x => new[] { x.LabelKey, x.ValueKey }));
            return new("en", keys.Select(key => new RegistrationPublishedText(key, "fixture-variant", "fixture-text-v1", Public, "en", "PUBLISHED", "Synthetic test only")),
                "fixture-contact-publication-v1", RecoveryCodePrivateIntegrationTests.Profile.Interval, Keys[0], Keys[1], Keys[2], Keys[3], follow);
        }
    }
    private sealed class Notices : IRecoveryContactNotificationDispatcher
    {
        private readonly Dictionary<Guid, RecoveryContactNotification> messages = [];
        internal bool FailNext { get; set; }
        internal int Calls { get; private set; }
        internal int Count => messages.Count;
        internal Func<RecoveryContactNotification, CancellationToken, Task>? Observe { get; set; }
        public async Task QueueAsync(RecoveryContactNotification notification, CancellationToken cancellationToken)
        {
            Calls++; if (Observe is { } observe) await observe(notification, cancellationToken);
            if (FailNext) { FailNext = false; throw new InvalidOperationException("FINITE_CONTACT_QUEUE_UNAVAILABLE"); }
            if (messages.TryGetValue(notification.DeliveryId, out var prior))
                Assert.True(prior.ChallengeId == notification.ChallengeId && prior.Code == notification.Code && prior.Address == notification.Address);
            else messages.Add(notification.DeliveryId, notification);
        }
        internal string Code(Guid challenge) => messages.Values.Single(x => x.ChallengeId == challenge).Code;
        public override string ToString() => nameof(Notices);
    }
    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Authority authority, Notices notices, SaveChangesInterceptor? observer = null) =>
        Start(db, services =>
        {
            services.AddSingleton<IRecoveryPasswordPolicySource>(authority); services.AddSingleton<IRecoveryCaseHierarchySource>(authority);
            services.AddSingleton<IRegistrationPasswordBlocklist>(authority); services.AddSingleton<IRecoveryPasswordPresentationSource, Presentation>();
            services.AddSingleton<IRecoveryContactRiskSource>(authority); services.AddSingleton<IRecoveryContactNotificationSource>(authority);
            services.AddSingleton<IRecoveryContactNotificationDispatcher>(notices);
            if (observer is not null) services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(observer));
        });
    private sealed record Seed(string Account, Guid Contact, Guid Factor, string Cookies, RecoveryEntryCaller Caller)
    { public override string ToString() => nameof(Seed); }
    private static async Task<Seed> SeedAsync(IdentityTestDatabase db, AuthenticationTestHost host)
    {
        var account = await host.SeedAsync(db, minor: true); var boot = await host.Options();
        using var loggedIn = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.True(loggedIn.StatusCode == HttpStatusCode.OK);
        var factor = Guid.NewGuid(); var now = host.Clock.GetUtcNow(); await using var context = db.Context();
        context.Add(new TotpAuthenticatorRow { AuthenticatorReference = factor, AccountId = account, Generation = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(),
            SecretStoreReference = "fixture-unavailable-vault", SecretResourceReference = "fixture-factor-" + factor.ToString("N"), SecretVersion = 1,
            SecretBindingVersion = Public, State = "ACTIVE", CreatedAt = now, ActivatedAt = now, PolicyVersion = Public, ProcessingReference = Public, RetentionReference = Public });
        await context.SaveChangesAsync(); var contact = await context.VerifiedContacts.Where(x => x.AccountId == account).Select(x => x.ContactId).SingleAsync();
        return new(account, contact, factor, Merge(boot.Cookies, loggedIn), new(AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N")), new('C', 64), Origin, new('D', 64)));
    }
    private sealed record Baseline(string Hash, string Stamp, int Sessions, int Outbox)
    { public override string ToString() => nameof(Baseline); }
    private static async Task<Baseline> Snapshot(IdentityTestDatabase db, Seed seed)
    {
        await using var context = db.Context(); var user = await context.Users.SingleAsync(x => x.AccountId == seed.Account);
        return new(user.PasswordHash!, user.SecurityStamp!, await context.Set<AuthenticationSessionRow>().CountAsync(), await context.Set<OutboxMessage>().CountAsync());
    }
    private sealed record Pending(Guid Request, Guid Challenge, Guid Delivery)
    { public override string ToString() => nameof(Pending); }
    private static RecoveryCaseApplication Application(IServiceProvider services) => services.GetRequiredService<RecoveryCaseApplication>();
    private static async Task<Pending> Begin(IdentityTestDatabase db, AuthenticationTestHost host, Authority authority, RecoveryEntryCaller caller)
    {
        var request = Guid.NewGuid(); await using var scope = host.Services.CreateAsyncScope(); var app = Application(scope.ServiceProvider);
        var entry = await app.StartPasswordEntryAsync(caller, request, "person@example.test", authority.Policy.GoalReference);
        Assert.True(entry.ErrorCode is null && entry.Case is { ProofKind: "CONTACT_RISK", State: "PENDING_CONTACT_RISK", Version: 1 });
        var options = await app.StartPasswordContactAsync(caller, request); Assert.True(options.ErrorCode is null && options.Presentation is not null);
        await using var check = db.Context(); var challenge = await check.Set<RecoveryContactChallengeRow>().SingleAsync(x => x.ChallengeId == options.Presentation!.ChallengeId);
        return new(request, challenge.ChallengeId, challenge.DeliveryId);
    }
    private static async Task<bool> Dispatch(AuthenticationTestHost host, Guid delivery)
    { await using var scope = host.Services.CreateAsyncScope(); return await Application(scope.ServiceProvider).DispatchContactNotificationAsync(delivery); }
    private static async Task<RecoveryEntryResult> Repair(AuthenticationTestHost host, RecoveryEntryCaller caller, Pending pending, string? code,
        long version = 1, string? password = Replacement, string? confirmation = Replacement, CancellationToken ct = default)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await Application(scope.ServiceProvider).RepairPasswordWithContactAsync(caller, pending.Request, version, pending.Challenge, code, password, confirmation, ct);
    }
    private static string Wrong(string code) => (code[0] == '0' ? "1" : "0") + code[1..];
    private static async Task Unrepaired(IdentityTestDatabase db, Seed seed, Baseline before)
    {
        Assert.True(await Snapshot(db, seed) == before); await using var check = db.Context();
        Assert.True(!await check.Set<RecoveryResultRow>().AnyAsync() && await check.Set<RecoveryEntryRow>().AllAsync(x => x.State == "OPEN" && x.Version == 1));
        Assert.True(await check.Set<RecoveryCaseRow>().AllAsync(x => x.State == "PENDING_CONTACT_RISK" && x.Version == 1));
        Assert.True(await check.Set<RecoveryContactChallengeRow>().AllAsync(x => x.State == "PENDING" && x.Attempts == 0 && x.Version == 1 && x.ResultId == null));
    }
    internal async Task ContactProofRepairsPasswordAndQueuesCommittedDeduplicatedNotice()
    {
        var checkpoint = new RecoveryCodeFirstCaseCheckpoint();
        try
        {
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DATABASE);
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var notices = new Notices();
        checkpoint.Mark(RecoveryCodeFirstCaseStage.HOST);
        await using var host = await Host(db, authority, notices);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.SEED);
        var seed = await SeedAsync(db, host);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
        var before = await Snapshot(db, seed);
        checkpoint.Phase = RecoveryCodeFirstCasePhase.GENERATE;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.START_POST);
        var pending = await Begin(db, host, authority, seed.Caller);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.START_STATUS);
        Assert.True(notices.Count == 0);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var ownerDb = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            notices.Observe = async (notice, ct) =>
            {
                checkpoint.Phase = RecoveryCodeFirstCasePhase.SAFE_READ;
                checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
                Assert.True(ownerDb.Database.CurrentTransaction is null); await using var committed = db.Context();
                checkpoint.Mark(RecoveryCodeFirstCaseStage.ACTIVE_SET_STATES);
                Assert.True(await committed.Set<RecoveryContactChallengeRow>().AnyAsync(x => x.ChallengeId == pending.Challenge && x.State == "PENDING", ct));
                checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BINDING);
                Assert.True(await committed.Set<OutboxMessage>().AnyAsync(x => x.EventType == "IdentitySecurityNotificationRequested" && x.Subject == notice.DeliveryId.ToString("N"), ct));
            };
            notices.FailNext = true; var unavailable = false;
            checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
            try { _ = await Application(scope.ServiceProvider).DispatchContactNotificationAsync(pending.Delivery); }
            catch (InvalidOperationException error) when (error.Message == "FINITE_CONTACT_QUEUE_UNAVAILABLE") { unavailable = true; }
            checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_STATUS);
            Assert.True(unavailable && notices.Count == 0);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BODY);
            Assert.True(await Application(scope.ServiceProvider).DispatchContactNotificationAsync(pending.Delivery));
            checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_COUNT);
            Assert.True(await Application(scope.ServiceProvider).DispatchContactNotificationAsync(pending.Delivery)); notices.Observe = null;
        }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RETAIN_CODES);
        Assert.True(notices.Count == 1 && notices.Calls == 3); var code = notices.Code(pending.Challenge);
        checkpoint.Phase = RecoveryCodeFirstCasePhase.REPLACE;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.PASSWORD_PROOF_POST);
        var repaired = await Repair(host, seed.Caller, pending, code);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.PROOF_STATUS);
        Assert.True(repaired.ErrorCode is null && repaired.Result is { ProofKind: "CONTACT_RISK", Outcome: "PASSWORD_REPAIRED_NO_SESSION" });
        checkpoint.Phase = RecoveryCodeFirstCasePhase.STATE;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
        var after = await Snapshot(db, seed);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.ACTIVE_SET_STATES);
        Assert.True(after.Hash != before.Hash && after.Stamp != before.Stamp && after.Sessions == before.Sessions && after.Outbox == before.Outbox + 2);
        await using var check = db.Context(); var user = await check.Users.SingleAsync(x => x.AccountId == seed.Account); var hasher = AuthenticationPasswordWork.Hasher(host.Policies.Policy);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CAUSAL_RETIREMENT);
        Assert.True(hasher.VerifyHashedPassword(user, user.PasswordHash!, Replacement) != PasswordVerificationResult.Failed && hasher.VerifyHashedPassword(user, user.PasswordHash!, Password) == PasswordVerificationResult.Failed);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BODY);
        Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Active && x.Version == 1 && x.AgeEligibilityStatus == "INELIGIBLE" && x.AgePolicyVersion == "fixture-age"));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_COUNT);
        Assert.True(await check.VerifiedContacts.AnyAsync(x => x.ContactId == seed.Contact && x.State == ContactVerificationState.Verified && x.Version == 1));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.PROOF_BINDING);
        Assert.True(await check.Set<TotpAuthenticatorRow>().AnyAsync(x => x.AuthenticatorReference == seed.Factor && x.State == "ACTIVE" && x.Version == 1 && x.UsageVersion == 0));
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CONSUMED_PROOFS);
        Assert.True(!await check.Set<StepUpProofRow>().AnyAsync() && !await check.Set<RecoveryCodeSetRow>().AnyAsync() && !await check.Set<PasskeyCredentialRow>().AnyAsync());
        checkpoint.Mark(RecoveryCodeFirstCaseStage.SAFE_RESULT);
        var proof = await check.Set<RecoveryContactChallengeRow>().SingleAsync(); Assert.True(proof.State == "CONSUMED" && proof.ResultId == repaired.Result!.ResultId && proof.ProtectedCode is null && proof.ProtectedDigest is null);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_BODY);
        Assert.True(repaired.Result!.ContactReference == seed.Contact && repaired.Result.ContactChallengeId == pending.Challenge && repaired.Result.ContactNotificationDeliveryId == pending.Delivery && repaired.Result.ContactRiskSnapshot is not null);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DELIVERY_BINDING);
        var payloads = await check.Set<OutboxMessage>().Where(x => x.EventId == proof.NotificationOutboxEventId || x.EventId == repaired.Result!.RepairOutboxEventId).Select(x => x.Payload).ToArrayAsync();
        Assert.True(payloads.Length == 2);
        foreach (var payload in payloads)
        { var wire = JsonSerializer.Deserialize<JsonElement>(payload); Assert.True(wire.EnumerateObject().Count() == 1 && Guid.TryParseExact(wire.GetProperty("reference").GetString(), "N", out _)); }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
        Assert.True(!await Dispatch(host, pending.Delivery));
        checkpoint.Phase = RecoveryCodeFirstCasePhase.LOGIN;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
        using var ended = await host.Get("/authentication/session", seed.Cookies); Assert.True(ended.StatusCode == HttpStatusCode.Unauthorized);
        checkpoint.Phase = RecoveryCodeFirstCasePhase.STATE;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.DISPOSAL);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new RecoveryCodeFirstCaseFailure(checkpoint.Phase, checkpoint.Stage);
        }
    }
    internal async Task ContactProofValidationAttemptsExpiryAndPurgeRemainOneUse()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var notices = new Notices();
        await using var host = await Host(db, authority, notices); var seed = await SeedAsync(db, host); var first = await Begin(db, host, authority, seed.Caller);
        Assert.True(await Dispatch(host, first.Delivery)); var code = notices.Code(first.Challenge); var before = await Snapshot(db, seed);
        Assert.True((await Repair(host, seed.Caller, first, "invalid")).ErrorCode == "VALIDATION_ERROR");
        Assert.True((await Repair(host, seed.Caller, first, code, password: "short", confirmation: "short")).ErrorCode == "VALIDATION_ERROR");
        Assert.True((await Repair(host, seed.Caller, first, code, confirmation: "different")).ErrorCode == "VALIDATION_ERROR");
        Assert.True((await Repair(host, seed.Caller, first, code, version: 2)).ErrorCode == "RECOVERY_PROOF_REJECTED"); await Unrepaired(db, seed, before);
        Assert.True((await Repair(host, seed.Caller, first, Wrong(code))).ErrorCode == "RECOVERY_PROOF_REJECTED");
        await using (var check = db.Context()) Assert.True(await check.Set<RecoveryContactChallengeRow>().AnyAsync(x => x.ChallengeId == first.Challenge && x.Attempts == 1 && x.State == "PENDING"));
        Assert.True((await Repair(host, seed.Caller, first, code)).ErrorCode is null);
        var exhausted = await Begin(db, host, authority, seed.Caller); Assert.True(await Dispatch(host, exhausted.Delivery)); var exhaustedCode = notices.Code(exhausted.Challenge);
        for (var i = 0; i < authority.Policy.ContactRecoveryProfile!.MaximumProofAttempts; i++) Assert.True((await Repair(host, seed.Caller, exhausted, Wrong(exhaustedCode))).ErrorCode == "RECOVERY_PROOF_REJECTED");
        Assert.True((await Repair(host, seed.Caller, exhausted, exhaustedCode)).ErrorCode == "RECOVERY_PROOF_REJECTED");
        await using (var check = db.Context()) Assert.True(await check.Set<RecoveryContactChallengeRow>().AnyAsync(x => x.ChallengeId == exhausted.Challenge && x.State == "REJECTED" && x.Attempts == 3 && x.ProtectedCode == null && x.ProtectedDigest == null));
        var expired = await Begin(db, host, authority, seed.Caller); Assert.True(await Dispatch(host, expired.Delivery)); var expiredCode = notices.Code(expired.Challenge);
        host.Clock.Advance(authority.Policy.ContactRecoveryProfile!.ProofLifetime);
        Assert.True((await Repair(host, seed.Caller, expired, expiredCode)).ErrorCode == "RECOVERY_PROOF_REJECTED" && !await Dispatch(host, expired.Delivery));
        host.Clock.Advance(authority.Policy.ContactRecoveryProfile.PendingMaterialRetention);
        await using (var scope = host.Services.CreateAsyncScope()) Assert.True(await Application(scope.ServiceProvider).PurgeExpiredContactMaterialAsync(8) == 3);
        await using var final = db.Context(); Assert.True(await final.Set<RecoveryContactChallengeRow>().AllAsync(x => x.ProtectedCode == null && x.ProtectedDigest == null && x.ProtectedAddress == null && x.MaterialPurgedAt != null));
        Assert.True(await final.Set<RecoveryResultRow>().CountAsync() == 1);
    }
    internal async Task ContactRiskHierarchyPolicyAndVerifiedContactDriftFailClosed()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var notices = new Notices();
        await using var host = await Host(db, authority, notices); var seed = await SeedAsync(db, host);
        foreach (var change in AdmissionChanges)
        {
            authority.Reset(); authority.StrongUnavailable = change != "stronger"; authority.CodesUnavailable = change != "codes";
            authority.ForeignRiskCase = change == "risk-case"; authority.ForeignRiskContext = change == "risk-context";
            var before = await Snapshot(db, seed); var request = Guid.NewGuid();
            await using var scope = host.Services.CreateAsyncScope(); var app = Application(scope.ServiceProvider);
            var entry = await app.StartPasswordEntryAsync(seed.Caller, request, "person@example.test", authority.Policy.GoalReference);
            Assert.True(entry.ErrorCode is null && entry.Entry is { State: "OPEN", Version: 1 } && entry.Case is null && entry.Result is null);
            var options = await app.StartPasswordContactAsync(seed.Caller, request);
            Assert.True(options.ErrorCode is null && options.Presentation is not null && notices.Count == 0 && await Snapshot(db, seed) == before);
            await using var check = db.Context();
            Assert.True(!await check.Set<RecoveryCaseRow>().AnyAsync() && !await check.Set<RecoveryContactChallengeRow>().AnyAsync() && !await check.Set<RecoveryResultRow>().AnyAsync());
            Assert.True(await check.Accounts.CountAsync() == 1 && await check.Users.CountAsync() == 1 && await check.VerifiedContacts.CountAsync() == 1);
        }
        foreach (var change in Changes)
        {
            authority.Reset(); var pending = await Begin(db, host, authority, seed.Caller); Assert.True(await Dispatch(host, pending.Delivery)); var code = notices.Code(pending.Challenge); var before = await Snapshot(db, seed);
            await using var scope = host.Services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>(); var observed = false;
            bool Applied() => context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added);
            if (change == "risk") authority.BeforeRisk = () => { if (Applied()) { observed = true; authority.RiskAllowed = false; } };
            if (change == "hierarchy") authority.BeforeHierarchy = () => { if (Applied()) { observed = true; authority.HierarchyEvidence = "fixture-withdrawn-hierarchy"; } };
            if (change == "policy") authority.BeforePolicy = () => { if (Applied()) { observed = true; authority.Policy = authority.Policy with { Version = "fixture-contact-repair-v2" }; } };
            if (change == "notification") authority.BeforeNotification = () => { if (Applied()) { observed = true; authority.TemplateVersion = "fixture-withdrawn-template"; } };
            if (change == "blocklist-await") authority.BeforeBlocklist = async ct => { await Task.Yield(); ct.ThrowIfCancellationRequested(); observed = true; authority.RiskAllowed = false; };
            if (change is "stronger" or "codes" or "risk-case" or "risk-context") authority.BeforeBlocklist = async ct =>
            {
                await Task.Yield(); ct.ThrowIfCancellationRequested(); observed = true;
                authority.StrongUnavailable = change != "stronger"; authority.CodesUnavailable = change != "codes";
                authority.ForeignRiskCase = change == "risk-case"; authority.ForeignRiskContext = change == "risk-context";
            };
            if (change == "contact") authority.BeforeBlocklist = async ct =>
            { await using var mutation = db.Context(); var contact = await mutation.VerifiedContacts.SingleAsync(x => x.ContactId == seed.Contact, ct); contact.Version++; await mutation.SaveChangesAsync(ct); observed = true; };
            var rejected = false;
            try { rejected = (await Application(scope.ServiceProvider).RepairPasswordWithContactAsync(seed.Caller, pending.Request, 1, pending.Challenge, code, Replacement, Replacement)).ErrorCode is not null; }
            catch (InvalidOperationException) when (observed) { rejected = true; }
            Assert.True(rejected && observed); authority.Reset(); await Unrepaired(db, seed, before);
        }
    }
    internal async Task ContactOriginalAndConcurrentRepairsApplyOnePasswordEffect()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var notices = new Notices();
        await using var host = await Host(db, authority, notices); var seed = await SeedAsync(db, host); var first = await Begin(db, host, authority, seed.Caller); var second = await Begin(db, host, authority, seed.Caller);
        Assert.True(await Dispatch(host, first.Delivery) && await Dispatch(host, second.Delivery)); var a = notices.Code(first.Challenge); var b = notices.Code(second.Challenge); var before = await Snapshot(db, seed);
        foreach (var caller in new[] { seed.Caller with { BrowserDigest = new('E', 64) }, seed.Caller with { PreviousCookieDigest = new('E', 64) }, seed.Caller with { NetworkDigest = new('E', 64) } }) Assert.True((await Repair(host, caller, first, a)).ErrorCode is not null);
        Assert.True((await Repair(host, seed.Caller, first with { Challenge = second.Challenge }, a)).ErrorCode == "RECOVERY_PROOF_REJECTED"); await Unrepaired(db, seed, before);
        var results = await Task.WhenAll(Repair(host, seed.Caller, first, a), Repair(host, seed.Caller, second, b));
        Assert.True(results.Count(x => x.ErrorCode is null && x.Result is not null) == 1 && results.Count(x => x.ErrorCode is not null) == 1);
        var winner = results[0].ErrorCode is null ? 0 : 1; var pending = winner == 0 ? first : second; var code = winner == 0 ? a : b;
        var duplicate = await Repair(host, seed.Caller, pending, code, version: 2, password: null, confirmation: null); Assert.True(duplicate.ErrorCode is null && duplicate.Reconciled && duplicate.Result?.ResultId == results[winner].Result!.ResultId);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var original = await Application(scope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, pending.Request); Assert.True(original.ErrorCode is null && original.Reconciled && original.Result?.ResultId == results[winner].Result!.ResultId);
            Assert.True((await Application(scope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, Guid.NewGuid())).ErrorCode is not null);
        }
        var after = await Snapshot(db, seed); Assert.True(after.Outbox == before.Outbox + 1 && after.Sessions == before.Sessions);
        var guarded = false; await using (var mutation = db.Context())
        { try { _ = await mutation.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.recovery_results SET contact_risk_snapshot='{{}}' WHERE result_id={duplicate.Result!.ResultId}"); } catch (PostgresException error) when (error.SqlState == "23514") { guarded = true; } }
        Assert.True(guarded); await using var check = db.Context(); Assert.True(await check.Set<RecoveryResultRow>().CountAsync() == 1 && await check.Set<RecoveryContactChallengeRow>().CountAsync(x => x.State == "CONSUMED") == 1);
    }
    private sealed class SaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Observed { get; private set; }
        internal bool Saved { get; private set; }
        internal CancellationTokenSource? Cancellation { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observed = Armed && eventData.Context is { } context && context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added && x.Entity.ProofKind == "CONTACT_RISK") &&
                context.ChangeTracker.Entries<RecoveryContactChallengeRow>().Any(x => x.Entity.State == "CONSUMED" && x.Entity.ProtectedDigest == null) && context.ChangeTracker.Entries<IdentityUserAdapter>().Any(x => x.State == EntityState.Modified);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed) return ValueTask.FromResult(result); Saved = true;
            if (Cancellation is { } cancellation) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("FINITE_CONTACT_REPAIR_SAVE_FAILURE");
        }
        internal void Reset() { Armed = false; Observed = false; Saved = false; Cancellation = null; }
    }
    internal async Task ContactPostSaveFaultAndCancellationPreserveProofAndPassword()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var notices = new Notices(); var observer = new SaveFailure();
        await using var host = await Host(db, authority, notices, observer); var seed = await SeedAsync(db, host); var pending = await Begin(db, host, authority, seed.Caller); Assert.True(await Dispatch(host, pending.Delivery));
        var code = notices.Code(pending.Challenge); var before = await Snapshot(db, seed);
        foreach (var cancel in FailureModes)
        {
            observer.Reset(); using var cancellation = new CancellationTokenSource(); observer.Armed = true; observer.Cancellation = cancel ? cancellation : null; var interrupted = false;
            try { _ = await Repair(host, seed.Caller, pending, code, ct: cancellation.Token); }
            catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { interrupted = true; }
            catch (InvalidOperationException) when (!cancel && observer.Saved) { interrupted = true; }
            Assert.True(interrupted && observer.Observed && observer.Saved); observer.Reset(); await Unrepaired(db, seed, before);
        }
        Assert.True((await Repair(host, seed.Caller, pending, code)).ErrorCode is null);
    }
    private static string WithoutCookie(string cookies, string name) => string.Join("; ", cookies.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !x.StartsWith(name + "=", StringComparison.Ordinal)));
    private static bool NoLoginCookie(HttpResponseMessage response) => !response.Headers.TryGetValues("Set-Cookie", out var values) || !values.Any(x => x.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal) && x.Split(';', 2)[0].Length > AuthenticationIdentity.CookieName.Length + 1);
    internal async Task ActualHttpsContactRecoveryIsNeutralBoundedAndBrowserBound()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var notices = new Notices();
        await using var host = await Host(db, authority, notices); var seed = await SeedAsync(db, host); var boot = await host.Options(); var cookies = boot.Cookies; var csrf = boot.Csrf;
        using (var options = await host.Get("/recovery/options", cookies)) { Assert.True(options.StatusCode == HttpStatusCode.OK); Assert.True((await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("contact").GetProperty("available").GetBoolean()); }
        JsonElement neutral; var unknown = Guid.NewGuid();
        using (var start = await host.Post("/recovery/start", new { requestId = unknown, email = "absent@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf)) { Assert.True(start.StatusCode == HttpStatusCode.OK && NoLoginCookie(start)); cookies = Merge(cookies, start); }
        using (var options = await host.Post("/recovery/contact/options", new { requestId = unknown }, cookies, csrf)) { Assert.True(options.StatusCode == HttpStatusCode.OK && NoLoginCookie(options)); neutral = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options); }
        using (var restored = await host.Post("/recovery/result", new { requestId = unknown }, WithoutCookie(cookies, Pointer), csrf)) { Assert.True(restored.StatusCode == HttpStatusCode.OK); cookies = Merge(WithoutCookie(cookies, Pointer), restored); }
        using (var repeated = await host.Post("/recovery/contact/options", new { requestId = unknown }, cookies, csrf)) Assert.True((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetRawText() == neutral.GetRawText());
        using (var rejected = await host.Post("/recovery/contact/repair", new { requestId = unknown, expectedVersion = 1, challengeId = neutral.GetProperty("challengeId").GetGuid(), code = "000000", password = Replacement, passwordConfirmation = Replacement }, cookies, csrf)) Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest && (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString() == "RECOVERY_PROOF_REJECTED");
        await using (var check = db.Context()) Assert.True(!await check.Set<RecoveryCaseRow>().AnyAsync() && !await check.Set<RecoveryContactChallengeRow>().AnyAsync() && notices.Count == 0);
        var blocked = false; await using (var downgrade = db.Context()) { try { _ = await downgrade.Database.ExecuteSqlRawAsync(RecoveryContactMigrationSql.BlockPopulatedDowngrade); } catch (PostgresException error) when (error.SqlState == "55000") { blocked = true; } } Assert.True(blocked);
        var request = Guid.NewGuid(); using (var start = await host.Post("/recovery/start", new { requestId = request, email = "person@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf)) { Assert.True(start.StatusCode == HttpStatusCode.OK); cookies = Merge(cookies, start); }
        using (var missing = await host.Post("/recovery/contact/options", new { requestId = request }, cookies, null)) Assert.True(missing.StatusCode == HttpStatusCode.BadRequest);
        using (var wrong = await host.Post("/recovery/contact/options", new { requestId = request }, cookies, "invalid-synthetic-csrf")) Assert.True(wrong.StatusCode == HttpStatusCode.BadRequest);
        using (var origin = await host.Post("/recovery/contact/options", new { requestId = request }, cookies, csrf, "https://foreign.example.test")) Assert.True(origin.StatusCode == HttpStatusCode.Forbidden);
        using (var browser = await host.Post("/recovery/contact/options", new { requestId = request }, WithoutCookie(cookies, AuthenticationEndpoints.BrowserCookieName), csrf)) Assert.True(browser.StatusCode == HttpStatusCode.Forbidden);
        using (var pointer = await host.Post("/recovery/contact/options", new { requestId = request }, WithoutCookie(cookies, Pointer), csrf)) Assert.True(pointer.StatusCode == HttpStatusCode.NotFound);
        using (var query = await host.Post("/recovery/contact/options?unexpected=true", new { requestId = request }, cookies, csrf)) Assert.True(query.StatusCode == HttpStatusCode.BadRequest);
        using (var oversized = await host.Post("/recovery/contact/options", new { requestId = request, padding = new string('X', 5000) }, cookies, csrf)) Assert.True(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge);
        foreach (var selector in Selectors) { using var rejected = await host.Post("/recovery/contact/options", new Dictionary<string, object> { ["requestId"] = request, [selector] = Guid.NewGuid() }, cookies, csrf); Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest); }
        JsonElement actual; using (var options = await host.Post("/recovery/contact/options", new { requestId = request }, cookies, csrf)) { Assert.True(options.StatusCode == HttpStatusCode.OK && NoLoginCookie(options)); actual = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options); }
        using (var restored = await host.Post("/recovery/result", new { requestId = request }, WithoutCookie(cookies, Pointer), csrf)) { Assert.True(restored.StatusCode == HttpStatusCode.OK && NoLoginCookie(restored)); cookies = Merge(WithoutCookie(cookies, Pointer), restored); }
        using (var repeated = await host.Post("/recovery/contact/options", new { requestId = request }, cookies, csrf)) Assert.True(repeated.StatusCode == HttpStatusCode.OK && (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetRawText() == actual.GetRawText());
        Assert.True(actual.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(neutral.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal)) && actual.GetProperty("expiresAt").GetDateTimeOffset() == neutral.GetProperty("expiresAt").GetDateTimeOffset());
        var challenge = actual.GetProperty("challengeId").GetGuid(); Guid delivery; await using (var check = db.Context()) delivery = await check.Set<RecoveryContactChallengeRow>().Where(x => x.ChallengeId == challenge).Select(x => x.DeliveryId).SingleAsync();
        Assert.True(await Dispatch(host, delivery)); var command = new { requestId = request, expectedVersion = 1, challengeId = challenge, code = notices.Code(challenge), password = Replacement, passwordConfirmation = Replacement };
        using (var missing = await host.Post("/recovery/contact/repair", command, cookies, null)) Assert.True(missing.StatusCode == HttpStatusCode.BadRequest);
        using (var wrong = await host.Post("/recovery/contact/repair", command, cookies, "invalid-synthetic-csrf")) Assert.True(wrong.StatusCode == HttpStatusCode.BadRequest);
        using (var origin = await host.Post("/recovery/contact/repair", command, cookies, csrf, "https://foreign.example.test")) Assert.True(origin.StatusCode == HttpStatusCode.Forbidden);
        using (var browser = await host.Post("/recovery/contact/repair", command, WithoutCookie(cookies, AuthenticationEndpoints.BrowserCookieName), csrf)) Assert.True(browser.StatusCode == HttpStatusCode.Forbidden);
        using (var pointer = await host.Post("/recovery/contact/repair", command, WithoutCookie(cookies, Pointer), csrf)) Assert.True(pointer.StatusCode == HttpStatusCode.NotFound);
        using (var query = await host.Post("/recovery/contact/repair?unexpected=true", command, cookies, csrf)) Assert.True(query.StatusCode == HttpStatusCode.BadRequest);
        using (var oversized = await host.Post("/recovery/contact/repair", new { requestId = request, expectedVersion = 1, challengeId = challenge, code = command.code, password = new string('X', 7000), passwordConfirmation = new string('X', 7000) }, cookies, csrf)) Assert.True(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge);
        var foreign = await host.Options(); var pointerCookie = cookies.Split(';').Select(x => x.Trim()).Single(x => x.StartsWith(Pointer + "=", StringComparison.Ordinal));
        using (var borrowed = await host.Post("/recovery/contact/repair", command, foreign.Cookies + "; " + pointerCookie, foreign.Csrf)) Assert.True(borrowed.StatusCode == HttpStatusCode.NotFound);
        foreach (var selector in Selectors)
        { using var rejected = await host.Post("/recovery/contact/repair", new Dictionary<string, object> { ["requestId"] = request, ["expectedVersion"] = 1, ["challengeId"] = challenge, ["code"] = command.code, ["password"] = Replacement, ["passwordConfirmation"] = Replacement, [selector] = Guid.NewGuid() }, cookies, csrf); Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest); }
        using (var completed = await host.Post("/recovery/contact/repair", command, cookies, csrf))
        { Assert.True(completed.StatusCode == HttpStatusCode.OK && NoLoginCookie(completed)); cookies = Merge(cookies, completed); var operation = (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation"); Assert.True(operation.GetProperty("status").GetString() == "PASSWORD_REPAIRED_NO_SESSION" && !operation.GetProperty("authenticated").GetBoolean()); }
        using (var result = await host.Post("/recovery/result", new { requestId = request }, WithoutCookie(cookies, Pointer), csrf)) Assert.True(result.StatusCode == HttpStatusCode.OK && NoLoginCookie(result));
        using (var session = await host.Get("/authentication/session", cookies)) Assert.True(session.StatusCode == HttpStatusCode.Unauthorized);
    }
}
