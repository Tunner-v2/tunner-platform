using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Handler.Abstractions;
using DotNet.Testcontainers.Configurations;
using Tunner.Platform.Secrets;
using Tunner.Testing.OpenBao;
using Xunit;

namespace Tunner.Installer.Tests;

public sealed class OpenBaoIntegrationTests
{
    private static readonly string[] FixturePolicies = ["installer-fixture"];
    [Fact]
    public async Task RealOpenBaoEnforcesScopedExpiringTokenKvV2AndCreateOnlyWrites()
    {
        // Disposable fixture only. No deployed endpoint, token, policy or bucket is read or modified.
        await using var fixture = await DisposableOpenBaoFixture.StartAsync();
        var endpoint = fixture.Endpoint;
        var admin = fixture.BootstrapClient;
        using (var mount = await admin.PostAsJsonAsync("v1/sys/mounts/fixture", new { type = "kv", options = new { version = "2" } }))
            Assert.Equal(HttpStatusCode.NoContent, mount.StatusCode);
        var policy = "path \"fixture/data/r2\" { capabilities = [\"create\", \"update\", \"read\"] }";
        using (var policyResponse = await admin.PutAsJsonAsync("v1/sys/policies/acl/installer-fixture", new { policy }))
            Assert.Equal(HttpStatusCode.NoContent, policyResponse.StatusCode);
        using var tokenResponse = await admin.PostAsJsonAsync("v1/auth/token/create", new
        {
            policies = FixturePolicies, ttl = "5m", explicit_max_ttl = "5m"
        });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var scopedCredential = tokenDocument.RootElement.GetProperty("auth").GetProperty("client_token").GetString()!;
        var reference = InstallerTests.Profile().Validate();
        using var rootStore = fixture.BootstrapStoreForRejectionTest(reference.StoreReference);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rootStore.ReadAsync(reference));
        using var store = new OpenBaoSecretStore(endpoint, reference.StoreReference, scopedCredential);
        var values = new Dictionary<string, string>
        {
            ["fixture-id"] = Guid.NewGuid().ToString("N"), ["fixture-secret"] = Guid.NewGuid().ToString("N")
        };
        await store.CreateAsync(reference, values);
        using (var actual = await store.ReadAsync(reference))
            Assert.True(StringComparer.Ordinal.Equals(values["fixture-secret"], actual.GetRequired("fixture-secret")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(reference,
            new Dictionary<string, string> { ["fixture-secret"] = "replacement-must-not-be-written" }));
        using (var unchanged = await store.ReadAsync(reference))
            Assert.True(StringComparer.Ordinal.Equals(values["fixture-secret"], unchanged.GetRequired("fixture-secret")));
        // This token's policy cannot read arbitrary sibling paths.
        using var scopedHttp = new HttpClient { BaseAddress = endpoint };
        scopedHttp.DefaultRequestHeaders.Add("X-Vault-Token", scopedCredential);
        using var denied = await scopedHttp.GetAsync("v1/fixture/data/unrelated");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task AutomaticLocalSessionCreatesReusesAndRevokesAgainstRealOpenBao()
    {
        await using var fixture = await DisposableOpenBaoFixture.StartAsync();
        var endpoint = fixture.Endpoint;
        var admin = fixture.BootstrapClient;
        using (var mount = await admin.PostAsJsonAsync("v1/sys/mounts/fixture", new { type = "kv", options = new { version = "2" } }))
            Assert.Equal(HttpStatusCode.NoContent, mount.StatusCode);
        var initialPolicies = await MetadataKeysAsync(admin, "v1/sys/policies/acl?list=true");
        var initialAccessors = await MetadataKeysAsync(admin, "v1/auth/token/accessors?list=true");
        var profile = LocalBootstrapTests.LocalProfile(endpoint.AbsoluteUri);
        await using (var session = await fixture.CreateSessionAsync(profile))
        {
            Assert.True(await LocalR2Setup.EnsureCredentialsAsync(profile, session.Secrets, () => new()
            {
                ["fixture-id"] = Guid.NewGuid().ToString("N"), ["fixture-secret"] = Guid.NewGuid().ToString("N")
            }));
            Assert.False(await LocalR2Setup.EnsureCredentialsAsync(profile, session.Secrets,
                () => throw new InvalidOperationException("must reuse the existing record")));
            var siblingProfile = LocalBootstrapTests.LocalProfile(endpoint.AbsoluteUri);
            var siblingJson = JsonSerializer.Serialize(siblingProfile).Replace("fixture/data/r2", "fixture/data/unrelated", StringComparison.Ordinal);
            var sibling = JsonSerializer.Deserialize<Tunner.Installer.R2InstallationProfile>(siblingJson)!.Validate();
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Secrets.ReadAsync(sibling));
        }
        Assert.Equal(initialPolicies, await MetadataKeysAsync(admin, "v1/sys/policies/acl?list=true"));
        Assert.Equal(initialAccessors, await MetadataKeysAsync(admin, "v1/auth/token/accessors?list=true"));
        // A later invocation resolves persisted credentials with a newly minted identity.
        await using (var again = await fixture.CreateSessionAsync(profile))
            Assert.False(await LocalR2Setup.EnsureCredentialsAsync(profile, again.Secrets,
                () => throw new InvalidOperationException("must not collect credentials on another run")));
        Assert.Equal(initialPolicies, await MetadataKeysAsync(admin, "v1/sys/policies/acl?list=true"));
        Assert.Equal(initialAccessors, await MetadataKeysAsync(admin, "v1/auth/token/accessors?list=true"));
    }

    private static async Task<string[]> MetadataKeysAsync(HttpClient admin, string path)
    {
        using var response = await admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("keys").EnumerateArray()
            .Select(value => value.GetString()!).Order(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task DisposablePrefixScopeKeepsPinnedVersionAndVerifiesCompleteCleanup()
    {
        await using var fixture = await DisposableOpenBaoFixture.StartAsync();
        var scope = fixture.CredentialScope();
        var session = await fixture.CreateSessionAsync(scope);
        var reference = new CredentialSecretReference(scope.StoreReference,
            scope.ResourcePrefix + "/" + Guid.NewGuid().ToString("N") + "/" + Guid.NewGuid().ToString("N"), 1);
        var value = Guid.NewGuid().ToString("N");
        await session.Secrets.CreateCredentialVersionAsync(reference, new Dictionary<string, string> { ["fixture-value"] = value });
        using (var actual = await session.Secrets.ReadCredentialVersionAsync(reference))
            Assert.True(actual is not null && StringComparer.Ordinal.Equals(value, actual.GetRequired("fixture-value")));
        var sibling = new CredentialSecretReference(scope.StoreReference, scope.ResourcePrefix + "-sibling/factor/generation", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Secrets.ReadCredentialVersionAsync(sibling));
        // A later vault version cannot silently replace the immutable reference's version-one read.
        using (var replaced = await fixture.BootstrapClient.PostAsJsonAsync("v1/" + reference.ResourceReference,
            new { options = new { cas = 1 }, data = new Dictionary<string, string> { ["fixture-value"] = "public-replacement-fixture" } }))
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        using (var original = await session.Secrets.ReadCredentialVersionAsync(reference))
            Assert.True(original is not null && StringComparer.Ordinal.Equals(value, original.GetRequired("fixture-value")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Secrets.CreateCredentialVersionAsync(reference,
            new Dictionary<string, string> { ["fixture-value"] = "must-not-overwrite" }));
        var provenance = fixture.Provenance;
        Assert.True(provenance.CachedOnly && provenance.HelperDisabled && provenance.LogPersistenceDisabled && provenance.LoopbackOnly && provenance.NoVolumes);
        Assert.Equal("2.7.0", provenance.Version);
        Assert.Matches("^sha256:[0-9a-f]{64}$", provenance.ImageId);
        Assert.Equal("SYNTHETIC_ONLY_UNTIL_VERIFIED_FIXTURE_REMOVAL", provenance.Retention);
        await fixture.DisposeAsync();
        Assert.True(session.CleanupVerified);
        Assert.True(fixture.CleanupVerified);
        await fixture.DisposeAsync();
    }
}

public sealed class DisposableOpenBaoCleanupTests
{
    private static readonly string[] CleanupOrder = ["sessions", "delete", "verify"];
    [Theory]
    [InlineData("sessions")]
    [InlineData("delete")]
    [InlineData("verify")]
    [InlineData("still-present")]
    public async Task EveryCleanupFailureRejectsSuccessAndStillAttemptsContainerCleanup(string failure)
    {
        var attempts = new List<string>();
        var result = await DisposableOpenBaoFixture.VerifyCleanupAsync(() =>
        {
            attempts.Add("sessions");
            return failure == "sessions" ? Task.FromException<bool>(new InvalidOperationException("private fixture failure")) : Task.FromResult(true);
        }, () =>
        {
            attempts.Add("delete");
            return failure == "delete" ? Task.FromException(new InvalidOperationException("private fixture failure")) : Task.CompletedTask;
        }, () =>
        {
            attempts.Add("verify");
            return failure == "verify" ? Task.FromException<bool>(new InvalidOperationException("private fixture failure")) : Task.FromResult(failure != "still-present");
        });
        Assert.False(result);
        Assert.Equal(CleanupOrder, attempts);
    }

    [Fact]
    public async Task CleanupSuccessRequiresSessionRevocationRemovalAndVerifiedAbsence() =>
        Assert.True(await DisposableOpenBaoFixture.VerifyCleanupAsync(() => Task.FromResult(true), () => Task.CompletedTask, () => Task.FromResult(true)));
}

public sealed class DisposableOpenBaoDockerEndpointTests
{
    [Theory]
    [InlineData("tcp://remote.invalid:2375")]
    [InlineData("http://remote.invalid:2375")]
    [InlineData("https://remote.invalid:2376")]
    [InlineData("tcp://127.0.0.1:2375")]
    [InlineData("npipe://remote.invalid/pipe/docker_engine")]
    [InlineData("npipe://user@localhost/pipe/docker_engine")]
    [InlineData("npipe://localhost/pipe/docker_engine?remote=true")]
    [InlineData("npipe://localhost/pipe/docker_engine/subpipe")]
    [InlineData("unix://remote.invalid/var/run/docker.sock")]
    [InlineData("unix:////remote.invalid/docker.sock")]
    public async Task UnsupportedDaemonIsRejectedBeforeCreatingAnyClient(string endpoint)
    {
        var configuration = new EndpointProbe(new Uri(endpoint));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DisposableOpenBaoFixture.StartAsync(configuration));
        Assert.Equal("DISPOSABLE_VAULT_LOCAL_DOCKER_REQUIRED", error.Message);
        Assert.DoesNotContain(endpoint, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, configuration.ClientFactoryCalls);
    }

    [Theory]
    [InlineData("npipe://./pipe/docker_engine")]
    [InlineData("npipe://localhost/pipe/dockerDesktopLinuxEngine")]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("unix:///run/user/1000/docker.sock")]
    public async Task SupportedLocalDaemonReachesOnlyTheControlledClientFactory(string endpoint)
    {
        var configuration = new EndpointProbe(new Uri(endpoint));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DisposableOpenBaoFixture.StartAsync(configuration));
        Assert.Equal("PUBLIC_CLIENT_FACTORY_REACHED", error.Message);
        Assert.Equal(1, configuration.ClientFactoryCalls);
    }

    private sealed class EndpointProbe(Uri endpoint) : IDockerEndpointAuthenticationConfiguration
    {
        public Version Version => new(1, 45);
        public Uri Endpoint { get; } = endpoint;
        public IAuthProvider AuthProvider => null!;
        public int ClientFactoryCalls { get; private set; }
        public DockerClientBuilder GetDockerClientBuilder(Guid sessionId)
        {
            ClientFactoryCalls++;
            throw new InvalidOperationException("PUBLIC_CLIENT_FACTORY_REACHED");
        }
    }
}
