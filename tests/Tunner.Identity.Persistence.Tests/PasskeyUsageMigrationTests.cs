using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class PasskeyTests
{
    [Fact]
    public async Task OperationProofUpgradePreservesHistoricalLifecycleNativeCounterSessionAndPendingReview()
    {
        await using var db = await fixture.CreateDatabaseAsync("20261109164622_P2PasskeyCeremonies"); await using var host = await StartPasskeys(db); var account = await host.SeedAsync(db);
        var session = await host.SeedHistoricalPasswordSessionAsync(db, account); var cookies = session.Cookies;
        using var key = new PasskeyAuthenticator(); var reference = Guid.NewGuid(); var ceremony = Guid.NewGuid(); var request = Guid.NewGuid(); var now = host.Clock.GetUtcNow();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAdapter>>(); var user = (await manager.FindByEmailAsync("person@example.test"))!;
            var handler = new PasskeyHandler<IdentityUserAdapter>(manager, Options.Create(new IdentityPasskeyOptions { ServerDomain = "authentication.example.test", UserVerificationRequirement = "required", ResidentKeyRequirement = "required", AttestationConveyancePreference = "none",
                IsAllowedAlgorithm = algorithm => algorithm == -7, ValidateOrigin = context => ValueTask.FromResult(context.Origin == Origin && !context.CrossOrigin && context.TopOrigin is null) }));
            var http = new DefaultHttpContext(); http.Request.Scheme = "https"; http.Request.Host = new HostString("authentication.example.test");
            var options = await handler.MakeCreationOptionsAsync(new PasskeyUserEntity { Id = user.Id, Name = account, DisplayName = account }, http);
            var verified = await handler.PerformAttestationAsync(new PasskeyAttestationContext { HttpContext = http, AttestationState = options.AttestationState!, CredentialJson = key.Attest(JsonSerializer.Deserialize<JsonElement>(options.CreationOptionsJson), Origin).GetRawText() });
            Assert.True(verified.Succeeded, verified.Failure?.Message); var value = verified.Passkey;
            Assert.True((await manager.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo(value.CredentialId, value.PublicKey, now, 37, value.Transports, value.IsUserVerified, value.IsBackupEligible, value.IsBackedUp, [], []))).Succeeded);
            await using var before = db.Context();
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(key.CredentialId)); var browser = AuthenticationIdentity.Digest(session.SessionId.ToString("N") + "|" + account + "|" + Origin);
            await before.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.passkey_credentials(credential_reference,account_id,credential_id,credential_digest,state,created_at,last_used_at,policy_version,processing_reference,retention_reference,version)
                VALUES ({reference},{account},{key.CredentialId},{digest},'ACTIVE',{now},{now},'fixture-passkey-v1','fixture-processing','fixture-retention',17)
                """);
            var expires = now.AddMinutes(1); var recovery = now.AddMinutes(3);
            await before.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.passkey_ceremonies(ceremony_id,request_id,kind,browser_digest,account_id,session_id,origin,policy_version,authentication_policy_version,security_stamp_digest,
                  protected_state,assurance_policy_version,options_json,request_digest,remember_me,credential_reference,expected_version,state,created_at,expires_at,recovery_expires_at,processing_reference,retention_reference,version)
                VALUES ({ceremony},{request},'REVOKE',{browser},{account},{session.SessionId},{Origin},'fixture-passkey-v1',{host.Policies.Policy.Version},{session.SecurityStampDigest},
                  '','fixture-revocation-v1','','',false,{reference},17,'PENDING',{now},{expires},{recovery},'fixture-processing','fixture-retention',1)
                """);
            await before.GetService<IMigrator>().MigrateAsync(); Assert.False(before.Database.HasPendingModelChanges());
            var migrated = await before.Set<PasskeyCredentialRow>().AsNoTracking().SingleAsync(); Assert.Equal(17, migrated.Version); Assert.Equal(0, migrated.UsageVersion); Assert.Equal(now, migrated.LastUsedAt);
            Assert.Equal(session.SessionId, await before.Set<AuthenticationSessionRow>().Select(x => x.SessionId).SingleAsync()); Assert.Equal(17, await before.Set<PasskeyCeremonyRow>().Select(x => x.ExpectedVersion).SingleAsync());
        }
        await SignInPasskey(host, key, counterOverride: 38);
        await using (var read = db.Context())
        {
            var row = await read.Set<PasskeyCredentialRow>().AsNoTracking().SingleAsync(); Assert.Equal(17, row.Version); Assert.Equal(1, row.UsageVersion);
            var pending = await read.Set<PasskeyCeremonyRow>().AsNoTracking().SingleAsync(x => x.CeremonyId == ceremony); Assert.Equal(17, pending.ExpectedVersion);
            await using var scope = host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAdapter>>(); var user = (await manager.FindByPasskeyIdAsync(key.CredentialId))!;
            Assert.Equal(38U, (await manager.GetPasskeyAsync(user, key.CredentialId))!.SignCount);
            var http = new DefaultHttpContext(); PasskeyStepUpOwner.Select(http, pending, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), now);
            using var selection = new HttpResponseMessage(); selection.Headers.TryAddWithoutValidation("Set-Cookie", http.Response.Headers.SetCookie.ToArray()); cookies = Merge(cookies, selection);
        }
        await Authorize(host, new(request, ceremony, cookies, ""));
        await using var ended = db.Context(); var credential = await ended.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal("REVOKED", credential.State); Assert.Equal(18, credential.Version); Assert.Equal(1, credential.UsageVersion);
        Assert.Equal("COMPLETED", await ended.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == ceremony).Select(x => x.State).SingleAsync());
    }
    [Fact]
    public async Task OperationProofMigrationRoundTripRestoresPriorGuardsAndAllRegisteredMigrations()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var context = db.Context(); var migrations = context.Database.GetMigrations().ToArray();
        Assert.Contains("20261110092326_P2PasskeyOperationProofs", migrations); Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
        await context.GetService<IMigrator>().MigrateAsync("20261109164622_P2PasskeyCeremonies");
        var prior = await context.Database.SqlQueryRaw<string>("SELECT pg_get_functiondef('identity.guard_passkey_credential()'::regprocedure) AS \"Value\"").SingleAsync(); Assert.DoesNotContain("usage_version", prior, StringComparison.Ordinal);
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema='identity' AND table_name='passkey_credentials' AND column_name='usage_version'").SingleAsync());
        await context.Database.MigrateAsync(); Assert.False(context.Database.HasPendingModelChanges()); Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
        var active = await context.Database.SqlQueryRaw<string>("SELECT pg_get_functiondef('identity.guard_passkey_credential()'::regprocedure) AS \"Value\"").SingleAsync(); Assert.Contains("usage_version", active, StringComparison.Ordinal);
    }
    [Fact]
    public async Task OperationProofSourceAndCeremonyBindingAreImmutableAndUsageCannotAlterLifecycle()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await StartPasskeys(db, passkeyProof: true); await host.SeedAsync(db); using var key = new PasskeyAuthenticator();
        var enrolled = await Enroll(host, await Login(host), key); await AuthorizeNative(host, await Begin(host, enrolled.Pending.Cookies), key);
        await using var context = db.Context();
        foreach (var sql in new[] { "UPDATE identity.step_up_proofs SET passkey_assurance_key='forged',version=version+1 WHERE authentication_method LIKE 'passkey:%'", "UPDATE identity.passkey_ceremonies SET bound_expected_version=bound_expected_version+1,version=version+1 WHERE kind='STEP_UP'", "UPDATE identity.passkey_credentials SET version=version+1 WHERE state='ACTIVE'", "UPDATE identity.passkey_credentials SET usage_version=usage_version+2,last_used_at=created_at WHERE state='ACTIVE'" })
        { var rejected = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql)); Assert.Equal("23514", rejected.SqlState); }
        var source = await context.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal(1, source.Version); Assert.Equal(1, source.UsageVersion);
    }
}
