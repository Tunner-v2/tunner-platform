using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tunner.Database.Outbox;
using Tunner.Testing;
using Tunner.Testing.Recovery;
using Xunit;
using Xunit.Sdk;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Standard xUnit lifecycle for this exact suite; all database/migration mechanics remain shared.</summary>
public sealed class SessionListingPostgresFixture : IAsyncLifetime
{
    private const string Image = "sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873";
    private IContainer? container;
    private DockerClient? docker;
    private string[] ownedVolumes = [];
    public async Task InitializeAsync()
    {
        if (!TestHarnessProfiles.IsContainerExecutionEnabled()) return;
        var run = Environment.GetEnvironmentVariable("TUNNER_SESSION_LISTING_RUN_ID");
        var engine = Environment.GetEnvironmentVariable("TUNNER_SESSION_LISTING_ENGINE_ID");
        var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (run is not { Length: 32 } || run.Any(c => !char.IsAsciiHexDigitLower(c)) || string.IsNullOrWhiteSpace(engine) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var address) || address.Scheme != "npipe" || address.Host != "." ||
            address.Query.Length != 0 || address.Fragment.Length != 0 || address.UserInfo.Length != 0 ||
            Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED") != "true")
            throw new InvalidOperationException("Explicit isolated session-listing integration inputs are required.");
        docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid())
            .WithLogger(NullLogger.Instance).Build();
        if (TestcontainersSettings.OS.DockerEndpointAuthConfig.Endpoint != address ||
            (await docker.System.GetSystemInfoAsync()).ID != engine || (await docker.Images.InspectImageAsync(Image)).ID != Image)
            throw new InvalidOperationException("The admitted local session-listing engine/image is unavailable.");
        container = new ContainerBuilder(Image).WithDockerEndpoint(address).WithImagePullPolicy(static _ => false)
            .WithLogger(NullLogger.Instance).WithCleanUp(true)
            .WithLabel("tunner.p2-013.run", run).WithLabel("tunner.p2-013.scope", "owned-session-listing")
            .WithEnvironment("POSTGRES_USER", "tunner").WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "trust")
            .WithPortBinding(5432, true)
            .WithCreateParameterModifier(parameters =>
            {
                parameters.HostConfig ??= new();
                parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
                { ["5432/tcp"] = [new() { HostIP = "127.0.0.1", HostPort = "" }] };
            })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-U", "tunner", "-d", "postgres"))
            .Build();
        await container.StartAsync();
        var actual = await docker.Containers.InspectContainerAsync(container.Id);
        ownedVolumes = (actual.Mounts ?? []).Where(x => x.Type == "volume" && !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name!).Distinct(StringComparer.Ordinal).ToArray();
        var ports = actual.NetworkSettings?.Ports;
        if (actual.Image != Image || actual.Config?.Labels is not { } labels || !labels.TryGetValue("tunner.p2-013.run", out var actualRun) || actualRun != run ||
            ports is null || !ports.TryGetValue("5432/tcp", out var bindings) || bindings is not { Count: 1 } || bindings[0].HostIP != "127.0.0.1" ||
            container.GetMappedPublicPort(5432) == 25432)
            throw new InvalidOperationException("The disposable session-listing container boundary is invalid.");
    }
    internal async Task<IdentityTestDatabase> CreateDatabaseAsync()
    {
        if (container is null) throw SkipException.ForSkip("Explicit isolated session-listing integration profile required.");
        var admin = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = container.GetMappedPublicPort(5432), Database = "postgres", Username = "tunner" };
        return new(await RecoveryCodeIdentityDatabaseProvisioning.CreateAsync(admin, null));
    }
    public async Task DisposeAsync()
    {
        try
        {
            if (container is not null)
            {
                var id = container.Id; await container.DisposeAsync();
                var absent = false;
                try { await docker!.Containers.InspectContainerAsync(id); }
                catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { absent = true; }
                if (!absent) throw new InvalidOperationException("The owned session-listing container was not removed.");
                foreach (var name in ownedVolumes)
                {
                    absent = false;
                    try { await docker!.Volumes.InspectAsync(name); }
                    catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { absent = true; }
                    if (!absent) throw new InvalidOperationException("An owned session-listing volume was not removed.");
                }
            }
        }
        finally { docker?.Dispose(); }
    }
}

public sealed class SessionListingTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private static readonly string Network = new('B', 64);
    private static async Task<StepUpCaller> SignIn(AuthenticationTestHost host, string email = "person@example.test", string origin = Origin)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var attempt = await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().SignInAsync(
            new(AuthenticationIdentity.Digest(Guid.NewGuid().ToString()), Network, origin, null), Command(Guid.NewGuid(), email: email), CancellationToken.None);
        Assert.True(attempt.ErrorCode is null && attempt.Grant is not null);
        return StepUpCaller.FromPrincipal(attempt.Grant!.Ticket.Principal, origin, Network);
    }
    // Insert a separate synthetic non-User record; never rewrite immutable issued session identity.
    private static async Task InsertForeignSchemeSession(AuthenticationTestHost host, IdentityTestDatabase db, Guid sourceId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        await using var context = db.Context();
        var source = await context.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == sourceId);
        var row = (AuthenticationSessionRow)context.Entry(source).CurrentValues.ToObject();
        row.SessionId = Guid.NewGuid(); row.Scheme = "Admin"; row.IdempotencyKey = Guid.NewGuid();
        row.BrowserDigest = AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N"));
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        row.KeyDigest = AuthenticationIdentity.Digest(key);
        row.ProtectedKey = protection.CreateProtector("Tunner.User.Session.RetryKey.v1").Protect(key);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, row.AccountId),
            new(AuthenticationIdentity.SessionClaim, row.SessionId.ToString("N")), new(AuthenticationIdentity.OriginClaim, row.Origin),
            new("amr", row.AuthenticationMethods), new("acr", row.Assurance)], row.Scheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        { IssuedUtc = row.AuthenticatedAt, ExpiresUtc = row.ExpiresAt, IsPersistent = row.RememberMe, AllowRefresh = false }, row.Scheme);
        row.ProtectedTicket = protection.CreateProtector("Tunner.User.Session.Ticket.v1").Protect(Convert.ToBase64String(TicketSerializer.Default.Serialize(ticket)));
        context.Add(row); await context.SaveChangesAsync();
    }
    private static async Task<SessionListingPage?> List(AuthenticationTestHost host, StepUpCaller caller, int maximum = 10, SessionListingContinuation? after = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().ListOwnedSessionsAsync(caller, new(maximum, after));
    }

    [Fact]
    public async Task ListsOnlyOwnedUserOriginAndSafeProjection()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db);
        await host.SeedAsync(db); await host.SeedAsync(db, foreignEmail: true);
        var other = await SignIn(host); var actor = await SignIn(host); var foreign = await SignIn(host, "foreign@example.test");
        await SignIn(host, origin: "https://other.example.test");
        await InsertForeignSchemeSession(host, db, other.SessionId);
        var page = await List(host, actor); Assert.NotNull(page);
        Assert.True(page.Items.Count == 2 && page.Items.Count(x => x.IsCurrent) == 1 && !page.HasMore && page.Next is null);
        Assert.True(page.Items.Any(x => x.SessionReference == other.SessionId) && page.Items.All(x => x.SessionReference != foreign.SessionId));
        await using var committed = db.Context();
        var current = await committed.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == actor.SessionId);
        Assert.True(page.Items.Single(x => x.IsCurrent).Version == current.Version && page.Items.Single(x => x.IsCurrent).LastSeenAt == current.LastSeenAt);
    }

    [Fact]
    public async Task BoundedKeysetEnumeratesTiesOnceAndRejectsForeignActorContinuation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await SignIn(host); var other = await SignIn(host); await SignIn(host);
        var seen = new HashSet<Guid>(); SessionListingContinuation? next = null;
        for (var i = 0; i < 3; i++)
        {
            var page = await List(host, actor, 1, next); Assert.NotNull(page);
            Assert.True(page.Items.Count == 1 && seen.Add(page.Items[0].SessionReference) && page.HasMore == (i < 2));
            next = page.Next;
        }
        Assert.True(seen.Count == 3 && next is null);
        var first = await List(host, actor, 1); Assert.NotNull(first?.Next);
        await Assert.ThrowsAsync<ArgumentException>(() => List(host, other, 1, first!.Next));
    }

    [Fact]
    public async Task SessionUseDoesNotGrantListPermission()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await SignIn(host); host.Access.DeniedOperation = AuthenticationApplication.ListSessionsOperation;
        Assert.Null(await List(host, actor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedOrExpiredActorCannotList(bool expired)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await SignIn(host);
        if (expired) host.Clock.Advance(host.Policies.Policy.AbsoluteLifetime);
        else
        {
            await using var update = db.Context(); var row = await update.Set<AuthenticationSessionRow>().SingleAsync();
            row.State = "REVOKED"; row.RevokedAt = host.Clock.GetUtcNow(); row.Version++; await update.SaveChangesAsync();
        }
        Assert.Null(await List(host, actor));
    }

    [Fact]
    public async Task TargetExpiryAndStampDenialAreTruthfulWithoutTargetMutationOrEvents()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var expired = await SignIn(host);
        host.Clock.Advance(host.Policies.Policy.AbsoluteLifetime + TimeSpan.FromSeconds(1));
        var denied = await SignIn(host);
        await using (var update = db.Context())
        {
            var user = await update.Users.SingleAsync();
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            await update.SaveChangesAsync();
        }
        var actor = await SignIn(host);
        await using var before = db.Context();
        var targets = await before.Set<AuthenticationSessionRow>().AsNoTracking().Where(x => x.SessionId != actor.SessionId).ToArrayAsync();
        var events = await before.Set<OutboxMessage>().CountAsync();
        var page = await List(host, actor); Assert.NotNull(page);
        Assert.True(page.Items.Single(x => x.SessionReference == expired.SessionId).EffectiveState == SessionListingState.Expired);
        Assert.True(page.Items.Single(x => x.SessionReference == denied.SessionId).EffectiveState == SessionListingState.Revoked);
        await using var after = db.Context();
        foreach (var target in targets)
        {
            var unchanged = await after.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == target.SessionId);
            Assert.True(unchanged.Version == target.Version && unchanged.LastSeenAt == target.LastSeenAt && unchanged.State == target.State);
        }
        Assert.True(await after.Set<OutboxMessage>().CountAsync() == events);
    }

    [Fact]
    public async Task MissingPolicyAndStaleAccessFailClosed()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await SignIn(host); host.Policies.Available = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => List(host, actor));
        host.Policies.Available = true; host.Access.Stale = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => List(host, actor));
    }

    [Fact]
    public async Task AccessExpiringDuringReadCannotReturnAListing()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var host = await Start(db); await host.SeedAsync(db);
        var actor = await SignIn(host); var decisions = 0;
        host.Access.DecisionLifetime = TimeSpan.FromSeconds(1);
        host.Access.BeforeDecision = _ => { if (++decisions == 4) host.Clock.Advance(TimeSpan.FromSeconds(2)); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => List(host, actor));
    }

    [Fact]
    public async Task ReResolvesPolicyAtTheFinalDecisionBoundary()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var policies = new DecisionBoundaryPolicies();
        await using var host = await Start(db, services => services.AddSingleton<IAuthenticationPolicySource>(policies)); await host.SeedAsync(db);
        var actor = await SignIn(host); policies.Arm();
        await Assert.ThrowsAsync<InvalidOperationException>(() => List(host, actor));
        Assert.True(policies.Reads == 4);
    }
    private sealed class DecisionBoundaryPolicies : IAuthenticationPolicySource
    {
        private readonly AuthenticationPolicy policy = new TestPolicies().Policy;
        private bool armed;
        internal int Reads { get; private set; }
        internal void Arm() { armed = true; Reads = 0; }
        public AuthenticationPolicy RequireEffective(DateTimeOffset at) => armed && ++Reads >= 4
            ? policy with { Version = "fixture-policy-new-at-final-decision" } : policy;
    }
}

public sealed class SessionListingContractTests
{
    [Fact]
    public void ReadBudgetRequiresAnExplicitSafePositiveValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionListingReadWindow(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionListingReadWindow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionListingReadWindow(int.MaxValue));
    }
    [Fact]
    public void ProjectionContainsOnlyDeclaredSafeEvidence()
    {
        var properties = typeof(SessionListingItem).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(properties.SetEquals(["SessionReference", "Version", "IsCurrent", "AuthenticatedAt", "LastSeenAt", "ExpiresAt", "RecordedState", "EffectiveState", "EvaluatedAt"]));
    }
}
