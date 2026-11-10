using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class TotpTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    // All policy/content/provider data below is synthetic isolated test input, never a production selection.
    internal sealed class Profile : ITotpPolicySource, ITotpSeedStore, ITotpMutationEligibilitySource, ITotpMfaRequirementSource, ITotpPresentationSource, IAssurancePolicySource
    {
        internal TotpPolicy Policy { get; set; } = new("fixture-totp-v1", Origin, "Fixture only", "10.0.13", "SHA1", 6, 30, 2,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(15), 10_000, 16, 3, 8,
            TotpSessionEffect.PreserveExistingSessions, TotpSessionEffect.PreserveExistingSessions, TotpReplacementOrdering.IndependentEnrollment,
            TotpResetBehavior.RetireAffectedFactorThenSeparateEnrollment, "fixture-proof", "fixture-proof", "fixture-proof", "fixture-proof",
            new Dictionary<string, string> { ["pwd"] = "fixture-mfa", ["passkey"] = "fixture-mfa" }, "fixture-binding", "fixture-processing", "fixture-retention");
        private readonly ConcurrentDictionary<TotpSeedReference, (TotpSeedBinding Binding, string Seed)> values = new();
        internal bool RequireMfa { get; set; }
        internal bool AllowMutation { get; set; } = true;
        internal bool ThrowAfterCreate { get; set; }
        internal bool ThrowBeforeCreate { get; set; }
        internal int Creates;
        internal Action? BeforeMfaDecision { get; set; }
        public TotpPolicy RequireEffective(DateTimeOffset effectiveAt) { Policy.Validate(); return Policy; }
        public TotpSeedReference Allocate(TotpSeedBinding binding) => new("fixture-vault", "fixture/data/totp/" + binding.AuthenticatorReference.ToString("N") + "/" + binding.Generation.ToString("N"), 1);
        public Task CreateOnceAsync(TotpSeedBinding binding, TotpSeedReference reference, string seed, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Creates);
            if (ThrowBeforeCreate) throw new InvalidOperationException("Synthetic unknown provider result.");
            if (reference != Allocate(binding) || !values.TryAdd(reference, (binding, seed))) throw new InvalidOperationException("Synthetic CAS rejection.");
            if (ThrowAfterCreate) throw new InvalidOperationException("Synthetic lost create response."); return Task.CompletedTask;
        }
        public Task<TotpSeedValue?> ReadAsync(TotpSeedBinding binding, TotpSeedReference reference, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(reference, out var item) && item.Binding == binding ? new TotpSeedValue(item.Seed) : null);
        internal string Seed(Guid reference) => values.Single(x => x.Value.Binding.AuthenticatorReference == reference).Value.Seed;
        public ValueTask<TotpMutationEligibilityDecision> RequireAsync(TotpMutationEligibilityRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new TotpMutationEligibilityDecision(request.AccountId, request.AccountVersion, request.AuthenticatorReference, request.AuthenticatorVersion,
                request.Operation, request.AlternativeMethodsDigest, AllowMutation, "fixture-eligibility", "fixture-evidence", Interval(request.EffectiveAt)));
        public ValueTask<TotpMfaRequirementDecision> RequireAsync(TotpMfaRequirementRequest request, CancellationToken cancellationToken)
        {
            BeforeMfaDecision?.Invoke();
            return ValueTask.FromResult(new TotpMfaRequirementDecision(request.AccountId, request.AccountVersion, request.PrimaryMethod, request.PrimaryAssurance, RequireMfa,
                "fixture-mfa-requirement", "fixture-evidence", Interval(request.EffectiveAt), TimeSpan.FromMinutes(2)));
        }
        public TotpPresentation RequirePublished(TotpPolicy policy) => new("en", [new("fixture.only", "fixture", "fixture", "SYNTHETIC-TEST-ONLY", "en", "PUBLISHED", "Fixture only")]);
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null)
        {
            if (expectedVersion is not null && expectedVersion != "fixture-proof") throw new AssurancePolicyVersionConflictException();
            return new(new("fixture-proof", "ACTIVE", scope, Interval(executedAt), null, ["SYNTHETIC-TEST-ONLY"]), "fixture-proof", TimeSpan.FromMinutes(5),
                new Dictionary<string, string> { ["pwd"] = "fixture-proof", ["totp"] = "fixture-proof" });
        }
        private static EffectivePolicyInterval Interval(DateTimeOffset now) => new(now.AddHours(-1), true, now.AddHours(1), false);
    }
    internal static async Task<AuthenticationTestHost> StartTotp(IdentityTestDatabase db, Profile profile)
    {
        var host = await Start(db, services =>
        {
            services.AddSingleton<ITotpPolicySource>(profile); services.AddSingleton<ITotpSeedStore>(profile); services.AddSingleton<ITotpMutationEligibilitySource>(profile);
            services.AddSingleton<ITotpMfaRequirementSource>(profile); services.AddSingleton<ITotpPresentationSource>(profile); services.AddSingleton<IAssurancePolicySource>(profile);
            services.AddSingleton<IStepUpPresentationSource, StepUpBrowserTests.Presentation>();
        });
        host.Clock.SynchronizeToSystem(); return host;
    }
    internal sealed record Pending(Guid RequestId, Guid EnrollmentId, Guid Reference, string Cookies, string Csrf);
    internal static async Task<string> Login(AuthenticationTestHost host)
    {
        var boot = await host.Options(); using var response = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return Merge(boot.Cookies, response);
    }
    internal static async Task<Pending> Begin(AuthenticationTestHost host, string cookies, string kind = "ENROLL", Guid? reference = null, long version = 0)
    {
        using var options = await host.Get("/account/totp/options", cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var view = await options.Content.ReadFromJsonAsync<JsonElement>(); var request = view.GetProperty("requestId").GetGuid(); var csrf = view.GetProperty("csrfToken").GetString()!;
        cookies = Merge(cookies, options);
        using var started = await host.Post("/account/totp/" + (kind == "ENROLL" ? "enrollment" : kind.ToLowerInvariant()) + "/start",
            new { requestId = request, authenticatorReference = reference, expectedVersion = version }, cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode); var operation = await started.Content.ReadFromJsonAsync<JsonElement>();
        return new(request, operation.GetProperty("enrollmentId").GetGuid(), operation.GetProperty("authenticatorReference").GetGuid(), Merge(cookies, started), csrf);
    }
    internal static async Task<Pending> Authorize(AuthenticationTestHost host, Pending pending, string? totpCode = null, Guid? proofFactor = null)
    {
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var view = await options.Content.ReadFromJsonAsync<JsonElement>(); var cookies = Merge(pending.Cookies, options); var csrf = view.GetProperty("csrfToken").GetString()!;
        using var result = totpCode is null ? await host.Post("/authentication/step-up/password", new { requestId = view.GetProperty("requestId").GetGuid(), password = Password }, cookies, csrf) :
            await host.Post("/authentication/step-up/totp", new { requestId = view.GetProperty("requestId").GetGuid(), attemptId = Guid.NewGuid(), authenticatorReference = proofFactor, code = totpCode }, cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode); return pending with { Cookies = Merge(cookies, result), Csrf = csrf };
    }
    internal static async Task<Pending> Enroll(AuthenticationTestHost host, Profile profile, string cookies)
    {
        var pending = await Authorize(host, await Begin(host, cookies));
        using var disclosed = await host.Post("/account/totp/enrollment/disclose", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId }, pending.Cookies, pending.Csrf);
        Assert.Equal(HttpStatusCode.OK, disclosed.StatusCode);
        using var activated = await host.Post("/account/totp/enrollment/activate", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId, attemptId = Guid.NewGuid(), code = NativeCode(profile.Seed(pending.Reference)) }, pending.Cookies, pending.Csrf);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode); return pending with { Cookies = Merge(pending.Cookies, activated) };
    }
    // Invoke the pinned framework's own base32 and RFC6238 helpers solely to create native fixtures. No copied OTP math.
    internal static string NativeCode(string seed, int counterOffset = 0)
    {
        var assembly = typeof(AuthenticatorTokenProvider<IdentityUserAdapter>).Assembly;
        var decoder = assembly.GetType("Microsoft.AspNetCore.Identity.Base32", throwOnError: true)!.GetMethod("FromBase32", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var bytes = (byte[])decoder.Invoke(null, [seed])!;
        try
        {
            var compute = assembly.GetType("Microsoft.AspNetCore.Identity.Rfc6238AuthenticationService", throwOnError: true)!.GetMethod("ComputeTotp", BindingFlags.Static | BindingFlags.NonPublic)!;
            return ((int)compute.Invoke(null, [bytes, (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + counterOffset), null])!).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    [Fact]
    public async Task FreshProofOneTimeDisclosureAndFirstNativeCodeAreSeparateAtomicStages()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var pending = await Begin(host, await Login(host));
        using (var early = await host.Post("/account/totp/enrollment/disclose", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId }, pending.Cookies, pending.Csrf)) Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Equal(0, profile.Creates); pending = await Authorize(host, pending);
        using (var disclosed = await host.Post("/account/totp/enrollment/disclose", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId }, pending.Cookies, pending.Csrf))
        {
            Assert.Equal(HttpStatusCode.OK, disclosed.StatusCode); Assert.True(disclosed.Headers.CacheControl?.NoStore);
            var response = await disclosed.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(response.GetProperty("manualKey").GetString()!.Length > 0);
            var nativeUri = response.GetProperty("otpAuthUri").GetString()!.StartsWith("otpauth://", StringComparison.Ordinal); Assert.True(nativeUri);
        }
        await using (var read = db.Context()) { Assert.Equal("INACTIVE", (await read.Set<TotpAuthenticatorRow>().SingleAsync()).State); Assert.Empty(await read.Set<TotpUsedCodeRow>().ToArrayAsync()); }
        using (var repeated = await host.Post("/account/totp/enrollment/disclose", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId }, pending.Cookies, pending.Csrf)) Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        var code = NativeCode(profile.Seed(pending.Reference)); var attempt = Guid.NewGuid();
        using (var active = await host.Post("/account/totp/enrollment/activate", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId, attemptId = attempt, code }, pending.Cookies, pending.Csrf)) Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        using (var retry = await host.Post("/account/totp/enrollment/activate", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId, attemptId = attempt, code }, pending.Cookies, pending.Csrf)) Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await using var check = db.Context(); Assert.Equal("ACTIVE", (await check.Set<TotpAuthenticatorRow>().SingleAsync()).State); Assert.Single(await check.Set<TotpUsedCodeRow>().ToArrayAsync());
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorAdded"));
        Assert.True(await check.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM identity.\"AspNetUserTokens\"").SingleAsync() == 0);
        var payloads = await check.Set<OutboxMessage>().Select(x => x.Payload).ToArrayAsync(); Assert.True(payloads.All(x => !x.Contains(profile.Seed(pending.Reference), StringComparison.Ordinal) && !x.Contains(code, StringComparison.Ordinal)));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task UnknownProviderCreateReconcilesOnlyItsImmutableTargetAndNeverRegenerates(bool created)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile { ThrowAfterCreate = created, ThrowBeforeCreate = !created };
        await using var host = await StartTotp(db, profile); await host.SeedAsync(db); var pending = await Authorize(host, await Begin(host, await Login(host)));
        using (var first = await host.Post("/account/totp/enrollment/disclose", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId }, pending.Cookies, pending.Csrf)) Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        using (var reconciled = await host.Post("/account/totp/enrollment/disclose", new { requestId = pending.RequestId, enrollmentId = pending.EnrollmentId }, pending.Cookies, pending.Csrf)) Assert.Equal(created ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, reconciled.StatusCode);
        Assert.Equal(1, profile.Creates);
    }
    [Theory]
    [InlineData("DISABLE", TotpSessionEffect.PreserveExistingSessions)]
    [InlineData("DISABLE", TotpSessionEffect.RevokeFactorSessions)]
    [InlineData("RESET", TotpSessionEffect.PreserveExistingSessions)]
    [InlineData("RESET", TotpSessionEffect.RevokeFactorSessions)]
    public async Task SeparateDisableAndResetUseOwnExactProofAndExplicitFactorSessionEffect(string kind, TotpSessionEffect effect)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); profile.Policy = profile.Policy with { DisableSessionEffect = effect, ResetSessionEffect = effect };
        await using var host = await StartTotp(db, profile); await host.SeedAsync(db); var enrolled = await Enroll(host, profile, await Login(host));
        profile.RequireMfa = true; var boot = await host.Options();
        using var primary = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); var cookies = Merge(boot.Cookies, primary);
        using var mfaOptions = await host.Get("/authentication/totp/options", cookies); var view = await mfaOptions.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, mfaOptions);
        using var mfa = await host.Post("/authentication/totp/complete", new { requestId = boot.Key, authenticatorReference = enrolled.Reference, code = NativeCode(profile.Seed(enrolled.Reference), 1) }, cookies, view.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK, mfa.StatusCode); cookies = Merge(cookies, mfa);
        var mutation = await Authorize(host, await Begin(host, cookies, kind, enrolled.Reference, 2), NativeCode(profile.Seed(enrolled.Reference), 2), enrolled.Reference);
        using var live = await host.Get("/authentication/session", mutation.Cookies); Assert.Equal(effect == TotpSessionEffect.PreserveExistingSessions ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, live.StatusCode);
        await using var read = db.Context(); var factor = await read.Set<TotpAuthenticatorRow>().SingleAsync(); Assert.Equal("DISABLED", factor.State); Assert.Equal(3, factor.Version);
        var operation = await read.Set<TotpOperationRow>().SingleAsync(x => x.EnrollmentId == mutation.EnrollmentId); Assert.Equal(kind, operation.Kind); Assert.Equal("COMPLETED", operation.State);
        Assert.Equal(2, await read.Set<TotpOperationRow>().CountAsync()); Assert.Equal(1, profile.Creates);
    }
    [Fact]
    public async Task RequiredMfaCreatesNoEarlySessionAndLostFinalResponseRecoversWithoutCode()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var enrolled = await Enroll(host, profile, await Login(host)); profile.RequireMfa = true; var boot = await host.Options();
        using var primary = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
        Assert.Equal("MFA_REQUIRED", (await primary.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()); var cookies = Merge(boot.Cookies, primary);
        await using (var read = db.Context()) { Assert.Equal(1, await read.Set<AuthenticationSessionRow>().CountAsync()); Assert.Single(await read.Set<TotpPendingAuthenticationRow>().ToArrayAsync()); }
        using var options = await host.Get("/authentication/totp/options", cookies); var view = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options); var csrf = view.GetProperty("csrfToken").GetString();
        var code = NativeCode(profile.Seed(enrolled.Reference), 1);
        using (var completed = await host.Post("/authentication/totp/complete", new { requestId = boot.Key, authenticatorReference = enrolled.Reference, code }, cookies, csrf)) Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        // Deliberately discard final Set-Cookie response, then recover using original protected pending attempt only.
        using var recovered = await host.Post("/authentication/totp/result", new { requestId = boot.Key }, cookies, csrf); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using var session = await host.Get("/authentication/session", Merge(cookies, recovered)); Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        await using var check = db.Context(); Assert.Equal(2, await check.Set<AuthenticationSessionRow>().CountAsync()); Assert.Equal(2, await check.Set<TotpUsedCodeRow>().CountAsync());
        Assert.Equal("COMPLETED", (await check.Set<TotpPendingAuthenticationRow>().SingleAsync()).State);
    }
    [Fact]
    public async Task AdditiveMigrationRoundTripPreservesOldAuthModelAndInstallsImmutableGuards()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var context = db.Context(); Assert.False(context.Database.HasPendingModelChanges());
        var guards = await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger WHERE tgname IN ('totp_authenticator_guard','totp_operation_guard','totp_pending_guard','totp_replay_guard','totp_attempt_guard') AND NOT tgisinternal").SingleAsync(); Assert.Equal(5, guards);
        await context.GetService<IMigrator>().MigrateAsync("20261110092326_P2PasskeyOperationProofs"); await context.Database.MigrateAsync(); Assert.False(context.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task LiveTotpEvidencePreventsDestructiveMigrationDowngrade()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var enrolled = await Enroll(host, profile, await Login(host)); await using var context = db.Context();
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.GetService<IMigrator>().MigrateAsync("20261110092326_P2PasskeyOperationProofs"));
        Assert.Equal("23514", error.SqlState); Assert.True(await context.Set<TotpAuthenticatorRow>().AnyAsync(x => x.AuthenticatorReference == enrolled.Reference && x.State == "ACTIVE"));
        Assert.Single(await context.Set<TotpUsedCodeRow>().ToArrayAsync());
    }
    [Fact]
    public async Task OriginalPendingAttemptCannotBecomePrimaryOnlyAfterRequirementChanges()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile { RequireMfa = true }; await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var boot = await host.Options(); using var first = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        profile.RequireMfa = false;
        using var changed = await host.Post("/authentication/sign-in", Command(boot.Key), Merge(boot.Cookies, first), boot.Csrf); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        await using var context = db.Context(); Assert.Empty(await context.Set<AuthenticationSessionRow>().ToArrayAsync()); Assert.Equal("PENDING", (await context.Set<TotpPendingAuthenticationRow>().SingleAsync()).State);
    }
    [Fact]
    public async Task PendingMfaOptionsRequireItsExactOriginalAuthenticationBrowser()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile { RequireMfa = true }; await using var host = await StartTotp(db, profile); await host.SeedAsync(db);
        var boot = await host.Options(); using var primary = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf); Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
        var original = Merge(boot.Cookies, primary); var other = await host.Options();
        var pendingSelection = original.Split(';', StringSplitOptions.TrimEntries).Single(x => x.StartsWith(TotpBrowserService.MfaCookieName + "=", StringComparison.Ordinal));
        using var wrongBrowser = await host.Get("/authentication/totp/options", other.Cookies + "; " + pendingSelection); Assert.Equal(HttpStatusCode.NotFound, wrongBrowser.StatusCode);
        using var correct = await host.Get("/authentication/totp/options", original); Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
    }
}
