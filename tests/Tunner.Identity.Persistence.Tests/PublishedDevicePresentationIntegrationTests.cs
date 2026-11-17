using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

// Reuse the existing private Device Scenario/Create/Login/Prepare/ProofOptions/NoRemoval fixtures and real HTTPS host.
public sealed partial class DeviceHttpTests
{
    [Fact]
    public async Task PublishedDeviceContentCoversListingTrustRemovalAndExactPrivateResultReplay()
    {
        var configured = await PublishedDeviceScenario(); await using var test = configured.Scenario;
        var publications = configured.Publications;
        using (var options = await test.Host.Get(Root + "/options", test.Actor.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); Headers(options);
            var body = await options.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var entry in publications)
            {
                var actual = body.GetProperty("content").GetProperty(entry.Key);
                Assert.Equal(entry.Value.PublicationReference.ToString("N"), actual.GetProperty("publicationRef").GetString());
                Assert.Equal(entry.Value.VersionReference.ToString("N"), actual.GetProperty("versionRef").GetString());
                Assert.Equal("PUBLISHED", actual.GetProperty("state").GetString());
            }
        }
        var device = await Device(test);
        using (var details = await test.Host.Post(Root + "/details", new { reference = device.GetProperty("reference").GetGuid() }, test.Actor.Cookies, test.Actor.Csrf))
            Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var original = await Prepare(test, device: device); var proof = await ProofOptions(test, original);
        using (var response = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("COMPLETED", body.GetProperty("status").GetString()); Assert.False(body.GetProperty("signInRequired").GetBoolean());
        }
        for (var i = 0; i < 2; i++)
        {
            using var response = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, proof.Cookies, proof.Csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Headers(response);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("REVOKED", result.GetProperty("status").GetString()); Assert.Equal(original.OperationReference, result.GetProperty("operationReference").GetString());
            Assert.Equal(device.GetProperty("reference").GetGuid(), result.GetProperty("target").GetProperty("reference").GetGuid());
        }
        using var duplicate = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var session = await test.Host.Get("/authentication/session", proof.Cookies); Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        await using var read = test.Database.Context();
        Assert.Equal(1, await read.Set<DeviceTrustRow>().CountAsync(x => x.State == "REVOKED"));
        Assert.Equal(1, await read.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED"));
        Assert.Equal(1, await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.False(await read.Set<OutboxMessage>().AnyAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Theory]
    [InlineData("passkey")]
    [InlineData("totp")]
    public async Task PublishedDeviceNativeProofCompletesExistingTrustRemovalWithoutChangingSessionEffect(string method)
    {
        var totp = new TotpTests.Profile(); using var key = new PasskeyAuthenticator();
        Task<AuthenticationTestHost> Starter(IdentityTestDatabase db, Action<IServiceCollection> setup) => method == "passkey"
            ? PasskeyTests.StartRecoveryFixtureAsync(db, setup) : TotpTests.StartTotp(db, totp, setup);
        var configured = await PublishedDeviceScenario(start: Starter, configure: (_, profile) =>
        {
            if (method == "passkey")
            {
                profile.Methods.Add(PasskeyPolicy.MethodKey(false, false), profile.RequiredAssurance);
                profile.ScopeVersions.Add(PasskeyApplication.EnrollmentScope, "fixture-enrollment-v1");
                profile.ScopeVersions.Add(PasskeyApplication.RevocationScope, "fixture-revocation-v1");
            }
            else
            {
                profile.RequiredAssurance = totp.Policy.OperationProofAssurance; profile.Methods["pwd"] = profile.RequiredAssurance;
                profile.Methods.Add("totp", profile.RequiredAssurance); profile.NativePresentation = totp;
                foreach (var scope in new[] { TotpApplication.EnrollmentScope, TotpApplication.DisableScope, TotpApplication.ResetScope })
                    profile.ScopeVersions.Add(scope, totp.AssuranceVersion);
            }
        });
        await using var test = configured.Scenario;
        var source = method == "passkey" ? (await PasskeyTests.SeedRecoveryCredentialAsync(test.Host, key)).Reference :
            (await TotpTests.Enroll(test.Host, totp, await TotpTests.Login(test.Host))).Reference;
        var original = await Prepare(test); var proof = await ProofOptions(test, original); var attempt = Guid.NewGuid();
        HttpResponseMessage response;
        if (method == "passkey")
        {
            using var options = await test.Host.Post("/authentication/step-up/passkey/options", new { requestId = proof.RequestId, attemptId = attempt }, proof.Cookies, proof.Csrf);
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var native = await options.Content.ReadFromJsonAsync<JsonElement>();
            response = await test.Host.Post("/authentication/step-up/passkey", new { requestId = proof.RequestId, attemptId = attempt,
                ceremonyId = native.GetProperty("ceremonyId").GetGuid(), credential = key.Assert(native.GetProperty("publicKey"), Origin) }, Merge(proof.Cookies, options), proof.Csrf);
        }
        else response = await test.Host.Post("/authentication/step-up/totp", new { requestId = proof.RequestId, attemptId = attempt,
            authenticatorReference = source, code = TotpTests.NativeCode(totp.Seed(source), 1) }, proof.Cookies, proof.Csrf);
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("COMPLETED", body.GetProperty("status").GetString()); Assert.False(body.GetProperty("signInRequired").GetBoolean());
        }
        using var result = await test.Host.Post(Root + "/result", new { requestId = original.RequestId }, proof.Cookies, proof.Csrf);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await using var read = test.Database.Context(); Assert.Equal(1, await read.Set<DeviceTrustRow>().CountAsync(x => x.State == "REVOKED"));
        Assert.False(await read.Set<OutboxMessage>().AnyAsync(x => x.EventType == "TunnerSessionRevoked"));
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("binding")]
    [InlineData("profile")]
    [InlineData("expiry")]
    public async Task PublishedDeviceRejectsChangedOriginalPublicationBeforeAnyTrustRemoval(string change)
    {
        var configured = await PublishedDeviceScenario(); await using var test = configured.Scenario;
        var original = await Prepare(test); var proof = await ProofOptions(test, original);
        if (change == "replacement")
        {
            test.Host.Clock.Advance(TimeSpan.FromSeconds(1)); const string key = "account.devices.title";
            _ = await configured.Content.PublishTextAsync(key, "Synthetic changed Device title", supersedes: configured.Publications[key].PublicationReference);
        }
        else if (change == "binding") configured.Authority.ConfigurationVersion = "fixture-device-binding-changed";
        else if (change == "profile") configured.Authority.Available = false;
        else configured.Authority.Until = test.Host.Clock.GetUtcNow();
        using var response = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode); await NoRemoval(test, 1);
        await using var read = test.Database.Context(); Assert.False(await read.Set<StepUpProofRow>().AnyAsync(x => x.State == "CONSUMED"));
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("publication-expiry")]
    [InlineData("profile")]
    [InlineData("binding")]
    public async Task PublishedDeviceFinalLocalWitnessRollsBackSavedRemovalWhenQualificationChanges(string change)
    {
        var observer = new DevicePublicationSqlGuard(); var configured = await PublishedDeviceScenario(observer,
            publicationLifetime: change == "publication-expiry" ? TimeSpan.FromSeconds(1) : null);
        await using var test = configured.Scenario;
        if (change == "expiry") configured.Authority.Until = test.Host.Clock.GetUtcNow().AddSeconds(1);
        var original = await Prepare(test); var proof = await ProofOptions(test, original);
        observer.AfterDeviceSave = () =>
        {
            if (change is "expiry" or "publication-expiry") test.Host.Clock.Advance(TimeSpan.FromSeconds(1));
            else if (change == "profile") configured.Authority.Available = false;
            else configured.Authority.ConfigurationVersion = "fixture-device-after-save";
        };
        using var response = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf);
        Assert.True(observer.Fired); Assert.NotEqual(HttpStatusCode.OK, response.StatusCode); await NoRemoval(test, 1);
        await using var read = test.Database.Context();
        Assert.True(await read.Set<DeviceOperationRow>().AllAsync(x => x.State == "PENDING" && x.Version == 1));
        Assert.False(await read.Set<StepUpProofRow>().AnyAsync(x => x.State == "CONSUMED"));
    }

    [Theory]
    [InlineData("proof")]
    [InlineData("session-use")]
    [InlineData("operation-access")]
    public async Task PublishedDeviceSavedChangesCannotOutliveQualifiedProofOrAccessWithLivePublicationAndOriginal(string boundary)
    {
        var observer = new DevicePublicationSqlGuard();
        var configured = await PublishedDeviceScenario(observer, configure: (_, profile) =>
        { if (boundary == "proof") profile.ProofLifetime = TimeSpan.FromSeconds(1); });
        await using var test = configured.Scenario; var original = await Prepare(test); var proof = await ProofOptions(test, original);
        if (boundary != "proof")
            test.Host.Access.BeforeDecision = request => test.Host.Access.DecisionLifetime =
                request.Operation == (boundary == "session-use" ? "SESSION_USE" : DeviceApplication.RevokeScope.Operation)
                    ? TimeSpan.FromSeconds(1) : TimeSpan.FromHours(1);
        observer.AfterDeviceSave = () => test.Host.Clock.Advance(TimeSpan.FromSeconds(2));
        using var response = await test.Host.Post("/authentication/step-up/password", new { requestId = proof.RequestId, password = Password }, proof.Cookies, proof.Csrf);
        Assert.True(observer.Fired); Assert.NotEqual(HttpStatusCode.OK, response.StatusCode); await NoRemoval(test, 1);
        var at = test.Host.Clock.GetUtcNow(); Assert.True(original.View.GetProperty("expiresAt").GetDateTimeOffset() > at);
        Assert.True(configured.Authority.RequireCurrentLocal(test.Host.Policies.Policy, configured.Authority.DeviceVersion, at).EffectiveInterval.Contains(at));
        Assert.True(test.Qualification.RequireCurrent(Origin, at).Metadata.EffectiveInterval.Contains(at));
        await using var read = test.Database.Context();
        var row = await read.Set<StepUpProofRow>().SingleAsync(x => x.Operation == DeviceApplication.RevokeScope.Operation);
        Assert.Equal("ACTIVE", row.State); if (boundary != "proof") Assert.True(row.ExpiresAt > at);
        Assert.True(await read.Set<DeviceOperationRow>().AllAsync(x => x.State == "PENDING" && x.Version == 1));
    }

    [Fact]
    public async Task PublishedDeviceActualNativeOwnerResolutionTakesContentBeforeDeviceAndReusesOnlyExactTransactionWitness()
    {
        var observer = new DevicePublicationSqlGuard(); var configured = await PublishedDeviceScenario(observer); await using var test = configured.Scenario;
        var original = await Prepare(test); var proof = await ProofOptions(test, original);
        await using var scope = test.Host.Services.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
        var account = await database.Set<AuthenticationSessionRow>().Where(x => x.SessionId == test.Actor.SessionId).Select(x => x.AccountId).SingleAsync();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, account),
                new Claim(AuthenticationIdentity.SessionClaim, test.Actor.SessionId.ToString("D")), new Claim(AuthenticationIdentity.OriginClaim, Origin) }, AuthenticationIdentity.Scheme)) };
        http.Connection.RemoteIpAddress = IPAddress.Loopback; http.Request.Method = "POST"; http.Request.Path = "/authentication/step-up/password";
        http.Request.Headers.Cookie = proof.Cookies;
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext; accessor.HttpContext = http;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var blocker = test.Database.Context(); await using var writer = await blocker.Database.BeginTransactionAsync(deadline.Token);
        var content = configured.Content; var contentKey = "presentation-content:publication:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { content.Scope, Key = DevicePresentation.RequiredKeys[0], VariantReference = content.Variant }))));
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({contentKey},0))", deadline.Token);
        Task<UserStepUpOperationContext?>? resolving = null; var released = false;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(deadline.Token);
            var caller = StepUpCaller.FromPrincipal(http.User, Origin, scope.ServiceProvider.GetRequiredService<AuthenticationPartitionKey>().Network(IPAddress.Loopback));
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().LockForOperationAsync(caller, DeviceApplication.RevokeScope.Operation, deadline.Token));
            var owner = scope.ServiceProvider.GetRequiredService<IUserStepUpOperationSource>();
            resolving = owner.ResolveAsync(new(caller), deadline.Token);
            await using var probe = test.Database.Context(); var pid = ((Npgsql.NpgsqlConnection)database.Database.GetDbConnection()).ProcessID;
            while (!await probe.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock' AND query LIKE '%pg_advisory_xact_lock_shared%') AS \"Value\"").SingleAsync(deadline.Token))
                await Task.Delay(10, deadline.Token);
            var deviceLock = "device:" + original.Target.GetProperty("reference").GetGuid().ToString("N");
            Assert.True(await probe.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({deviceLock},0)) AS \"Value\"").SingleAsync(deadline.Token));
            await writer.RollbackAsync(deadline.Token); released = true;
            var selected = await resolving; Assert.NotNull(selected); Assert.Equal(original.OperationReference, selected.Binding.CorrelationId);
            Assert.Same(transaction, database.Database.CurrentTransaction);
            Assert.False(await probe.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({deviceLock},0)) AS \"Value\"").SingleAsync(deadline.Token));
            // Preparation after Device is held must reuse the pinned read, never re-read or lock new Content keys.
            var source = Assert.IsType<PublishedDevicePresentationSource>(scope.ServiceProvider.GetRequiredService<IDevicePresentationSource>());
            var prepared = await source.PreparePublishedAsync(test.Host.Policies.Policy, configured.Authority.DeviceVersion, deadline.Token);
            var profileReads = configured.Authority.OrdinaryProfileReads; var sqlReads = observer.SqlReads; observer.RejectSql = true;
            try
            {
                Assert.Same(prepared, await source.PreparePublishedAsync(test.Host.Policies.Policy, configured.Authority.DeviceVersion, deadline.Token));
                configured.Authority.ContentScopeOverride = "fixture-changed-device-content";
                await Assert.ThrowsAsync<InvalidOperationException>(() => source.PreparePublishedAsync(test.Host.Policies.Policy, configured.Authority.DeviceVersion, deadline.Token).AsTask());
                Assert.Equal(profileReads, configured.Authority.OrdinaryProfileReads); Assert.Equal(sqlReads, observer.SqlReads);
            }
            finally { observer.RejectSql = false; }
            await transaction.RollbackAsync(deadline.Token);
        }
        finally
        {
            deadline.Cancel(); if (!released) await writer.RollbackAsync(CancellationToken.None);
            if (resolving is not null) try { await resolving; } catch (Exception error) when (error is OperationCanceledException or InvalidOperationException) { }
            accessor.HttpContext = previous;
        }
        await NoRemoval(test, 1);
    }

    [Fact]
    public async Task PublishedDeviceBrowserSnapshotCannotBecomeNativeAndOwnerWitnessPerformsNoResolverOrDatabaseIo()
    {
        var observer = new DevicePublicationSqlGuard(); var configured = await PublishedDeviceScenario(observer);
        await using var test = configured.Scenario; await using var scope = test.Host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
        var source = Assert.IsType<PublishedDevicePresentationSource>(scope.ServiceProvider.GetRequiredService<IDevicePresentationSource>());
        var auth = test.Host.Policies.Policy; var version = configured.Authority.DeviceVersion;
        var browser = await source.PreparePublishedAsync(auth, version, CancellationToken.None);
        Assert.Null(database.Database.CurrentTransaction); Assert.True(browser.CurrentAt(database, auth, version, test.Host.Clock.GetUtcNow()));
        DevicePreparedPresentation native;
        await using (var tx = await database.Database.BeginTransactionAsync())
        {
            Assert.False(browser.CurrentAt(database, auth, version, test.Host.Clock.GetUtcNow()));
            native = await source.PreparePublishedAsync(auth, version, CancellationToken.None); Assert.Same(tx, database.Database.CurrentTransaction);
            await using var foreign = test.Database.Context();
            Assert.False(native.CurrentAt(foreign, auth, version, test.Host.Clock.GetUtcNow()));
            var ordinaryReads = configured.Authority.OrdinaryProfileReads; var sql = observer.SqlReads; observer.RejectSql = true;
            try
            {
                Assert.True(native.CurrentAt(database, auth, version, test.Host.Clock.GetUtcNow()));
                Assert.Equal(ordinaryReads, configured.Authority.OrdinaryProfileReads); Assert.Equal(sql, observer.SqlReads);
                Assert.Throws<InvalidOperationException>(() => source.RequirePublished(auth, version));
            }
            finally { observer.RejectSql = false; }
            await tx.RollbackAsync();
        }
        Assert.False(native.CurrentAt(database, auth, version, test.Host.Clock.GetUtcNow()));
        Assert.True(browser.CurrentAt(database, auth, version, test.Host.Clock.GetUtcNow()));
        await NoRemoval(test, 0);
    }

    [Fact]
    public async Task PublishedDeviceBorrowedReadLocksContentBeforeDeviceAndCancellationPreservesOwnerTransaction()
    {
        var configured = await PublishedDeviceScenario(); await using var test = configured.Scenario;
        var target = await Device(test);
        await using var blocker = test.Database.Context(); await using var writer = await blocker.Database.BeginTransactionAsync();
        var content = configured.Content; var key = "presentation-content:publication:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { content.Scope, Key = DevicePresentation.RequiredKeys[0], VariantReference = content.Variant }))));
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))");
        await using var scope = test.Host.Services.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
        await using var tx = await database.Database.BeginTransactionAsync(); using var cancellation = new CancellationTokenSource();
        var source = Assert.IsType<PublishedDevicePresentationSource>(scope.ServiceProvider.GetRequiredService<IDevicePresentationSource>());
        var read = source.PreparePublishedAsync(test.Host.Policies.Policy, configured.Authority.DeviceVersion, cancellation.Token).AsTask();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var probe = test.Database.Context();
            while (!await probe.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%pg_advisory_xact_lock_shared%') AS \"Value\"").SingleAsync(deadline.Token))
                await Task.Delay(10, deadline.Token);
            Assert.False(read.IsCompleted);
            // During Content lock acquisition, no later Device lock may have been acquired.
            var deviceLock = "device:" + target.GetProperty("reference").GetGuid().ToString("N");
            await using var deviceProbeTransaction = await probe.Database.BeginTransactionAsync(deadline.Token);
            Assert.True(await probe.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({deviceLock},0)) AS \"Value\"").SingleAsync(deadline.Token));
            await deviceProbeTransaction.RollbackAsync(deadline.Token);
            cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            Assert.Same(tx, database.Database.CurrentTransaction);
            // PostgreSQL cancellation aborts statements in this caller's transaction; the caller remains responsible for rollback.
            await tx.RollbackAsync();
        }
        finally
        {
            cancellation.Cancel();
            try { await read; } catch (Exception error) when (error is OperationCanceledException or InvalidOperationException) { }
            await writer.RollbackAsync();
        }
        await NoRemoval(test, 0);
    }

    [Fact]
    public void PublishedDeviceQualificationCarrierBindsOriginalWithoutChangingLegacyDigestOrSerializedPresentation()
    {
        var texts = DevicePresentation.RequiredKeys.Select(key => new RegistrationPublishedText(key, "fixture-device-variant",
            "fixture-device-version", "fixture-device-publication", "en", "PUBLISHED", "Synthetic Device copy: " + key)).ToArray();
        var ordinary = new DevicePresentation("en", texts);
        var qualified = new DevicePresentation("en", texts) { BindingQualificationDigest = AuthenticationIdentity.Digest("fixture-device-binding-a") };
        var changed = new DevicePresentation("en", texts) { BindingQualificationDigest = AuthenticationIdentity.Digest("fixture-device-binding-b") };
        var legacyDigest = AuthenticationIdentity.Digest(JsonSerializer.Serialize(new {
            ordinary.Locale, Content = ordinary.Content.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value).ToArray() }));
        Assert.Equal(legacyDigest, DeviceBrowserEndpoints.PublicationDigest(ordinary));
        Assert.NotEqual(legacyDigest, DeviceBrowserEndpoints.PublicationDigest(qualified));
        Assert.NotEqual(DeviceBrowserEndpoints.PublicationDigest(qualified), DeviceBrowserEndpoints.PublicationDigest(changed));
        Assert.Equal(JsonSerializer.Serialize(ordinary), JsonSerializer.Serialize(qualified));
        Assert.Equal(JsonSerializer.Serialize(ordinary), JsonSerializer.Serialize(changed));
    }

    [Fact]
    public void PublishedDeviceSelectionRequiresExplicitDependenciesAndDoesNotActivateDevices()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedDevicePresentation());
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IDevicePresentationSource) || item.ServiceType == typeof(DeviceManagementBrowserService));
    }

    private async Task<(Scenario Scenario, PresentationContentTestEnvironment Content, DevicePublicationAuthority Authority,
        Dictionary<string, ContentHistoryEntry> Publications)> PublishedDeviceScenario(DevicePublicationSqlGuard? observer = null,
        Func<IdentityTestDatabase, Action<IServiceCollection>, Task<AuthenticationTestHost>>? start = null, Action<IServiceCollection, Profile>? configure = null, TimeSpan? publicationLifetime = null)
    {
        IdentityTestDatabase? database = null; string? role = null; PresentationContentTestEnvironment? content = null; DevicePublicationAuthority? authority = null;
        var scenario = await Create(start: async (actual, setup) =>
        {
            database = actual; await using var reader = actual.Context();
            role = await reader.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync();
            return await (start ?? ((db, callback) => Start(db, callback)))(actual, setup);
        }, configure: (services, profile) =>
        {
            configure?.Invoke(services, profile);
            var clock = Assert.IsType<TestClock>(services.Last(x => x.ServiceType == typeof(TimeProvider)).ImplementationInstance);
            var policy = Assert.IsType<TestPolicies>(services.Last(x => x.ServiceType == typeof(IAuthenticationPolicySource)).ImplementationInstance).Policy;
            var devicePolicy = Assert.IsType<DeviceTrustTests.Qualification>(services.Last(x => x.ServiceType == typeof(IDeviceEvidencePolicySource)).ImplementationInstance).RequireCurrent(Origin, clock.GetUtcNow());
            content = new(database!, clock, DevicePresentation.RequiredKeys.ToDictionary(key => key, key => "Synthetic Device copy: " + key, StringComparer.Ordinal));
            authority = new(content, policy.Version, devicePolicy.Metadata.VersionId, new Npgsql.NpgsqlConnectionStringBuilder(database!.ConnectionString).Database!, role!);
            services.AddSingleton<IPresentationContentProfileSource>(authority); services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization);
            services.AddTunnerPresentationContent(database.ConnectionString); services.AddSingleton<IDeviceLocalContentBindingSource>(authority);
            services.Remove(services.Single(x => x.ServiceType == typeof(IDevicePresentationSource))); services.AddTunnerPublishedDevicePresentation();
            if (observer is not null) services.AddDbContext<IdentityPersistenceDbContext>(options => options.AddInterceptors(observer, new DevicePublicationSaveGuard(observer)));
        });
        try
        {
            Assert.NotNull(content); Assert.NotNull(authority); Assert.Same(scenario.Host.Clock, content.Clock);
            // PostgreSQL stores microseconds; align the same clock forward before publication seeding, never rewind it.
            var remainder = scenario.Host.Clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond;
            if (remainder != 0) scenario.Host.Clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond - remainder));
            Assert.Equal(0L, scenario.Host.Clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond);
            await content.InitializeAsync();
            var publications = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
            foreach (var definition in content.Profile.Definitions)
            {
                if (publicationLifetime is not { } lifetime || definition.Key != DevicePresentation.RequiredKeys[0])
                {
                    publications.Add(definition.Key, await content.PublishTextAsync(definition.Key, "Synthetic Device copy: " + definition.Key)); continue;
                }
                await using var writer = content.Open(); content.Authorization.Actor = "fixture-author";
                var proposal = await writer.Application.CreateDraftAsync(content.Draft("Synthetic finite Device copy", definition.Key));
                proposal = await writer.Application.ValidateAsync(content.Command(proposal)); content.Authorization.Actor = "fixture-approver";
                proposal = await writer.Application.ApproveAsync(content.Command(proposal)); content.Authorization.Actor = "fixture-publisher";
                publications.Add(definition.Key, await writer.Application.PublishAsync(content.Publish(proposal) with
                { EffectiveInterval = new(content.Clock.GetUtcNow(), true, content.Clock.GetUtcNow() + lifetime, false) }));
            }
            return (scenario, content, authority, publications);
        }
        catch { await scenario.DisposeAsync(); throw; }
    }

    private sealed class DevicePublicationAuthority(PresentationContentTestEnvironment content, string authenticationVersion,
        string deviceVersion, string database, string role) : IDeviceLocalContentBindingSource, IContentTransactionLocalProfileSource
    {
        internal string ConfigurationVersion = "fixture-device-content-v1";
        internal string? ContentScopeOverride;
        internal bool Available = true;
        internal DateTimeOffset? Until;
        internal int OrdinaryProfileReads;
        internal string DeviceVersion => deviceVersion;
        public DeviceContentBinding RequireCurrentLocal(AuthenticationPolicy policy, string version, DateTimeOffset at)
        {
            if (!Available) throw new InvalidOperationException("Synthetic Device binding unavailable.");
            var interval = new EffectivePolicyInterval(content.Interval.From, true, Until, false);
            var qualification = new ContentTransactionReadQualification(new("fixture-device-content-read", "ACTIVE", content.Scope, content.Interval, null, ["SYNTHETIC-ONLY"]),
                "fixture-device-read-evidence", database, role, 512, 512, 10, TimeSpan.FromSeconds(10));
            return new(authenticationVersion, deviceVersion, ConfigurationVersion, "fixture-device-content-qualification", ContentScopeOverride ?? content.Scope, content.Variant, interval, qualification);
        }
        public ContentGovernanceProfile RequireEffective(string scope, DateTimeOffset at)
        { OrdinaryProfileReads++; return RequireCurrentLocal(scope, at); }
        public ContentGovernanceProfile RequireCurrentLocal(string scope, DateTimeOffset at) =>
            Available ? content.RequireEffective(scope, at) : throw new ContentUnavailableException();
    }

    private sealed class DevicePublicationSqlGuard : DbCommandInterceptor
    {
        internal bool RejectSql, Fired;
        internal int SqlReads;
        internal Action? AfterDeviceSave;
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Check(); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(); return ValueTask.FromResult(result); }
        private void Check() { SqlReads++; if (RejectSql) throw new InvalidOperationException("Synthetic pure-witness SQL violation."); }
    }
    private sealed class DevicePublicationSaveGuard(DevicePublicationSqlGuard owner) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!owner.Fired && owner.AfterDeviceSave is not null && eventData.Context is IdentityPersistenceDbContext db &&
                db.ChangeTracker.Entries<DeviceOperationRow>().Any(x => x.Entity.State == "COMPLETED") &&
                db.ChangeTracker.Entries<StepUpProofRow>().Any(x => x.Entity.State == "CONSUMED"))
            { owner.Fired = true; owner.AfterDeviceSave(); }
            return ValueTask.FromResult(result);
        }
    }
}
