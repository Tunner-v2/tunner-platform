using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class PasskeyTests
{
    private sealed class NativeStorageObserver : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Stored { get; private set; }
        private bool saving;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            saving = Armed && eventData.Context?.ChangeTracker.Entries().Any(x => x.State == EntityState.Modified && x.Metadata.GetTableName() == "AspNetUserPasskeys") == true;
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Stored |= saving; return ValueTask.FromResult(result); }
    }
    [Theory]
    [InlineData("proof")][InlineData("session")][InlineData("access")][InlineData("policy")]
    public async Task NativeProofFinalAuthorityBoundaryRollsBackCounterUsageProofAndCeremony(string expired)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var observer = new NativeStorageObserver(); var policy = new Policies();
        await using var host = await StartPasskeys(db, policy, configure: services => services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(observer)), passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var native = await BeginNativeProof(host, await Begin(host, enrolled.Pending.Cookies)); observer.Armed = true;
        if (expired == "session") host.Policies.Policy = host.Policies.Policy with { IdleLifetime = TimeSpan.FromSeconds(1) };
        if (expired == "access") host.Access.DecisionLifetime = TimeSpan.FromSeconds(1);
        var advanced = false;
        host.Access.BeforeDecision = request => { if (!advanced && observer.Stored && request.Operation == "REGISTER_PASSKEY") { advanced = true; if (expired == "policy") policy.Version = "fixture-passkey-v2"; else host.Clock.Advance(expired == "proof" ? TimeSpan.FromSeconds(46) : TimeSpan.FromSeconds(2)); } };
        using var rejected = await CompleteNativeProof(host, native, key.Assert(native.Options, Origin)); Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode); Assert.True(observer.Stored); Assert.True(advanced);
        await using var read = db.Context(); var source = await read.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal(1, source.Version); Assert.Equal(0, source.UsageVersion); Assert.Null(source.LastUsedAt);
        Assert.Empty(await read.Set<StepUpProofRow>().Where(x => x.AuthenticationMethod.StartsWith("passkey:")).ToArrayAsync()); Assert.Equal("REJECTED", await read.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == native.CeremonyId).Select(x => x.State).SingleAsync());
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "PasskeyOperationProofVerified"));
        await using var scope = host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAdapter>>(); var user = (await manager.FindByPasskeyIdAsync(key.CredentialId))!; Assert.Equal(0U, (await manager.GetPasskeyAsync(user, key.CredentialId))!.SignCount);
    }
    [Fact]
    public async Task EnrollmentIdleExpiryDuringFinalAuthorityRollsBackNativeKeyAndActivation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db);
        var pending = await Authorize(host, await Begin(host, await Login(host))); var options = await EnrollmentOptions(host, pending); using var key = new PasskeyAuthenticator();
        host.Policies.Policy = host.Policies.Policy with { IdleLifetime = TimeSpan.FromSeconds(1) }; var calls = 0;
        host.Access.BeforeDecision = request => { if (request.Operation == "REGISTER_PASSKEY" && ++calls == 3) host.Clock.Advance(TimeSpan.FromSeconds(2)); };
        using var denied = await host.Post("/account/passkeys/enrollment/complete", new { pending.RequestId, pending.CeremonyId, credential = key.Attest(options.Options, Origin) }, pending.Cookies, pending.Csrf); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal(3, calls);
        await using var read = db.Context(); Assert.Empty(await read.Set<PasskeyCredentialRow>().ToArrayAsync()); Assert.Equal("AUTHORIZED", await read.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == pending.CeremonyId).Select(x => x.State).SingleAsync());
        Assert.Equal(0, await read.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM identity.\"AspNetUserPasskeys\"").SingleAsync()); Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorAdded"));
    }
    [Fact]
    public async Task AnotherKeysNativeUsageDoesNotInvalidateReviewedTargetLifecycle()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db); using var source = new PasskeyAuthenticator(); using var target = new PasskeyAuthenticator();
        var first = await Enroll(host, await Login(host), source); var second = await Enroll(host, first.Pending.Cookies, target); var native = await BeginNativeProof(host, await Begin(host, second.Pending.Cookies, second.Reference, 1));
        await using var before = db.Context(); var originalStamp = await before.Users.Select(x => x.ConcurrencyStamp).SingleAsync(); await SignInPasskey(host, source);
        using var removed = await CompleteNativeProof(host, native, source.Assert(native.Options, Origin)); Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync());
        await using var read = db.Context(); var a = await read.Set<PasskeyCredentialRow>().SingleAsync(x => x.CredentialReference == first.Reference); var b = await read.Set<PasskeyCredentialRow>().SingleAsync(x => x.CredentialReference == second.Reference);
        Assert.Equal(1, a.Version); Assert.Equal(2, a.UsageVersion); Assert.Equal("ACTIVE", a.State); Assert.Equal(2, b.Version); Assert.Equal(0, b.UsageVersion); Assert.Equal("REVOKED", b.State); Assert.NotEqual(originalStamp, await read.Users.Select(x => x.ConcurrencyStamp).SingleAsync());
    }
    [Fact]
    public async Task ConcurrentSignedUsageSerializesCounterAndUsageWithoutAdvancingLifecycle()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db); await host.SeedAsync(db); using var key = new PasskeyAuthenticator(); await Enroll(host, await Login(host), key);
        var a = await AssertionOptions(host); var b = await AssertionOptions(host); var first = key.Assert(a.Ceremony.GetProperty("publicKey"), Origin); var second = key.Assert(b.Ceremony.GetProperty("publicKey"), Origin);
        var results = await Task.WhenAll(host.Post("/authentication/passkeys/sign-in", new { requestId = a.Bootstrap.Key, ceremonyId = a.Ceremony.GetProperty("ceremonyId").GetGuid(), credential = first }, a.Bootstrap.Cookies, a.Bootstrap.Csrf),
            host.Post("/authentication/passkeys/sign-in", new { requestId = b.Bootstrap.Key, ceremonyId = b.Ceremony.GetProperty("ceremonyId").GetGuid(), credential = second }, b.Bootstrap.Cookies, b.Bootstrap.Csrf));
        try { Assert.All(results, x => Assert.True(x.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized)); Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
            var successes = results.Count(x => x.IsSuccessStatusCode); await using var read = db.Context(); var row = await read.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal(1, row.Version); Assert.Equal(successes, row.UsageVersion); Assert.Equal(successes, await read.Set<AuthenticationSessionRow>().CountAsync(x => x.AuthenticationMethods == "passkey"));
            await using var scope = host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAdapter>>(); var user = (await manager.FindByPasskeyIdAsync(key.CredentialId))!; Assert.Equal(2U, (await manager.GetPasskeyAsync(user, key.CredentialId))!.SignCount);
        } finally { foreach (var result in results) result.Dispose(); }
    }
    private sealed class RevokeBetweenNativeIssuanceAndExecution : DbTransactionInterceptor
    {
        internal Func<Task>? Revoke { get; set; }
        internal bool Fired { get; private set; }
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Fired || Revoke is null || eventData.Context?.ChangeTracker.Entries<PasskeyCeremonyRow>().Any(x => x.Entity.Kind == "STEP_UP" && x.Entity.State == "COMPLETED") != true ||
                !eventData.Context.ChangeTracker.Entries<StepUpProofRow>().Any(x => x.Entity.AuthenticationMethod.StartsWith("passkey:", StringComparison.Ordinal) && x.Entity.State == "ACTIVE")) return;
            Fired = true; await Revoke();
        }
    }
    [Fact]
    public async Task SourceRevokedAfterNativeProofCommitCannotAuthorizeOwnerThroughTrackedCredentialCache()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var race = new RevokeBetweenNativeIssuanceAndExecution();
        await using var host = await StartPasskeys(db, configure: services => services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(race)), passkeyProof: true); await host.SeedAsync(db);
        using var key = new PasskeyAuthenticator(); var enrolled = await Enroll(host, await Login(host), key); var native = await BeginNativeProof(host, await Begin(host, enrolled.Pending.Cookies));
        race.Revoke = async () => { await Authorize(host, await Begin(host, enrolled.Pending.Cookies, enrolled.Reference, 1)); };
        using var denied = await CompleteNativeProof(host, native, key.Assert(native.Options, Origin)); Assert.True(race.Fired); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var read = db.Context(); var source = await read.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal("REVOKED", source.State); Assert.Equal(2, source.Version); Assert.Equal(1, source.UsageVersion);
        Assert.Equal("PENDING", await read.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == native.Owner.CeremonyId).Select(x => x.State).SingleAsync());
        var proof = await read.Set<StepUpProofRow>().SingleAsync(x => x.AuthenticationMethod.StartsWith("passkey:")); Assert.Equal("ACTIVE", proof.State);
        Assert.Equal(1, await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorAdded")); Assert.Equal(1, await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "AuthenticatorRevoked"));
        using var options = await host.Get("/authentication/step-up/options", native.Cookies); var recovery = (await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("passkeyRecovery"); Assert.Equal("EXPIRED", recovery.GetProperty("status").GetString());
    }

}
