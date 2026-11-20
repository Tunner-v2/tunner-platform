using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityPostCommitDeviceTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task CommittedDeviceRemovalAttributesTheRemoversSessionAndPreservesTheOriginalObservationReference()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var qualification = new DeviceTrustTests.Qualification();
        await using var host = await Start(database, services =>
        {
            services.AddSingleton<IDeviceEvidencePolicySource>(qualification);
            services.AddSingleton<IDeviceTrustEligibilitySource>(qualification);
            services.AddSingleton<IDeviceSafeCharacteristicSource>(qualification);
            DeviceBrowserService.AddQualifiedEvidence(services);
        });
        await host.SeedAsync(database);
        // Separate anonymous bootstraps preserve two active sessions and two installation observations.
        var observed = await SignIn(host, database);
        var remover = await SignIn(host, database);
        Assert.NotEqual(observed.SessionId, remover.SessionId);
        DeviceTrustRemovalRequest request;
        await using (var read = database.Context())
        {
            var observation = await read.Set<DeviceSessionEvidenceRow>().AsNoTracking().SingleAsync(x => x.SessionId == observed.SessionId);
            var device = await read.Set<DeviceRecordRow>().AsNoTracking().SingleAsync(x => x.DeviceId == observation.DeviceId);
            var trust = await read.Set<DeviceTrustRow>().AsNoTracking().SingleAsync(x => x.DeviceId == device.DeviceId);
            Assert.Equal(observed.SessionId, trust.SourceSessionId);
            var account = await read.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == remover.AccountId);
            request = new(Guid.NewGuid(), device.DeviceId, device.Version, trust.Version, account.Version, "fixture-device-removal-" + Guid.NewGuid().ToString("N"));
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<IdentityPersistenceDbContext>();
            var authentication = services.GetRequiredService<AuthenticationApplication>();
            var devices = new DeviceApplication(db, authentication, host.Policies, qualification, qualification, host.Clock);
            var assurance = new AssurancePolicyCatalog([new(new("synthetic-device-proof", "ACTIVE", DeviceApplication.RevokeScope,
                new(host.Clock.GetUtcNow().AddHours(-1), true, host.Clock.GetUtcNow().AddHours(1), false), null, ["SYNTHETIC-ONLY"]),
                "synthetic-device-assurance", TimeSpan.FromMinutes(1), new Dictionary<string, string> { ["pwd"] = "synthetic-device-assurance" })]);
            var stepUp = new StepUpApplication(db, authentication, host.Policies, assurance,
                services.GetRequiredService<AuthenticationPasswordWork>(), services.GetRequiredService<IDataProtectionProvider>(), host.Clock);
            var owner = new DeviceOperationApplication(devices, stepUp);
            var context = await owner.PrepareAsync(remover, request, assurance); Assert.NotNull(context);
            var issued = await stepUp.IssuePasswordAsync(remover, context.Binding, context.ExpectedPolicyVersion, Password);
            Assert.Null(issued.ErrorCode);
            var grant = Assert.IsType<StepUpProofGrant>(issued.Grant);
            Assert.True((await owner.RevokeAsync(remover, request, grant.Key)).Applied);
        }
        await using var committed = database.Context();
        var revoked = await committed.Set<DeviceTrustRow>().AsNoTracking().SingleAsync(x => x.State == "REVOKED");
        var removal = await committed.Set<DeviceOperationRow>().AsNoTracking().SingleAsync();
        var consumed = await committed.Set<StepUpProofRow>().AsNoTracking().SingleAsync();
        Assert.Equal(observed.SessionId, revoked.SourceSessionId);
        Assert.Equal(remover.SessionId, removal.SessionId);
        Assert.Equal("COMPLETED", removal.State); Assert.Equal("CONSUMED", consumed.State);
        Assert.Equal(removal.Id, revoked.RemovalOperationId); Assert.Equal(consumed.ProofId, removal.ProofId);
        var message = IdentityPostCommitProcessor.Project(await committed.Set<OutboxMessage>().AsNoTracking().SingleAsync(x => x.EventType == "DeviceTrustRevoked"));
        Assert.Equal(message.EventId, removal.OutboxEventId); Assert.Equal(message.EventId, revoked.OutboxEventId);
        Assert.Equal(message.OccurredAtUtc, removal.CompletedAt);
        var source = new IdentitySecurityPostCommitOwnerSource(new Factory(database));
        var facts = Assert.IsType<IdentityPostCommitOwnerFacts>(await source.ReadCommittedAsync(message, default));
        Assert.Equal(remover.AccountId, facts.ActorAccountId);
        Assert.Equal(remover.SessionId.ToString("N"), facts.SessionReference);
        Assert.Equal(observed.SessionId.ToString("N"), facts.References["sourceSessionReference"]);
        Assert.Equal(removal.Id.ToString("N"), facts.References["removalOperationReference"]);
        Assert.Equal(message.Subject, facts.OwnerReference);
        Assert.Null(await source.ReadCommittedAsync(message with { OperationId = "unrelated-removal" }, default));
        Assert.Null(await source.ReadCommittedAsync(message with { CorrelationId = "unrelated-correlation" }, default));
    }

    private static async Task<StepUpCaller> SignIn(AuthenticationTestHost host, IdentityTestDatabase database)
    {
        var options = await host.Options();
        using var response = await host.Post("/authentication/sign-in", Command(options.Key), options.Cookies, options.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var read = database.Context();
        var session = await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.IdempotencyKey == options.Key);
        await using var scope = host.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.Session.Ticket.v1");
        var ticket = TicketSerializer.Default.Deserialize(Convert.FromBase64String(protector.Unprotect(session.ProtectedTicket)))!;
        return StepUpCaller.FromPrincipal(ticket.Principal, Origin, new string('B', 64));
    }

    private sealed class Factory(IdentityTestDatabase database) : IDbContextFactory<IdentityPersistenceDbContext>
    {
        public IdentityPersistenceDbContext CreateDbContext() => database.Context();
        public Task<IdentityPersistenceDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }
}
