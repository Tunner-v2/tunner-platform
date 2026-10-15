using System.Net;
using System.Text;
using System.Text.Json;
using Tunner.Installer;
using Tunner.Platform.ObjectStorage;
using Tunner.Platform.Secrets;
using Xunit;

namespace Tunner.Installer.Tests;

public sealed class InstallerTests
{
    private static readonly string[] ExpectedReadPaths = ["/v1/auth/token/lookup-self", "/v1/fixture/data/r2"];
    internal static R2InstallationProfile Profile(bool confirmed = true) => new()
    {
        EnvironmentClass = "EPHEMERAL_TEST", EnvironmentReference = "fixture",
        Endpoint = "https://fixture.r2.cloudflarestorage.com", Bucket = "fixture-test",
        SecretStoreReference = "fixture-vault", OpenBaoEndpoint = "http://127.0.0.1:8200",
        CredentialResourceReference = "fixture/data/r2", AccessKeyField = "fixture-id",
        SecretKeyField = "fixture-secret", DedicatedNonProductionTestBucket = confirmed
    };

    [Fact]
    public async Task ProfileRoundTripContainsReferencesAndRefusesOverwrite()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var profile = Profile(false);
            await profile.SaveNewAsync(path);
            var original = await File.ReadAllTextAsync(path);
            var actual = await R2InstallationProfile.LoadAsync(path);
            Assert.False(actual.DedicatedNonProductionTestBucket);
            Assert.Equal(profile.CredentialResourceReference, actual.Validate().ResourceReference);
            await Assert.ThrowsAsync<IOException>(() => Profile().SaveNewAsync(path));
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.DoesNotContain("Token", original, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", original, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://fixture.r2.cloudflarestorage.com/bucket")]
    [InlineData("http://fixture.r2.cloudflarestorage.com")]
    [InlineData("https://id:secret@fixture.r2.cloudflarestorage.com")]
    [InlineData("https://fixture.r2.cloudflarestorage.com?secret=x")]
    public void RejectsNonR2AndEmbeddedConfiguration(string endpoint)
    {
        var json = JsonSerializer.Serialize(Profile()).Replace(Profile().Endpoint, endpoint, StringComparison.Ordinal);
        var profile = JsonSerializer.Deserialize<R2InstallationProfile>(json)!;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
    }

    [Theory]
    [InlineData("fixture/data/../secret")]
    [InlineData("fixture/data/%2e%2e/secret")]
    [InlineData("/fixture/data/r2")]
    [InlineData("fixture/metadata/r2")]
    [InlineData("fixture/data/r2?x=y")]
    [InlineData("fixture/data//r2")]
    public void RejectsUnsafeOrNonDataResourceReferences(string resource) =>
        Assert.Throws<ArgumentException>(() => OpenBaoSecretStore.ValidateResourceReference(resource));

    [Theory]
    [InlineData("http://vault.example.com")]
    [InlineData("https://user:secret@vault.example.com")]
    [InlineData("https://vault.example.com/v1/")]
    public void RejectsInsecureOrAmbiguousVaultEndpoints(string endpoint) =>
        Assert.Throws<ArgumentException>(() => OpenBaoSecretStore.ValidateEndpoint(new Uri(endpoint)));

    [Fact]
    public async Task UnknownProfileFieldsIncludingSecretsAreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var json = JsonSerializer.Serialize(Profile());
            await File.WriteAllTextAsync(path, json.Insert(1, "\"Token\":\"fixture-only\","));
            await Assert.ThrowsAsync<JsonException>(() => R2InstallationProfile.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UnconfirmedBucketMakesNoStorageCalls()
    {
        var store = new MemoryStorage();
        await Assert.ThrowsAsync<InvalidOperationException>(() => R2ContractProbe.RunAsync(Profile(false), store, store.IsAbsent));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task SuccessfulProbeComparesContentAndVerifiesDeletion()
    {
        var store = new MemoryStorage();
        var first = await R2ContractProbe.RunAsync(Profile(), store, store.IsAbsent);
        var second = await R2ContractProbe.RunAsync(Profile(), store, store.IsAbsent);
        Assert.True(first.Passed);
        Assert.True(second.Passed);
        Assert.NotEqual(first.ObjectKey, second.ObjectKey);
        Assert.Empty(store.Objects);
    }

    [Theory]
    [InlineData("put")]
    [InlineData("read")]
    [InlineData("content")]
    [InlineData("extra")]
    [InlineData("delete")]
    [InlineData("absence")]
    public async Task FailureCannotPassAndCleanupIsAttempted(string failure)
    {
        var store = new MemoryStorage { Failure = failure };
        var result = await R2ContractProbe.RunAsync(Profile(), store, store.IsAbsent);
        Assert.False(result.Passed);
        Assert.NotNull(result.FailureCode);
        Assert.True(store.DeleteAttempted);
        Assert.DoesNotContain("fixture-secret-response", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Equal(failure is not ("delete" or "absence"), result.CleanupVerified);
    }

    [Fact]
    public async Task CancellationDuringPutStillUsesIndependentCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new MemoryStorage { CancelDuringPut = cancellation };
        var result = await R2ContractProbe.RunAsync(Profile(), store, store.IsAbsent, cancellation.Token);
        Assert.False(result.Passed);
        Assert.True(result.CleanupVerified);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task SecretStoreBindsReferenceAndUsesKvV2ShapeWithoutDisclosure()
    {
        var handler = new FixtureHandler();
        using var store = new OpenBaoSecretStore(new Uri(Profile().OpenBaoEndpoint), "fixture-vault", "fixture-token", handler);
        using var values = await store.ReadAsync(Profile().Validate());
        Assert.Equal("fixture-id-value", values.GetRequired("fixture-id"));
        Assert.DoesNotContain("fixture-id-value", values.ToString(), StringComparison.Ordinal);
        Assert.Equal(ExpectedReadPaths, handler.Paths);
        Assert.All(handler.Tokens, token => Assert.Equal("fixture-token", token));
        values.Dispose();
        Assert.Throws<ObjectDisposedException>(() => values.GetRequired("fixture-id"));
    }

    [Fact]
    public async Task SecretCreationUsesAtomicCasZero()
    {
        var handler = new FixtureHandler();
        using var store = new OpenBaoSecretStore(new Uri(Profile().OpenBaoEndpoint), "fixture-vault", "fixture-token", handler);
        await store.CreateAsync(Profile().Validate(), new Dictionary<string, string> { ["id"] = "fixture-id-value" });
        using var body = JsonDocument.Parse(handler.WriteBody!);
        Assert.Equal(0, body.RootElement.GetProperty("options").GetProperty("cas").GetInt32());
        Assert.Equal("fixture-id-value", body.RootElement.GetProperty("data").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("root")]
    [InlineData("permanent")]
    [InlineData("redirect")]
    [InlineData("reject")]
    [InlineData("malformed")]
    public async Task InvalidBootstrapOrRemoteFailureNeverReadsSecretOrLeaksResponse(string mode)
    {
        var handler = new FixtureHandler { Mode = mode };
        using var store = new OpenBaoSecretStore(new Uri(Profile().OpenBaoEndpoint), "fixture-vault", "fixture-token", handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(Profile().Validate()));
        Assert.DoesNotContain("fixture-secret-response", error.ToString(), StringComparison.Ordinal);
        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task StoreMismatchAndCancellationDoNotSendRequests()
    {
        var handler = new FixtureHandler();
        using var store = new OpenBaoSecretStore(new Uri(Profile().OpenBaoEndpoint), "other-vault", "fixture-token", handler);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(Profile().Validate()));
        Assert.Empty(handler.Paths);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var valid = new OpenBaoSecretStore(new Uri(Profile().OpenBaoEndpoint), "fixture-vault", "fixture-token", handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => valid.ReadAsync(Profile().Validate(), cancelled.Token));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public string? Mode { get; init; }
        public List<string> Paths { get; } = [];
        public List<string> Tokens { get; } = [];
        public string? WriteBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsolutePath);
            Tokens.Add(request.Headers.GetValues("X-Vault-Token").Single());
            if (Mode == "redirect")
                return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://untrusted.invalid") } };
            if (Mode == "reject") return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("fixture-secret-response") };
            if (Mode == "malformed") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture-secret-response") };
            if (request.Method == HttpMethod.Post)
            {
                WriteBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("{}");
            }
            if (request.RequestUri.AbsolutePath.EndsWith("lookup-self", StringComparison.Ordinal))
                return Json(Mode == "root" ? "{\"data\":{\"ttl\":60,\"policies\":[\"root\"]}}" :
                    Mode == "permanent" ? "{\"data\":{\"ttl\":0,\"policies\":[\"default\"]}}" :
                    "{\"data\":{\"ttl\":60,\"policies\":[\"default\",\"fixture\"]}}");
            return Json("{\"data\":{\"data\":{\"fixture-id\":\"fixture-id-value\",\"fixture-secret\":\"fixture-secret-value\"}}}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class MemoryStorage : IObjectStorage
    {
        public string? Failure { get; init; }
        public CancellationTokenSource? CancelDuringPut { get; init; }
        public Dictionary<string, byte[]> Objects { get; } = [];
        public bool DeleteAttempted { get; private set; }
        public int Calls { get; private set; }

        public async Task PutAsync(ObjectStorageWriteRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            Objects[request.Address.Key] = buffer.ToArray();
            CancelDuringPut?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure == "put") throw new IOException("fixture-secret-response");
        }

        public Task<ObjectStorageReadResult> OpenReadAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failure == "read") throw new IOException("fixture-secret-response");
            var data = Objects[address.Key].ToArray();
            if (Failure == "content") data[0] ^= 1;
            if (Failure == "extra") data = [.. data, 1];
            return Task.FromResult(new ObjectStorageReadResult(new MemoryStream(data)));
        }

        public Task DeleteAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
        {
            Calls++;
            DeleteAttempted = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure == "delete") throw new IOException("fixture-secret-response");
            Objects.Remove(address.Key);
            return Task.CompletedTask;
        }

        public Task<bool> IsAbsent(ObjectStorageAddress address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Failure != "absence" && !Objects.ContainsKey(address.Key));
        }
    }
}
