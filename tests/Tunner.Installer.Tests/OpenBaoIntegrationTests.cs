using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Tunner.Platform.Secrets;
using Xunit;

namespace Tunner.Installer.Tests;

public sealed class OpenBaoIntegrationTests
{
    private static readonly string[] FixturePolicies = ["installer-fixture"];
    [Fact]
    public async Task RealOpenBaoEnforcesScopedExpiringTokenKvV2AndCreateOnlyWrites()
    {
        // Disposable fixture only. No deployed endpoint, token, policy or bucket is read or modified.
        var fixtureRoot = Guid.NewGuid().ToString("N");
        await using var container = new ContainerBuilder("openbao/openbao:2.7.0")
            .WithEnvironment("BAO_DEV_ROOT_TOKEN_ID", fixtureRoot)
            .WithCommand("server", "-dev", "-dev-listen-address=0.0.0.0:8200")
            .WithPortBinding(8200, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8200).ForPath("/v1/sys/health")))
            .Build();
        await container.StartAsync();
        var endpoint = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(8200)}/");
        using var admin = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(20) };
        admin.DefaultRequestHeaders.Add("X-Vault-Token", fixtureRoot);
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
        using var rootStore = new OpenBaoSecretStore(endpoint, reference.StoreReference, fixtureRoot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rootStore.ReadAsync(reference));
        using var store = new OpenBaoSecretStore(endpoint, reference.StoreReference, scopedCredential);
        var values = new Dictionary<string, string>
        {
            ["fixture-id"] = Guid.NewGuid().ToString("N"), ["fixture-secret"] = Guid.NewGuid().ToString("N")
        };
        await store.CreateAsync(reference, values);
        using (var actual = await store.ReadAsync(reference))
            Assert.Equal(values["fixture-secret"], actual.GetRequired("fixture-secret"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(reference,
            new Dictionary<string, string> { ["fixture-secret"] = "replacement-must-not-be-written" }));
        using (var unchanged = await store.ReadAsync(reference))
            Assert.Equal(values["fixture-secret"], unchanged.GetRequired("fixture-secret"));
        // This token's policy cannot read arbitrary sibling paths.
        using var scopedHttp = new HttpClient { BaseAddress = endpoint };
        scopedHttp.DefaultRequestHeaders.Add("X-Vault-Token", scopedCredential);
        using var denied = await scopedHttp.GetAsync("v1/fixture/data/unrelated");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task AutomaticLocalSessionCreatesReusesAndRevokesAgainstRealOpenBao()
    {
        var fixtureRoot = Guid.NewGuid().ToString("N");
        await using var container = new ContainerBuilder("openbao/openbao:2.7.0")
            .WithEnvironment("BAO_DEV_ROOT_TOKEN_ID", fixtureRoot)
            .WithCommand("server", "-dev", "-dev-listen-address=0.0.0.0:8200")
            .WithPortBinding(8200, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8200).ForPath("/v1/sys/health")))
            .Build();
        await container.StartAsync();
        var endpoint = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(8200)}/");
        using var admin = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(20) };
        admin.DefaultRequestHeaders.Add("X-Vault-Token", fixtureRoot);
        using (var mount = await admin.PostAsJsonAsync("v1/sys/mounts/fixture", new { type = "kv", options = new { version = "2" } }))
            Assert.Equal(HttpStatusCode.NoContent, mount.StatusCode);
        var initialPolicies = await MetadataKeysAsync(admin, "v1/sys/policies/acl?list=true");
        var initialAccessors = await MetadataKeysAsync(admin, "v1/auth/token/accessors?list=true");
        var profile = LocalBootstrapTests.LocalProfile(endpoint.AbsoluteUri);
        await using (var session = await LocalOpenBaoSession.CreateAsync(profile, fixtureRoot))
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
        await using (var again = await LocalOpenBaoSession.CreateAsync(profile, fixtureRoot))
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
}
