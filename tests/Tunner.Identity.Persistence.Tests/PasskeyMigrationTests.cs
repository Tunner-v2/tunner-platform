using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PasskeyMigrationTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    [Fact]
    public async Task FreshMigrationMatchesCurrentModelAndInstallsAllImmutableGuards()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var context = db.Context();
        Assert.False(context.Database.HasPendingModelChanges()); Assert.Contains("20261109164622_P2PasskeyCeremonies", await context.Database.GetAppliedMigrationsAsync());
        var triggers = await context.Database.SqlQueryRaw<string>("SELECT tgname AS \"Value\" FROM pg_trigger WHERE tgname IN ('passkey_credential_guard','passkey_ceremony_guard','tunner_session_guard') AND NOT tgisinternal").ToArrayAsync();
        Assert.Equal(3, triggers.Length);
    }
    [Fact]
    public async Task AdditiveUpgradePreservesExistingPasswordSessionAndNewOrdinarySignInWorks()
    {
        await using var db = await fixture.CreateDatabaseAsync("20261109033631_P2OperationStepUpProofs");
        await using var host = await Start(db); var accountId = await host.SeedAsync(db);
        var id = Guid.NewGuid(); var key = new string('A', 64); var now = host.Clock.GetUtcNow(); var expires = now + host.Policies.Policy.AbsoluteLifetime;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, accountId), new(AuthenticationIdentity.SessionClaim, id.ToString("N")),
                new(AuthenticationIdentity.OriginClaim, Origin), new("amr", "pwd"), new("acr", host.Policies.Policy.PasswordAssurance)], AuthenticationIdentity.Scheme));
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = expires, IsPersistent = false, AllowRefresh = false }, AuthenticationIdentity.Scheme);
            var protectedTicket = protection.CreateProtector("Tunner.User.Session.Ticket.v1").Protect(Convert.ToBase64String(TicketSerializer.Default.Serialize(ticket)));
            var protectedKey = protection.CreateProtector("Tunner.User.Session.RetryKey.v1").Protect(key);
            await using var context = db.Context(); var user = await context.Users.SingleAsync(); var stamp = AuthenticationIdentity.Digest(user.SecurityStamp!);
            var digest = AuthenticationIdentity.Digest(key); var browser = new string('B', 64); var requestId = Guid.NewGuid(); var idle = host.Policies.Policy.IdleLifetime.Ticks;
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.tunner_sessions(session_id,key_digest,protected_key,protected_ticket,browser_digest,idempotency_key,identifier_digest,remember_me,account_id,security_stamp_digest,
                  scheme,origin,authentication_methods,assurance,authenticated_at,last_seen_at,expires_at,idle_ticks,state,policy_version,access_policy_version,access_evidence_reference,
                  processing_reference,retention_reference,operation_id,correlation_id,version)
                VALUES ({id},{digest},{protectedKey},{protectedTicket},{browser},{requestId},{browser},false,{accountId},{stamp},{AuthenticationIdentity.Scheme},{Origin},'pwd',
                  {host.Policies.Policy.PasswordAssurance},{now},{now},{expires},{idle},'ACTIVE',{host.Policies.Policy.Version},'fixture-access-v1','fixture-access-evidence','fixture-processing','fixture-retention','OP-legacy','fixture-legacy',1)
                """);
            await context.GetService<IMigrator>().MigrateAsync(); Assert.False(context.Database.HasPendingModelChanges());
            var legacy = await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(); Assert.Equal(id, legacy.SessionId); Assert.Null(legacy.PasskeyCredentialReference); Assert.Null(legacy.TerminationCause);
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var recovered = await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().RetrieveAsync(key, Origin, CancellationToken.None);
            Assert.NotNull(recovered); Assert.Equal(id, AuthenticationIdentity.SessionId(recovered.Principal));
        }
        var bootstrap = await host.Options(); using var signed = await host.Post("/authentication/sign-in", Command(bootstrap.Key), bootstrap.Cookies, bootstrap.Csrf);
        signed.EnsureSuccessStatusCode(); using var live = await host.Get("/authentication/session", Merge(bootstrap.Cookies, signed)); live.EnsureSuccessStatusCode();
        await using var check = db.Context(); Assert.Equal(2, await check.Set<AuthenticationSessionRow>().CountAsync());
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.tunner_sessions SET passkey_policy_version='forged',version=version+1 WHERE session_id={id}"));
        Assert.Equal("23514", rejected.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => check.GetService<IMigrator>().MigrateAsync("20261109033631_P2OperationStepUpProofs"));
    }
}
