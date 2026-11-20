using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Tunner.Platform.Secrets;
using Tunner.Testing;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Identity.Persistence.Tests;

public sealed class TotpOpenBaoIntegrationTests
{
    private static readonly string[] FixturePolicies = ["totp-fixture-only"];
    [Fact]
    public async Task RealScopedOpenBaoBindsOwnerGenerationAndVersionOneWhenLatestVersionChanges()
    {
        if (!TestHarnessProfiles.IsContainerExecutionEnabled()) throw SkipException.ForSkip("Explicit isolated container opt-in required.");
        var fixtureRoot = Guid.NewGuid().ToString("N");
        await using var container = new ContainerBuilder("openbao/openbao:2.7.0")
            .WithEnvironment("BAO_DEV_ROOT_TOKEN_ID", fixtureRoot).WithCommand("server", "-dev", "-dev-listen-address=0.0.0.0:8200")
            .WithPortBinding(8200, true).WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(x => x.ForPort(8200).ForPath("/v1/sys/health"))).Build();
        await container.StartAsync(); var endpoint = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(8200)}/");
        using var admin = new HttpClient { BaseAddress = endpoint }; admin.DefaultRequestHeaders.Add("X-Vault-Token", fixtureRoot);
        using (var mount = await admin.PostAsJsonAsync("v1/sys/mounts/fixture", new { type = "kv", options = new { version = "2" } })) Assert.Equal(HttpStatusCode.NoContent, mount.StatusCode);
        var binding = new TotpSeedBinding("ACC-PUBLIC-FIXTURE", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "fixture-binding");
        var resource = "fixture/data/totp/" + binding.AuthenticatorReference.ToString("N") + "/" + binding.Generation.ToString("N");
        var policy = "path \"" + resource + "\" { capabilities = [\"create\", \"update\", \"read\"] }";
        using (var approved = await admin.PutAsJsonAsync("v1/sys/policies/acl/totp-fixture-only", new { policy })) Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        using var response = await admin.PostAsJsonAsync("v1/auth/token/create", new { policies = FixturePolicies, ttl = "5m", explicit_max_ttl = "5m" }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var token = document.RootElement.GetProperty("auth").GetProperty("client_token").GetString()!;
        using var transport = new OpenBaoSecretStore(endpoint, "fixture-vault", token); var store = new OpenBaoTotpSeedStore(transport, "fixture-vault", "fixture/data/totp", "fixture-binding");
        var reference = store.Allocate(binding); const string publicFixtureSeed = "JBSWY3DPEHPK3PXP";
        await store.CreateOnceAsync(binding, reference, publicFixtureSeed, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateOnceAsync(binding, reference, "PUBLIC-REPLACEMENT-MUST-NOT-WIN", CancellationToken.None));
        using (var first = await store.ReadAsync(binding, reference, CancellationToken.None)) { Assert.NotNull(first); Assert.True(first.Require() == publicFixtureSeed); }
        // Provider latest may change independently. Identity must still read its immutable generation/version-one binding.
        using (var latest = await admin.PostAsJsonAsync("v1/" + resource, new { options = new { cas = 1 }, data = new Dictionary<string, string> { ["seed"] = "PUBLIC-LATEST-VERSION", ["account"] = "OTHER-OWNER" } })) Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        using (var original = await store.ReadAsync(binding, reference, CancellationToken.None)) { Assert.NotNull(original); Assert.True(original.Require() == publicFixtureSeed); }
        var ownerError = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(binding with { AccountId = "OTHER-OWNER" }, reference, CancellationToken.None)); Assert.Equal("SECRET_OWNER_BINDING_MISMATCH", ownerError.Message);
        var generationError = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(binding with { Generation = Guid.NewGuid() }, reference, CancellationToken.None)); Assert.Equal("SECRET_OWNER_BINDING_MISMATCH", generationError.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(binding, reference with { Version = 2 }, CancellationToken.None));
    }
}
