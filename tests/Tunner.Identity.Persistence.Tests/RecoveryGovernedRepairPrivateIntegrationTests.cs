using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

// Synthetic trusted-owner projections exercise the consumer only; no production review producer or approval is claimed.
// Existing finite literal execution keeps all account, credential and SQL values in the private process.
internal sealed class RecoveryGovernedRepairPrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Public = "SYNTHETIC-TEST-ONLY";
    private const string Replacement = "a synthetic governed recovery replacement passphrase";
    private const string Pointer = "__Host-Tunner.Recovery.Original";
    private static readonly string[] Unresolved = ["ACCESS_RESTORATION", "NOTIFICATION_DELIVERY"];
    private static readonly string[] AdmissionChanges = ["stronger", "codes", "contact", "selection", "evidence"];
    private static readonly string[] CurrentChanges = ["stronger", "codes", "contact", "hierarchy", "policy", "disposition", "blocklist-await", "contact-version"];
    private static readonly string[] Selectors = ["accountId", "caseId", "dispositionReference", "dispositionVersion", "authorizesExactEffect", "controlEvidenceReference", "proofKind"];
    private static readonly bool[] FailureModes = [false, true];
    private sealed class Authority : IRecoveryPasswordPolicySource, IRecoveryCaseHierarchySource, IRegistrationPasswordBlocklist, IRecoveryGovernedDispositionSource
    {
        internal RecoveryPasswordPolicy Policy { get; set; } = new("fixture-governed-repair-v1", Origin, "fixture-governed-password", 15, 128, 10_000,
            RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
            32, 100, 100, Unresolved, "fixture-processing", "fixture-retention")
        { GovernedRecoveryProfile = new("fixture-governed-v1", "fixture-governed-disposition-contract-v1") };
        internal bool StrongUnavailable { get; set; } = true;
        internal bool CodesUnavailable { get; set; } = true;
        internal bool ContactUnavailable { get; set; } = true;
        internal bool Selection { get; set; } = true;
        internal string UnavailabilityEvidence { get; set; } = "fixture-qualified-unavailable-higher-tiers";
        internal string HierarchyEvidence { get; set; } = "fixture-governed-hierarchy-evidence-v1";
        internal bool DispositionAvailable { get; set; } = true;
        internal Func<RecoveryGovernedDisposition, RecoveryGovernedDisposition>? Transform { get; set; }
        internal Action? BeforePolicy { get; set; }
        internal Action? BeforeHierarchy { get; set; }
        internal Action? BeforeDisposition { get; set; }
        internal Func<CancellationToken, Task>? BeforeBlocklist { get; set; }
        private static EffectivePolicyInterval Interval => RecoveryCodePrivateIntegrationTests.Profile.Interval;
        public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
        { BeforePolicy?.Invoke(); return new(new(Policy.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", Interval, null, [Public]), Policy); }
        public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at)
        {
            BeforeHierarchy?.Invoke();
            return new(false, StrongUnavailable, "fixture-governed-hierarchy-v1", HierarchyEvidence, request.Facts.Digest(), Interval)
            { GovernedSelection = Selection ? new(Policy.GovernedRecoveryProfile!.Version, CodesUnavailable, ContactUnavailable, UnavailabilityEvidence) : null };
        }
        public RecoveryGovernedDisposition RequireCurrent(RecoveryGovernedDispositionRequest request, DateTimeOffset at)
        {
            BeforeDisposition?.Invoke();
            if (!DispositionAvailable) throw new InvalidOperationException("FINITE_GOVERNED_DISPOSITION_UNAVAILABLE");
            Assert.True(request.EffectsEncoding == "UTF8_BYTE_LENGTH_PREFIXED_FIELDS_V1" && request.EffectsDigest == Effects(request.Goal, request.SessionEffect, request.UnresolvedEffectsJson));
            var value = new RecoveryGovernedDisposition(request.CaseId, request.RequestId, request.AccountId, request.OriginalCaseVersion,
                request.AccountVersion, request.ContextDigest, request.FactsDigest, request.UserSourceDigest, request.ContactsDigest,
                request.Goal, request.SessionEffect, request.RecoveryPolicyVersion, request.ProfileVersion, request.DispositionContractReference,
                request.UnresolvedEffectsJson, request.EffectsEncoding, request.EffectsDigest,
                "fixture-disposition-" + request.CaseId.ToString("N"), 1, "fixture-owner-authority-v1", "fixture-owner-controls-v1", true, Interval);
            return Transform is { } transform ? transform(value) : value;
        }
        public async ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken)
        { if (BeforeBlocklist is { } action) await action(cancellationToken); return false; }
        internal void Reset()
        {
            Policy = Policy with { Version = "fixture-governed-repair-v1" }; StrongUnavailable = true; CodesUnavailable = true; ContactUnavailable = true;
            Selection = true; UnavailabilityEvidence = "fixture-qualified-unavailable-higher-tiers"; HierarchyEvidence = "fixture-governed-hierarchy-evidence-v1";
            DispositionAvailable = true; Transform = null; BeforePolicy = null; BeforeHierarchy = null; BeforeDisposition = null; BeforeBlocklist = null;
        }
        public override string ToString() => nameof(Authority);
    }
    private static string Effects(string goal, string sessionEffect, string unresolved)
    {
        static string Field(string value) => Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Field(goal) + Field(sessionEffect) + Field(unresolved))));
    }
    private sealed class Presentation : IRecoveryPasswordPresentationSource
    {
        private static readonly string[] Keys = ["fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value"];
        public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at)
        {
            var follow = policy.UnresolvedDownstreamEffects.Select((reference, i) => new RecoveryPasswordFollowUp(reference, "fixture.follow-label-" + i, "fixture.follow-value-" + i)).ToArray();
            var keys = Keys.Concat(follow.SelectMany(x => new[] { x.LabelKey, x.ValueKey }));
            return new("en", keys.Select(key => new RegistrationPublishedText(key, "fixture-variant", "fixture-text-v1", Public, "en", "PUBLISHED", "Synthetic test only")),
                "fixture-governed-publication-v1", RecoveryCodePrivateIntegrationTests.Profile.Interval, Keys[0], Keys[1], Keys[2], Keys[3], follow);
        }
    }
    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Authority authority, SaveChangesInterceptor? observer = null, bool source = true) =>
        Start(db, services =>
        {
            services.AddSingleton<IRecoveryPasswordPolicySource>(authority); services.AddSingleton<IRecoveryCaseHierarchySource>(authority);
            services.AddSingleton<IRegistrationPasswordBlocklist>(authority); services.AddSingleton<IRecoveryPasswordPresentationSource, Presentation>();
            if (source) services.AddSingleton<IRecoveryGovernedDispositionSource>(authority);
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
    private static RecoveryCaseApplication Application(IServiceProvider services) => services.GetRequiredService<RecoveryCaseApplication>();
    private static async Task<Guid> Begin(AuthenticationTestHost host, Authority authority, RecoveryEntryCaller caller)
    {
        var request = Guid.NewGuid(); await using var scope = host.Services.CreateAsyncScope();
        var entry = await Application(scope.ServiceProvider).StartPasswordEntryAsync(caller, request, "person@example.test", authority.Policy.GoalReference);
        Assert.True(entry.ErrorCode is null && entry.Case is { ProofKind: "GOVERNED_REVIEW", State: "PENDING_GOVERNED_REVIEW", Version: 1 });
        return request;
    }
    private static async Task<RecoveryEntryResult> Repair(AuthenticationTestHost host, RecoveryEntryCaller caller, Guid request,
        long version = 1, string? password = Replacement, string? confirmation = Replacement, CancellationToken ct = default)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await Application(scope.ServiceProvider).RepairPasswordWithGovernedDispositionAsync(caller, request, version, password, confirmation, ct);
    }
    private static async Task Unrepaired(IdentityTestDatabase db, Seed seed, Baseline before)
    {
        Assert.True(await Snapshot(db, seed) == before); await using var check = db.Context();
        Assert.True(!await check.Set<RecoveryResultRow>().AnyAsync() && await check.Set<RecoveryEntryRow>().AllAsync(x => x.State == "OPEN" && x.Version == 1));
        Assert.True(await check.Set<RecoveryCaseRow>().AllAsync(x => x.State == "PENDING_GOVERNED_REVIEW" && x.Version == 1 && x.ResultId == null));
        Assert.True(!await check.Set<RecoveryContactChallengeRow>().AnyAsync() && !await check.Set<StepUpProofRow>().AnyAsync());
    }
    internal async Task ExactTrustedDispositionRepairsOnlyPasswordAndRecordsExplicitEffects()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        await using var host = await Host(db, authority); var seed = await SeedAsync(db, host); var before = await Snapshot(db, seed);
        var request = await Begin(host, authority, seed.Caller); var repaired = await Repair(host, seed.Caller, request);
        Assert.True(repaired.ErrorCode is null && repaired.Result is { ProofKind: "GOVERNED_REVIEW", Outcome: "PASSWORD_REPAIRED_NO_SESSION" });
        var after = await Snapshot(db, seed); Assert.True(after.Hash != before.Hash && after.Stamp != before.Stamp && after.Sessions == before.Sessions && after.Outbox == before.Outbox + 1);
        await using var check = db.Context(); var user = await check.Users.SingleAsync(x => x.AccountId == seed.Account); var hasher = AuthenticationPasswordWork.Hasher(host.Policies.Policy);
        Assert.True(hasher.VerifyHashedPassword(user, user.PasswordHash!, Replacement) != PasswordVerificationResult.Failed && hasher.VerifyHashedPassword(user, user.PasswordHash!, Password) == PasswordVerificationResult.Failed);
        Assert.True(await check.Accounts.AllAsync(x => x.State == AccountState.Active && x.Version == 1 && x.AgeEligibilityStatus == "INELIGIBLE" && x.AgePolicyVersion == "fixture-age"));
        Assert.True(await check.VerifiedContacts.AnyAsync(x => x.ContactId == seed.Contact && x.State == ContactVerificationState.Verified && x.Version == 1));
        Assert.True(await check.Set<TotpAuthenticatorRow>().AnyAsync(x => x.AuthenticatorReference == seed.Factor && x.State == "ACTIVE" && x.Version == 1 && x.UsageVersion == 0));
        Assert.True(!await check.Set<StepUpProofRow>().AnyAsync() && !await check.Set<RecoveryCodeSetRow>().AnyAsync() && !await check.Set<PasskeyCredentialRow>().AnyAsync() && !await check.Set<RecoveryContactChallengeRow>().AnyAsync());
        var row = await check.Set<RecoveryCaseRow>().SingleAsync(); var result = await check.Set<RecoveryResultRow>().SingleAsync();
        var disposition = JsonSerializer.Deserialize<RecoveryGovernedDisposition>(result.GovernedDispositionSnapshot!);
        Assert.True(row.State == "REPAIRED" && row.Version == 2 && row.ResultId == result.ResultId && result.SessionEffect == "InvalidateExistingTunnerSessions" && result.UnresolvedEffectsJson == JsonSerializer.Serialize(Unresolved));
        Assert.True(disposition is { AuthorizesExactEffect: true, DispositionVersion: 1 } && disposition.CaseId == row.CaseId && disposition.ContextDigest == row.ContextDigest && disposition.EffectsDigest == Effects(row.Goal, row.SessionEffect, row.UnresolvedEffectsJson));
        var payload = await check.Set<OutboxMessage>().Where(x => x.EventId == result.RepairOutboxEventId).Select(x => x.Payload).SingleAsync();
        var wire = JsonSerializer.Deserialize<JsonElement>(payload); Assert.True(wire.EnumerateObject().Count() == 1 && Guid.TryParseExact(wire.GetProperty("reference").GetString(), "N", out _));
        using var ended = await host.Get("/authentication/session", seed.Cookies); Assert.True(ended.StatusCode == HttpStatusCode.Unauthorized);
    }
    private static RecoveryGovernedDisposition Substitute(RecoveryGovernedDisposition value, string field) => field switch
    {
        "case" => value with { CaseId = Guid.NewGuid() }, "request" => value with { RequestId = Guid.NewGuid() }, "account" => value with { AccountId = "fixture-foreign" },
        "case-version" => value with { OriginalCaseVersion = 2 }, "account-version" => value with { AccountVersion = 2 },
        "context" => value with { ContextDigest = new('E', 64) }, "facts" => value with { FactsDigest = new('E', 64) },
        "user" => value with { UserSourceDigest = new('E', 64) }, "contacts" => value with { ContactsDigest = new('E', 64) },
        "goal" => value with { Goal = "ContainSelectedTunnerSession" }, "session" => value with { SessionEffect = "fixture-foreign" },
        "policy" => value with { RecoveryPolicyVersion = "fixture-foreign" }, "profile" => value with { ProfileVersion = "fixture-foreign" },
        "contract" => value with { DispositionContractReference = "fixture-foreign" }, "unresolved" => value with { UnresolvedEffectsJson = "[]" },
        "encoding" => value with { EffectsEncoding = "fixture-foreign" }, "digest" => value with { EffectsDigest = new('E', 64) },
        "reference" => value with { DispositionReference = "" }, "version" => value with { DispositionVersion = 0 },
        "authority" => value with { AuthorityPolicyVersion = "" }, "controls" => value with { ControlEvidenceReference = "" },
        "denied" => value with { AuthorizesExactEffect = false }, _ => throw new InvalidOperationException("FINITE_GOVERNED_VARIANT_INVALID")
    };
    private static readonly string[] BindingChanges = ["case", "request", "account", "case-version", "account-version", "context", "facts", "user", "contacts", "goal", "session", "policy", "profile", "contract", "unresolved", "encoding", "digest", "reference", "version", "authority", "controls", "denied"];
    internal async Task ForeignUnqualifiedOrUncontrolledDispositionCannotAuthorizeAnyEffect()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        await using var host = await Host(db, authority); var seed = await SeedAsync(db, host); var request = await Begin(host, authority, seed.Caller); var before = await Snapshot(db, seed);
        foreach (var field in BindingChanges)
        {
            authority.Transform = value => Substitute(value, field);
            Assert.True((await Repair(host, seed.Caller, request)).ErrorCode == "RECOVERY_PROOF_REJECTED"); await Unrepaired(db, seed, before);
        }
        authority.Reset(); authority.DispositionAvailable = false; Assert.True((await Repair(host, seed.Caller, request)).ErrorCode == "RECOVERY_PROOF_REJECTED");
        authority.Reset(); var at = host.Clock.GetUtcNow();
        foreach (var interval in new[] { new EffectivePolicyInterval(at.AddTicks(1), true, null, false), new EffectivePolicyInterval(at.AddMinutes(-1), true, at, false) })
        { authority.Transform = value => value with { EffectiveInterval = interval }; Assert.True((await Repair(host, seed.Caller, request)).ErrorCode == "RECOVERY_PROOF_REJECTED"); await Unrepaired(db, seed, before); }
        authority.Reset();
        Assert.True((await Repair(host, seed.Caller, request, password: "short", confirmation: "short")).ErrorCode == "VALIDATION_ERROR");
        Assert.True((await Repair(host, seed.Caller, request, confirmation: "different")).ErrorCode == "VALIDATION_ERROR");
        Assert.True((await Repair(host, seed.Caller, request, version: 2)).ErrorCode == "RECOVERY_PROOF_REJECTED"); await Unrepaired(db, seed, before);
        Assert.True((await Repair(host, seed.Caller, request)).ErrorCode is null);
    }
    internal async Task HigherProofTiersAndLateAuthorityOrContactDriftRemainAuthoritative()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        await using var host = await Host(db, authority); var seed = await SeedAsync(db, host);
        foreach (var change in AdmissionChanges)
        {
            authority.Reset(); authority.StrongUnavailable = change != "stronger"; authority.CodesUnavailable = change != "codes"; authority.ContactUnavailable = change != "contact";
            authority.Selection = change != "selection"; authority.UnavailabilityEvidence = change == "evidence" ? "" : authority.UnavailabilityEvidence;
            var before = await Snapshot(db, seed); await using var scope = host.Services.CreateAsyncScope();
            var entry = await Application(scope.ServiceProvider).StartPasswordEntryAsync(seed.Caller, Guid.NewGuid(), "person@example.test", authority.Policy.GoalReference);
            Assert.True(entry.ErrorCode is null && entry.Entry is { State: "OPEN", Version: 1 } && entry.Case is null && entry.Result is null); await Unrepaired(db, seed, before);
        }
        foreach (var change in CurrentChanges)
        {
            authority.Reset(); var request = await Begin(host, authority, seed.Caller); var before = await Snapshot(db, seed);
            await using var scope = host.Services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>(); var observed = false;
            bool Applied() => context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added);
            if (change == "hierarchy") authority.BeforeHierarchy = () => { if (Applied()) { observed = true; authority.HierarchyEvidence = "fixture-withdrawn-hierarchy"; } };
            if (change == "policy") authority.BeforePolicy = () => { if (Applied()) { observed = true; authority.Policy = authority.Policy with { Version = "fixture-governed-repair-v2" }; } };
            if (change == "disposition") authority.BeforeDisposition = () => { if (Applied()) { observed = true; authority.DispositionAvailable = false; } };
            if (change is "stronger" or "codes" or "contact" or "blocklist-await") authority.BeforeBlocklist = async ct =>
            {
                await Task.Yield(); ct.ThrowIfCancellationRequested(); observed = true; authority.StrongUnavailable = change != "stronger";
                authority.CodesUnavailable = change != "codes"; authority.ContactUnavailable = change != "contact"; if (change == "blocklist-await") authority.DispositionAvailable = false;
            };
            if (change == "contact-version") authority.BeforeBlocklist = async ct =>
            { await using var mutation = db.Context(); var contact = await mutation.VerifiedContacts.SingleAsync(x => x.ContactId == seed.Contact, ct); contact.Version++; await mutation.SaveChangesAsync(ct); observed = true; };
            var rejected = false;
            try { rejected = (await Application(scope.ServiceProvider).RepairPasswordWithGovernedDispositionAsync(seed.Caller, request, 1, Replacement, Replacement)).ErrorCode is not null; }
            catch (InvalidOperationException) when (observed) { rejected = true; }
            Assert.True(rejected && observed); authority.Reset(); await Unrepaired(db, seed, before);
        }
        authority.Reset(); var expired = await Begin(host, authority, seed.Caller); host.Clock.Advance(authority.Policy.CaseLifetime);
        Assert.True((await Repair(host, seed.Caller, expired)).ErrorCode == "RECOVERY_PROOF_REJECTED");
    }
    internal async Task OriginalAndCompetingDispositionExecutionsHaveOnlyOneCommittedEffect()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        await using var host = await Host(db, authority); var seed = await SeedAsync(db, host);
        var first = await Begin(host, authority, seed.Caller); var second = await Begin(host, authority, seed.Caller); var before = await Snapshot(db, seed);
        foreach (var caller in new[] { seed.Caller with { BrowserDigest = new('E', 64) }, seed.Caller with { PreviousCookieDigest = new('E', 64) }, seed.Caller with { NetworkDigest = new('E', 64) } })
            Assert.True((await Repair(host, caller, first)).ErrorCode is not null);
        await Unrepaired(db, seed, before);
        var results = await Task.WhenAll(Repair(host, seed.Caller, first), Repair(host, seed.Caller, second));
        Assert.True(results.Count(x => x.ErrorCode is null && x.Result is not null) == 1 && results.Count(x => x.ErrorCode is not null) == 1);
        var winner = results[0].ErrorCode is null ? 0 : 1; var request = winner == 0 ? first : second;
        authority.DispositionAvailable = false;
        var duplicate = await Repair(host, seed.Caller, request, version: 2, password: null, confirmation: null);
        Assert.True(duplicate.ErrorCode is null && duplicate.Reconciled && duplicate.Result?.ResultId == results[winner].Result!.ResultId);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var original = await Application(scope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, request);
            Assert.True(original.ErrorCode is null && original.Reconciled && original.Result?.ResultId == duplicate.Result!.ResultId);
            Assert.True((await Application(scope.ServiceProvider).ReadPasswordEntryAsync(seed.Caller, Guid.NewGuid())).ErrorCode is not null);
        }
        var after = await Snapshot(db, seed); Assert.True(after.Outbox == before.Outbox + 1 && after.Sessions == before.Sessions);
        authority.Reset(); var reuse = await Begin(host, authority, seed.Caller); var usedReference = duplicate.Result!.GovernedDispositionReference!;
        authority.Transform = value => value with { DispositionReference = usedReference };
        var deniedReuse = false; try { deniedReuse = (await Repair(host, seed.Caller, reuse)).ErrorCode is not null; }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" }) { deniedReuse = true; }
        Assert.True(deniedReuse && await Snapshot(db, seed) == after);
        await using var check = db.Context(); Assert.True(await check.Set<RecoveryResultRow>().CountAsync() == 1 && await check.Set<RecoveryCaseRow>().CountAsync(x => x.State == "REPAIRED") == 1);
    }
    private sealed class SaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Observed { get; private set; }
        internal bool Saved { get; private set; }
        internal CancellationTokenSource? Cancellation { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observed = Armed && eventData.Context is { } context && context.ChangeTracker.Entries<RecoveryResultRow>().Any(x => x.State == EntityState.Added && x.Entity.ProofKind == "GOVERNED_REVIEW") &&
                context.ChangeTracker.Entries<IdentityUserAdapter>().Any(x => x.State == EntityState.Modified);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed) return ValueTask.FromResult(result); Saved = true;
            if (Cancellation is { } cancellation) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("FINITE_GOVERNED_REPAIR_SAVE_FAILURE");
        }
        internal void Reset() { Armed = false; Observed = false; Saved = false; Cancellation = null; }
    }
    internal async Task PostSaveFailureAndCancellationRollbackDispositionPasswordAndOutbox()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority(); var observer = new SaveFailure();
        await using var host = await Host(db, authority, observer); var seed = await SeedAsync(db, host); var request = await Begin(host, authority, seed.Caller); var before = await Snapshot(db, seed);
        foreach (var cancel in FailureModes)
        {
            observer.Reset(); using var cancellation = new CancellationTokenSource(); observer.Armed = true; observer.Cancellation = cancel ? cancellation : null; var interrupted = false;
            try { _ = await Repair(host, seed.Caller, request, ct: cancellation.Token); }
            catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { interrupted = true; }
            catch (InvalidOperationException) when (!cancel && observer.Saved) { interrupted = true; }
            Assert.True(interrupted && observer.Observed && observer.Saved); observer.Reset(); await Unrepaired(db, seed, before);
        }
        Assert.True((await Repair(host, seed.Caller, request)).ErrorCode is null);
    }
    private static string WithoutCookie(string cookies, string name) => string.Join("; ", cookies.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !x.StartsWith(name + "=", StringComparison.Ordinal)));
    private static bool NoLoginCookie(HttpResponseMessage response) => !response.Headers.TryGetValues("Set-Cookie", out var values) || !values.Any(x => x.StartsWith(AuthenticationIdentity.CookieName + "=", StringComparison.Ordinal) && x.Split(';', 2)[0].Length > AuthenticationIdentity.CookieName.Length + 1);
    internal async Task ActualHttpsGovernedConsumerIsNeutralStrictAndOriginalBrowserBound()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        await using var host = await Host(db, authority); _ = await SeedAsync(db, host); var boot = await host.Options(); var cookies = boot.Cookies; var csrf = boot.Csrf;
        using (var options = await host.Get("/recovery/options", cookies)) Assert.True(options.StatusCode == HttpStatusCode.OK && (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("governedReview").GetProperty("available").GetBoolean());
        var unknown = Guid.NewGuid();
        using (var start = await host.Post("/recovery/start", new { requestId = unknown, email = "absent@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf)) { Assert.True(start.StatusCode == HttpStatusCode.OK && NoLoginCookie(start)); cookies = Merge(cookies, start); }
        using (var rejected = await host.Post("/recovery/review/repair", new { requestId = unknown, expectedVersion = 1, password = Replacement, passwordConfirmation = Replacement }, cookies, csrf)) Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest && (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString() == "RECOVERY_PROOF_REJECTED");
        await using (var check = db.Context()) Assert.True(!await check.Set<RecoveryCaseRow>().AnyAsync() && !await check.Set<RecoveryResultRow>().AnyAsync());
        var request = Guid.NewGuid();
        using (var start = await host.Post("/recovery/start", new { requestId = request, email = "person@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf)) { Assert.True(start.StatusCode == HttpStatusCode.OK && NoLoginCookie(start)); cookies = Merge(cookies, start); }
        var command = new { requestId = request, expectedVersion = 1, password = Replacement, passwordConfirmation = Replacement };
        using (var missing = await host.Post("/recovery/review/repair", command, cookies, null)) Assert.True(missing.StatusCode == HttpStatusCode.BadRequest);
        using (var wrong = await host.Post("/recovery/review/repair", command, cookies, "invalid-synthetic-csrf")) Assert.True(wrong.StatusCode == HttpStatusCode.BadRequest);
        using (var origin = await host.Post("/recovery/review/repair", command, cookies, csrf, "https://foreign.example.test")) Assert.True(origin.StatusCode == HttpStatusCode.Forbidden);
        using (var browser = await host.Post("/recovery/review/repair", command, WithoutCookie(cookies, AuthenticationEndpoints.BrowserCookieName), csrf)) Assert.True(browser.StatusCode == HttpStatusCode.Forbidden);
        using (var pointer = await host.Post("/recovery/review/repair", command, WithoutCookie(cookies, Pointer), csrf)) Assert.True(pointer.StatusCode == HttpStatusCode.NotFound);
        using (var query = await host.Post("/recovery/review/repair?unexpected=true", command, cookies, csrf)) Assert.True(query.StatusCode == HttpStatusCode.BadRequest);
        using (var oversized = await host.Post("/recovery/review/repair", new { requestId = request, expectedVersion = 1, password = new string('X', 7000), passwordConfirmation = new string('X', 7000) }, cookies, csrf)) Assert.True(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge);
        var foreign = await host.Options(); var pointerCookie = cookies.Split(';').Select(x => x.Trim()).Single(x => x.StartsWith(Pointer + "=", StringComparison.Ordinal));
        using (var borrowed = await host.Post("/recovery/review/repair", command, foreign.Cookies + "; " + pointerCookie, foreign.Csrf)) Assert.True(borrowed.StatusCode == HttpStatusCode.NotFound);
        foreach (var selector in Selectors)
        { using var rejected = await host.Post("/recovery/review/repair", new Dictionary<string, object> { ["requestId"] = request, ["expectedVersion"] = 1, ["password"] = Replacement, ["passwordConfirmation"] = Replacement, [selector] = Guid.NewGuid() }, cookies, csrf); Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest); }
        using (var completed = await host.Post("/recovery/review/repair", command, cookies, csrf))
        { Assert.True(completed.StatusCode == HttpStatusCode.OK && NoLoginCookie(completed)); cookies = Merge(cookies, completed); var operation = (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation"); Assert.True(operation.GetProperty("status").GetString() == "PASSWORD_REPAIRED_NO_SESSION" && !operation.GetProperty("authenticated").GetBoolean()); }
        using (var result = await host.Post("/recovery/result", new { requestId = request }, WithoutCookie(cookies, Pointer), csrf)) Assert.True(result.StatusCode == HttpStatusCode.OK && NoLoginCookie(result));
        using (var session = await host.Get("/authentication/session", cookies)) Assert.True(session.StatusCode == HttpStatusCode.Unauthorized);
        var blocked = false; await using (var downgrade = db.Context()) { try { _ = await downgrade.Database.ExecuteSqlRawAsync(RecoveryGovernedMigrationSql.BlockPopulatedDowngrade); } catch (PostgresException error) when (error.SqlState == "55000") { blocked = true; } } Assert.True(blocked);
        await using var absent = await Host(db, authority, source: false); var absentBoot = await absent.Options();
        using var unavailable = await absent.Post("/recovery/review/repair", command, absentBoot.Cookies, absentBoot.Csrf); Assert.True(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable);
    }
    private static Task<string> ResultJson(IdentityPersistenceDbContext context, Guid id) => context.Database.SqlQuery<string>($"SELECT to_jsonb(r)::text AS \"Value\" FROM identity.recovery_results r WHERE result_id={id}").SingleAsync();
    // Replay the exact committed row inside a rolled-back transaction, preserving every FK and real owner effect.
    // SET CONSTRAINTS forces the production deferred guard; no trigger is disabled or replaced.
    private static async Task SqlDisposition(IdentityTestDatabase db, Guid id, string original, string snapshot, bool accepts)
    {
        await using var context = db.Context(); await using var transaction = await context.Database.BeginTransactionAsync(); var rejected = false;
        try
        {
            Assert.True(await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.recovery_results WHERE result_id={id}") == 1);
            _ = await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO identity.recovery_results SELECT (jsonb_populate_record(NULL::identity.recovery_results, {original}::jsonb || jsonb_build_object('governed_disposition_snapshot', {snapshot}::text))).*");
            _ = await context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        }
        catch (PostgresException error) when (error.SqlState == "23514") { rejected = true; }
        finally { await transaction.RollbackAsync(); }
        Assert.True(rejected != accepts && await ResultJson(context, id) == original);
    }
    internal async Task SqlGuardRequiresExactFullDispositionBindingsAndImmutableResults()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        await using var host = await Host(db, authority); var seed = await SeedAsync(db, host); var request = await Begin(host, authority, seed.Caller); var repaired = await Repair(host, seed.Caller, request);
        Assert.True(repaired.ErrorCode is null && repaired.Result is not null); var result = repaired.Result!;
        await using var check = db.Context(); var original = await ResultJson(check, result.ResultId); var disposition = JsonSerializer.Deserialize<RecoveryGovernedDisposition>(result.GovernedDispositionSnapshot!)!;
        await SqlDisposition(db, result.ResultId, original, result.GovernedDispositionSnapshot!, true);
        foreach (var field in BindingChanges) await SqlDisposition(db, result.ResultId, original, JsonSerializer.Serialize(Substitute(disposition, field)), false);
        await SqlDisposition(db, result.ResultId, original, JsonSerializer.Serialize(disposition with { EffectsDigest = AuthenticationIdentity.Digest(disposition.Goal + disposition.SessionEffect + disposition.UnresolvedEffectsJson) }), false);
        var guarded = false;
        try { _ = await check.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.recovery_results SET governed_disposition_snapshot='{{}}' WHERE result_id={result.ResultId}"); }
        catch (PostgresException error) when (error.SqlState == "23514") { guarded = true; }
        Assert.True(guarded && await ResultJson(check, result.ResultId) == original);
    }
    private static string IntervalSnapshot(string snapshot, string from, bool includesFrom, string? until, bool includesUntil)
    {
        var value = JsonNode.Parse(snapshot)!; value["EffectiveInterval"] = new JsonObject { ["From"] = from, ["IncludesFrom"] = includesFrom, ["Until"] = until, ["IncludesUntil"] = includesUntil };
        return value.ToJsonString();
    }
    internal async Task SqlIntervalPreservesHundredNanosecondsOffsetsInclusiveEdgesAndInvalidDates()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
        await using var host = await Host(db, authority); var seed = await SeedAsync(db, host); var request = await Begin(host, authority, seed.Caller); var repaired = await Repair(host, seed.Caller, request);
        Assert.True(repaired.ErrorCode is null && repaired.Result is not null); var result = repaired.Result!;
        await using var check = db.Context(); var original = await ResultJson(check, result.ResultId); var at = result.RecordedAt;
        string Stamp(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
        foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromHours(5.5), TimeSpan.FromHours(-4) })
        {
            var bound = at.ToOffset(offset);
            foreach (var sample in new[]
            {
                (From: bound, IncludesFrom: true, Until: bound, IncludesUntil: true, Accepts: true),
                (From: bound, IncludesFrom: false, Until: bound.AddTicks(1), IncludesUntil: true, Accepts: false),
                (From: bound.AddTicks(-1), IncludesFrom: true, Until: bound, IncludesUntil: false, Accepts: false),
                (From: bound.AddTicks(1), IncludesFrom: true, Until: bound.AddSeconds(1), IncludesUntil: true, Accepts: false),
                (From: bound.AddSeconds(-1), IncludesFrom: true, Until: bound.AddTicks(-1), IncludesUntil: true, Accepts: false),
                (From: bound.AddTicks(-1), IncludesFrom: false, Until: bound.AddTicks(1), IncludesUntil: false, Accepts: true),
                (From: bound.AddTicks(1), IncludesFrom: true, Until: bound.AddTicks(-1), IncludesUntil: true, Accepts: false)
            }) await SqlDisposition(db, result.ResultId, original, IntervalSnapshot(result.GovernedDispositionSnapshot!, Stamp(sample.From), sample.IncludesFrom, Stamp(sample.Until), sample.IncludesUntil), sample.Accepts);
        }
        await SqlDisposition(db, result.ResultId, original, IntervalSnapshot(result.GovernedDispositionSnapshot!, Stamp(at), true, null, false), true);
        foreach (var invalid in new[] { "2026-02-30T12:00:00+00:00", "2026-01-01T24:00:00+00:00", "2026-01-01T00:00:00+14:01", "2026-01-01T00:00:00", "2026-01-01T00:00:00.00000001Z", "not-a-timestamp" })
        {
            await SqlDisposition(db, result.ResultId, original, IntervalSnapshot(result.GovernedDispositionSnapshot!, invalid, true, null, false), false);
            await SqlDisposition(db, result.ResultId, original, IntervalSnapshot(result.GovernedDispositionSnapshot!, Stamp(at.AddSeconds(-1)), true, invalid, true), false);
        }
        foreach (var malformed in new[] { "{}", "null", "{\"From\":1,\"IncludesFrom\":true,\"Until\":null,\"IncludesUntil\":false}", "{\"From\":\"2026-01-01T00:00:00Z\",\"IncludesFrom\":\"true\",\"Until\":null,\"IncludesUntil\":false}" })
        {
            var value = JsonNode.Parse(result.GovernedDispositionSnapshot!)!; value["EffectiveInterval"] = JsonNode.Parse(malformed);
            await SqlDisposition(db, result.ResultId, original, value.ToJsonString(), false);
        }
    }
}
