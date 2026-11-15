using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class DeviceTrustTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private static readonly string Network = new('B', 64);
    // Every value below is synthetic, including the safe vocabulary, privacy inventory and no-session-cascade qualification.
    internal sealed class Qualification : IDeviceEvidencePolicySource, IDeviceTrustEligibilitySource, IDeviceSafeCharacteristicSource
    {
        internal bool Available { get; set; } = true;
        internal bool AllowTrust { get; set; } = true;
        internal bool AllowRegrant { get; set; }
        internal bool Unsafe { get; set; }
        internal DeviceTrustEligibilityFacts? LastFacts { get; private set; }
        private static EffectivePolicyInterval Interval => new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), false);
        public DeviceEvidencePolicy RequireCurrent(string origin, DateTimeOffset at)
        {
            if (!Available) throw new InvalidOperationException("Synthetic device qualification unavailable.");
            return new(new("synthetic-device-v1", "ACTIVE", Origin, Interval, null, ["SYNTHETIC-ONLY"]), Origin,
                "synthetic-device-collection", "synthetic-device-inventory", "synthetic-device-processing", "synthetic-device-retention",
                "RESTRICTED_SECURITY", "synthetic-device-permission", "synthetic-device-eligibility", "synthetic-device-proof",
                "synthetic-preserve-sessions", TimeSpan.FromDays(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), 4, 4,
                new Dictionary<string, IReadOnlyList<string>> { ["BrowserFamily"] = ["SyntheticBrowser"] });
        }
        public DeviceTrustEligibilityDecision RequireCurrent(DeviceTrustEligibilityFacts facts, DateTimeOffset at)
        {
            LastFacts = facts;
            return new(facts.Digest, AllowTrust && (facts.PriorTrustState != "REVOKED" || AllowRegrant),
                "synthetic-device-eligibility", "synthetic-exact-device-eligibility", Interval);
        }
        public IReadOnlyDictionary<string, string> Observe(HttpContext context, DeviceEvidencePolicy policy) =>
            Unsafe ? new Dictionary<string, string> { ["RawFingerprint"] = "not-qualified" } : new Dictionary<string, string> { ["BrowserFamily"] = "SyntheticBrowser" };
    }
    private sealed class OwnerScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        internal IdentityPersistenceDbContext Database { get; }
        internal DeviceApplication Devices { get; }
        internal StepUpApplication StepUp { get; }
        internal DeviceOperationApplication Owner { get; }
        internal IAssurancePolicySource Assurance { get; }
        internal OwnerScope(AuthenticationTestHost host, Qualification qualification)
        {
            scope = host.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            Database = services.GetRequiredService<IdentityPersistenceDbContext>();
            var authentication = services.GetRequiredService<AuthenticationApplication>();
            Devices = new(Database, authentication, host.Policies, qualification, qualification, host.Clock);
            Assurance = new AssurancePolicyCatalog([new(new("synthetic-device-proof", "ACTIVE", DeviceApplication.RevokeScope,
                new(host.Clock.GetUtcNow().AddHours(-1), true, host.Clock.GetUtcNow().AddHours(1), false), null, ["SYNTHETIC-ONLY"]),
                "synthetic-device-assurance", TimeSpan.FromMinutes(1), new Dictionary<string, string> { ["pwd"] = "synthetic-device-assurance" })]);
            StepUp = new(Database, authentication, host.Policies, Assurance, services.GetRequiredService<AuthenticationPasswordWork>(),
                services.GetRequiredService<IDataProtectionProvider>(), host.Clock);
            Owner = new(Devices, StepUp);
        }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Qualification qualification) => Start(db, services =>
    {
        services.AddSingleton<IDeviceEvidencePolicySource>(qualification); services.AddSingleton<IDeviceTrustEligibilitySource>(qualification);
        services.AddSingleton<IDeviceSafeCharacteristicSource>(qualification); DeviceBrowserService.AddQualifiedEvidence(services);
    });
    private static async Task<(StepUpCaller Actor, string Cookies, Guid Session)> Login(AuthenticationTestHost host, IdentityTestDatabase db,
        string? existing = null, string email = "person@example.test")
    {
        var options = await host.Options(existing); using var response = await host.Post("/authentication/sign-in", Command(options.Key, email: email), options.Cookies, options.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var cookies = Merge(options.Cookies, response);
        await using var context = db.Context(); var row = await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.IdempotencyKey == options.Key);
        await using var scope = host.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.Session.Ticket.v1");
        var ticket = TicketSerializer.Default.Deserialize(Convert.FromBase64String(protector.Unprotect(row.ProtectedTicket)))!;
        return (StepUpCaller.FromPrincipal(ticket.Principal, Origin, Network), cookies, row.SessionId);
    }
    private static async Task<DeviceTrustRemovalRequest> Request(IdentityTestDatabase db, StepUpCaller actor, Guid? target = null)
    {
        await using var context = db.Context(); var id = target ?? await context.Set<DeviceSessionEvidenceRow>().Where(x => x.SessionId == actor.SessionId).Select(x => x.DeviceId).SingleAsync();
        var record = await context.Set<DeviceRecordRow>().AsNoTracking().SingleAsync(x => x.DeviceId == id);
        var trust = await context.Set<DeviceTrustRow>().Where(x => x.DeviceId == id).OrderByDescending(x => x.Version).FirstAsync();
        var account = await context.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == actor.AccountId);
        return new(Guid.NewGuid(), id, record.Version, trust.Version, account.Version, "synthetic-" + Guid.NewGuid().ToString("N"));
    }
    private static async Task<string> Proof(OwnerScope scope, StepUpCaller actor, DeviceTrustRemovalRequest request)
    {
        var context = await scope.Owner.PrepareAsync(actor, request, scope.Assurance); Assert.NotNull(context);
        var issued = await scope.StepUp.IssuePasswordAsync(actor, context.Binding, context.ExpectedPolicyVersion, Password);
        Assert.Null(issued.ErrorCode); return Assert.IsType<StepUpProofGrant>(issued.Grant).Key;
    }

    [Fact]
    public async Task RealAuthenticationCollectsQualifiedEvidenceAndProofRemovalPreservesSessionAndHistory()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var signed = await Login(host, db); Assert.Contains(DeviceBrowserService.CookieName + "=", signed.Cookies, StringComparison.Ordinal);
        await using var scope = new OwnerScope(host, qualification);
        var page = await scope.Devices.ListAsync(signed.Actor, 1); var device = Assert.Single(Assert.IsType<DeviceListingPage>(page).Items);
        Assert.True(device.ObservedForCurrentSession); Assert.Equal("TRUSTED", device.EffectiveTrustState);
        var request = await Request(db, signed.Actor); var key = await Proof(scope, signed.Actor, request);
        var result = await scope.Owner.RevokeAsync(signed.Actor, request, key); Assert.True(result.Applied);
        Assert.Equal(result.Receipt, (await scope.Owner.RevokeAsync(signed.Actor, request, key)).Receipt);
        Assert.Equal(result.Receipt, (await scope.Owner.ReadResultAsync(signed.Actor, request)).Receipt);
        using var stillSignedIn = await host.Get("/authentication/session", signed.Cookies); Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
        var after = await scope.Devices.ListAsync(signed.Actor, 1); Assert.Equal("REVOKED", Assert.Single(after!.Items).EffectiveTrustState);
        await using var context = db.Context();
        Assert.Equal(1, await context.Set<DeviceRecordRow>().CountAsync()); Assert.Equal(2, await context.Set<DeviceTrustRow>().CountAsync());
        Assert.Equal(1, await context.Set<DeviceSessionEvidenceRow>().CountAsync());
        Assert.Equal("ACTIVE", (await context.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == signed.Session)).State);
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofConsumed"));
        Assert.Equal(0, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionRevoked"));
        foreach (var item in await context.Set<OutboxMessage>().Where(x => x.EventType.StartsWith("Device")).ToArrayAsync())
            Assert.True(!item.Payload.Contains(key, StringComparison.Ordinal) && !item.Payload.Contains("SyntheticBrowser", StringComparison.Ordinal) &&
                !item.Payload.Contains("person@example.test", StringComparison.Ordinal) && !item.Payload.Contains("BrowserDigest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndependentInstallationCorrelationSurvivesNewAuthenticationButNeverRegrantsRevokedTrustByItself()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var first = await Login(host, db); await using (var scope = new OwnerScope(host, qualification))
        {
            var request = await Request(db, first.Actor); var key = await Proof(scope, first.Actor, request);
            Assert.True((await scope.Owner.RevokeAsync(first.Actor, request, key)).Applied);
        }
        var second = await Login(host, db, first.Cookies);
        await using var context = db.Context(); var observations = await context.Set<DeviceSessionEvidenceRow>().OrderBy(x => x.TrustVersion).ToArrayAsync();
        Assert.Equal(2, observations.Length); Assert.Equal(observations[0].DeviceId, observations[1].DeviceId);
        Assert.Equal(3, observations[1].TrustVersion); Assert.Equal("REVOKED", qualification.LastFacts!.PriorTrustState);
        Assert.Equal("UNTRUSTED", (await context.Set<DeviceTrustRow>().OrderByDescending(x => x.Version).FirstAsync()).State);
        var record = await context.Set<DeviceRecordRow>().SingleAsync();
        Assert.DoesNotContain(record.CorrelationDigest, await context.Set<AuthenticationSessionRow>().Select(x => x.BrowserDigest).ToArrayAsync());
        Assert.NotEqual(first.Session, second.Session);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tampered")]
    [InlineData("foreign-account")]
    public async Task MissingTamperedAndOtherAccountCorrelationsCannotSupplyDeviceIdentityOrTrust(string kind)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var first = await Login(host, db); qualification.AllowTrust = false;
        string? cookies = kind == "missing" ? null : kind == "tampered" ? DeviceBrowserService.CookieName + "=tampered" : first.Cookies;
        if (kind == "foreign-account") await host.SeedAsync(db, foreignEmail: true);
        var second = await Login(host, db, cookies, kind == "foreign-account" ? "foreign@example.test" : "person@example.test");
        await using var context = db.Context(); var observations = await context.Set<DeviceSessionEvidenceRow>().ToArrayAsync();
        Assert.Equal(2, observations.Length); Assert.NotEqual(observations[0].DeviceId, observations[1].DeviceId);
        var current = observations.Single(x => x.SessionId == second.Session);
        Assert.Equal("UNTRUSTED", (await context.Set<DeviceTrustRow>().SingleAsync(x => x.DeviceId == current.DeviceId)).State);
        if (kind == "foreign-account")
        {
            var records = await context.Set<DeviceRecordRow>().ToArrayAsync(); Assert.NotEqual(records[0].CorrelationDigest, records[1].CorrelationDigest);
            await using var scope = new OwnerScope(host, qualification); Assert.Single((await scope.Devices.ListAsync(second.Actor, 4))!.Items);
        }
    }

    [Theory]
    [InlineData("device-version")]
    [InlineData("trust-version")]
    [InlineData("account-version")]
    [InlineData("foreign-target")]
    public async Task StaleAndForeignTargetsCannotBePrepared(string kind)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var signed = await Login(host, db); var request = await Request(db, signed.Actor);
        if (kind == "foreign-target")
        {
            await host.SeedAsync(db, foreignEmail: true); var foreign = await Login(host, db, email: "foreign@example.test");
            request = request with { DeviceId = (await Request(db, foreign.Actor)).DeviceId };
        }
        else request = kind switch { "device-version" => request with { ExpectedDeviceVersion = request.ExpectedDeviceVersion + 1 },
            "trust-version" => request with { ExpectedTrustVersion = request.ExpectedTrustVersion + 1 },
            _ => request with { ExpectedAccountVersion = request.ExpectedAccountVersion + 1 } };
        await using var scope = new OwnerScope(host, qualification); Assert.Null(await scope.Owner.PrepareAsync(signed.Actor, request, scope.Assurance));
        Assert.Equal(0, await scope.Database.Set<DeviceOperationRow>().CountAsync());
    }

    [Fact]
    public async Task BoundedListingBindsContinuationToActorAndRequiresExplicitPermission()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        _ = await Login(host, db); _ = await Login(host, db); var current = await Login(host, db);
        await using var scope = new OwnerScope(host, qualification); var first = await scope.Devices.ListAsync(current.Actor, 1);
        Assert.True(first!.HasMore); Assert.NotNull(first.Next); var next = await scope.Devices.ListAsync(current.Actor, 1, first.Next);
        Assert.NotEqual(first.Items[0].DeviceReference, next!.Items[0].DeviceReference);
        await Assert.ThrowsAsync<ArgumentException>(() => scope.Devices.ListAsync(current.Actor, 1, first.Next with { ActorSessionId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => scope.Devices.ListAsync(current.Actor, 5));
        host.Access.DeniedOperation = DeviceApplication.ListOperation; Assert.Null(await scope.Devices.ListAsync(current.Actor, 1));
    }

    [Fact]
    public async Task ExactOriginalAndProofCannotBeReboundAndCausalReceiptRequiresBothOutboxFacts()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var signed = await Login(host, db); await using var scope = new OwnerScope(host, qualification);
        var request = await Request(db, signed.Actor); var key = await Proof(scope, signed.Actor, request);
        Assert.Null(await scope.Owner.PrepareAsync(signed.Actor, request with { CorrelationId = "other" }, scope.Assurance));
        Assert.False((await scope.Owner.RevokeAsync(signed.Actor, request, new('F', 64))).Applied);
        Assert.True((await scope.Owner.RevokeAsync(signed.Actor, request, key)).Applied);
        Assert.False((await scope.Owner.RevokeAsync(signed.Actor, request, new('F', 64))).Applied);
        await scope.Database.Database.ExecuteSqlInterpolatedAsync($"UPDATE platform.outbox_messages SET event_type='SyntheticCorruptedProofFact' WHERE event_type='StepUpProofConsumed'");
        Assert.False((await scope.Owner.ReadResultAsync(signed.Actor, request)).Applied);
    }

    [Fact]
    public async Task QualificationChangingAfterOwnerMutationRollsBackTrustLedgerOutboxAndProofTogether()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var signed = await Login(host, db); await using var scope = new OwnerScope(host, qualification);
        var request = await Request(db, signed.Actor); var key = await Proof(scope, signed.Actor, request);
        host.Access.BeforeDecision = _ => { if (scope.Database.ChangeTracker.Entries<DeviceOperationRow>().Any(x => x.Entity.State == "COMPLETED")) qualification.Available = false; };
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Owner.RevokeAsync(signed.Actor, request, key));
        qualification.Available = true; host.Access.BeforeDecision = null;
        await using var context = db.Context(); Assert.Equal(1, await context.Set<DeviceTrustRow>().CountAsync());
        Assert.Equal("PENDING", (await context.Set<DeviceOperationRow>().SingleAsync()).State);
        Assert.Equal("ACTIVE", (await context.Set<StepUpProofRow>().SingleAsync()).State);
        Assert.Equal(0, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked" || x.EventType == "StepUpProofConsumed"));
    }

    [Fact]
    public async Task UnqualifiedRawCharacteristicsFailBeforeDevicePersistence()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification { Unsafe = true }; await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var options = await host.Options(); using var response = await host.Post("/authentication/sign-in", Command(options.Key), options.Cookies, options.Csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var context = db.Context(); Assert.Equal(0, await context.Set<DeviceRecordRow>().CountAsync());
        Assert.Equal(0, await context.Set<DeviceTrustRow>().CountAsync());
    }

    [Fact]
    public async Task DatabaseRejectsHistoryRewriteAndVersionGaps()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        _ = await Login(host, db); await using var context = db.Context();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE identity.device_trust_versions SET state='UNTRUSTED'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE identity.device_session_evidence SET characteristics_json={0}", "{}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE identity.device_records SET version=version+2"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO identity.device_trust_versions(device_id,version,account_id,origin,device_version,state,source_session_id,
              removal_operation_id,eligibility_facts_json,eligibility_decision_json,policy_version,policy_snapshot,recorded_at,operation_id,correlation_id,outbox_event_id)
            SELECT device_id,version+2,account_id,origin,device_version,state,source_session_id,removal_operation_id,eligibility_facts_json,
              eligibility_decision_json,policy_version,policy_snapshot,recorded_at,operation_id,correlation_id,outbox_event_id FROM identity.device_trust_versions
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task PersistedOriginalBindingUsesPostgresPrecision()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var signed = await Login(host, db); host.Clock.Advance(TimeSpan.FromTicks(11));
        var request = await Request(db, signed.Actor); string key;
        await using (var first = new OwnerScope(host, qualification)) key = await Proof(first, signed.Actor, request);
        await using var second = new OwnerScope(host, qualification); Assert.True((await second.Owner.RevokeAsync(signed.Actor, request, key)).Applied);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("invalid-stamp")]
    public async Task ObservationPreservesOrdinaryValidatorTerminalizationWithoutCreatingDeviceEvidence(string kind)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification();
        await using var host = await Start(db); await host.SeedAsync(db); var signed = await Login(host, db);
        AuthenticationGrant grant;
        await using (var services = host.Services.CreateAsyncScope())
        {
            await using var context = db.Context(); var session = await context.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == signed.Session);
            var protection = services.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
            var ticket = TicketSerializer.Default.Deserialize(Convert.FromBase64String(protection.CreateProtector("Tunner.User.Session.Ticket.v1").Unprotect(session.ProtectedTicket)))!;
            grant = new(protection.CreateProtector("Tunner.User.Session.RetryKey.v1").Unprotect(session.ProtectedKey), ticket);
            if (kind == "expired") host.Clock.Advance(host.Policies.Policy.AbsoluteLifetime + TimeSpan.FromSeconds(1));
            else { var user = await context.Users.SingleAsync(); user.SecurityStamp = Guid.NewGuid().ToString("N"); await context.SaveChangesAsync(); }
        }
        await using (var scope = new OwnerScope(host, qualification))
            Assert.False(await scope.Devices.ObserveAuthenticatedAsync(signed.Actor, grant, new('A', 64),
                new Dictionary<string, string> { ["BrowserFamily"] = "SyntheticBrowser" }, CancellationToken.None));
        await using var persisted = db.Context(); var ended = await persisted.Set<AuthenticationSessionRow>().SingleAsync();
        Assert.Equal(kind == "expired" ? "EXPIRED" : "REVOKED", ended.State);
        Assert.True(ended.ProtectedKey.Length == 0 && ended.ProtectedTicket.Length == 0);
        Assert.Equal(1, await persisted.Set<OutboxMessage>().CountAsync(x => x.EventType == (kind == "expired" ? "TunnerSessionExpired" : "TunnerSessionRevoked")));
        Assert.Equal(0, await persisted.Set<DeviceRecordRow>().CountAsync()); Assert.Equal(0, await persisted.Set<DeviceTrustRow>().CountAsync());
    }

    [Fact]
    public async Task ConcurrentExactRemovalReconcilesOneTrustVersionAndOneProofConsumption()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var signed = await Login(host, db); var request = await Request(db, signed.Actor); string key;
        await using (var prepare = new OwnerScope(host, qualification)) key = await Proof(prepare, signed.Actor, request);
        async Task<DeviceTrustRemovalResult> Apply()
        { await using var scope = new OwnerScope(host, qualification); return await scope.Owner.RevokeAsync(signed.Actor, request, key); }
        var results = await Task.WhenAll(Apply(), Apply()); Assert.All(results, x => Assert.True(x.Applied));
        Assert.Equal(results[0].Receipt, results[1].Receipt);
        await using var context = db.Context(); Assert.Equal(2, await context.Set<DeviceTrustRow>().CountAsync());
        Assert.Equal(1, await context.Set<DeviceOperationRow>().CountAsync());
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(x => x.EventType == "StepUpProofConsumed"));
    }

    [Fact]
    public async Task RevokingSessionPreservesIndependentDeviceAndTrustHistory()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var target = await Login(host, db); var actor = await Login(host, db);
        await using var services = host.Services.CreateAsyncScope(); var provider = services.ServiceProvider;
        var context = provider.GetRequiredService<IdentityPersistenceDbContext>(); var auth = provider.GetRequiredService<AuthenticationApplication>();
        var targetRow = await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == target.Session);
        var request = new SessionRevocationRequest(Guid.NewGuid(), target.Session, targetRow.Version, "synthetic-session-revoke");
        var assurance = new AssurancePolicyCatalog([new(new("synthetic-session-proof", "ACTIVE", SessionRevocationBinding.Scope,
            new(host.Clock.GetUtcNow().AddHours(-1), true, host.Clock.GetUtcNow().AddHours(1), false), null, ["SYNTHETIC-ONLY"]),
            "synthetic-assurance", TimeSpan.FromMinutes(1), new Dictionary<string, string> { ["pwd"] = "synthetic-assurance" })]);
        var stepUp = new StepUpApplication(context, auth, host.Policies, assurance, provider.GetRequiredService<AuthenticationPasswordWork>(),
            provider.GetRequiredService<IDataProtectionProvider>(), host.Clock);
        var owner = new SessionRevocationApplication(auth, stepUp); Assert.NotNull(await owner.PrepareAsync(actor.Actor, request));
        var proof = await stepUp.IssuePasswordAsync(actor.Actor, SessionRevocationBinding.Operation(actor.Actor, request), "synthetic-session-proof", Password);
        Assert.True((await owner.RevokeAsync(actor.Actor, request, proof.Grant!.Key)).Applied);
        Assert.Equal(2, await context.Set<DeviceRecordRow>().CountAsync()); Assert.Equal(2, await context.Set<DeviceSessionEvidenceRow>().CountAsync());
        Assert.Equal(2, await context.Set<DeviceTrustRow>().CountAsync()); Assert.Equal(0, await context.Set<DeviceTrustRow>().CountAsync(x => x.State == "REVOKED"));
    }

    [Fact]
    public async Task AdditiveMigrationPreservesExistingSessionWithoutInventingDeviceEvidenceAndSupportsDownUp()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await Login(host, db); await using var context = db.Context(); var migrator = context.GetService<IMigrator>();
        var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var deviceIndex = Array.FindIndex(migrations, x => x.EndsWith("_P2DeviceEvidenceAndTrust", StringComparison.Ordinal));
        Assert.True(deviceIndex > 0); var predecessor = migrations[deviceIndex - 1];
        await migrator.MigrateAsync(predecessor); Assert.Equal("ACTIVE", (await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == actor.Session)).State);
        await migrator.MigrateAsync(); Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(0, await context.Set<DeviceRecordRow>().CountAsync()); Assert.Equal(0, await context.Set<DeviceTrustRow>().CountAsync());
        using var response = await host.Get("/authentication/session", actor.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PopulatedDeviceDowngradeIsRejectedBeforeHistoryOrGuardsCanBeRemoved()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var qualification = new Qualification(); await using var host = await Host(db, qualification); await host.SeedAsync(db);
        var actor = await Login(host, db); var request = await Request(db, actor.Actor);
        await using (var scope = new OwnerScope(host, qualification))
        { var key = await Proof(scope, actor.Actor, request); Assert.True((await scope.Owner.RevokeAsync(actor.Actor, request, key)).Applied); }
        await using var context = db.Context(); var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(migrations, x => x.EndsWith("_P2DeviceEvidenceAndTrust", StringComparison.Ordinal)); Assert.True(index > 0);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("governed forward migration", exception.MessageText, StringComparison.Ordinal);
        Assert.Contains(migrations[index], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await context.Set<DeviceRecordRow>().CountAsync()); Assert.Equal(2, await context.Set<DeviceTrustRow>().CountAsync());
        Assert.Equal(1, await context.Set<DeviceSessionEvidenceRow>().CountAsync()); Assert.Equal(1, await context.Set<DeviceOperationRow>().CountAsync());
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE identity.device_trust_versions SET state='UNTRUSTED'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE identity.device_records SET version=version+2"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE identity.device_session_evidence SET characteristics_json={0}", "{}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE identity.device_operations SET correlation_id='rewritten'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM identity.device_session_evidence"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("history cannot be rewritten", exception.MessageText, StringComparison.Ordinal);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM identity.device_trust_versions WHERE state='REVOKED'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("history cannot be rewritten", exception.MessageText, StringComparison.Ordinal);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("TRUNCATE identity.device_session_evidence"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(2, await context.Set<DeviceTrustRow>().CountAsync()); Assert.Equal(1, await context.Set<DeviceSessionEvidenceRow>().CountAsync());
    }
}
