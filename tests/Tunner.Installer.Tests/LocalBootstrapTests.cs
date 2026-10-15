using System.Net;
using System.Text;
using System.Text.Json;
using Tunner.Installer;
using Tunner.Platform.Secrets;
using Xunit;

namespace Tunner.Installer.Tests;

public sealed class LocalBootstrapTests
{
    internal static R2InstallationProfile LocalProfile(string endpoint = "http://127.0.0.1:8200") => new()
    {
        EnvironmentClass = "LOCAL_DEVELOPMENT", EnvironmentReference = "fixture",
        Endpoint = "https://fixture.r2.cloudflarestorage.com", Bucket = "fixture-test",
        SecretStoreReference = "fixture-vault", OpenBaoEndpoint = endpoint,
        CredentialResourceReference = "fixture/data/r2", AccessKeyField = "fixture-id",
        SecretKeyField = "fixture-secret", DedicatedNonProductionTestBucket = true
    };

    [Fact]
    public async Task IssuesExactResourceIdentityAndRemovesBothTemporaryResources()
    {
        var handler = new AdminHandler();
        var session = await LocalOpenBaoSession.CreateAsync(LocalProfile(), "fixture-bootstrap", handler);
        Assert.DoesNotContain("fixture-bootstrap", session.ToString(), StringComparison.Ordinal);
        Assert.Contains("fixture/data/r2", handler.Policy, StringComparison.Ordinal);
        Assert.DoesNotContain("*", handler.Policy, StringComparison.Ordinal);
        Assert.Contains("auth/token/lookup-self", handler.Policy, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.IssueBody!);
        Assert.True(body.RootElement.GetProperty("no_default_policy").GetBoolean());
        Assert.False(body.RootElement.GetProperty("renewable").GetBoolean());
        Assert.Equal("30m", body.RootElement.GetProperty("explicit_max_ttl").GetString());
        Assert.Single(body.RootElement.GetProperty("policies").EnumerateArray());
        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.Equal(1, handler.Revocations);
        Assert.Equal(1, handler.Deletions);
        Assert.All(handler.Headers, value => Assert.Equal("fixture-bootstrap", value));
    }

    [Theory]
    [InlineData("https://vault.example.com")]
    [InlineData("http://vault.example.com")]
    public async Task AdministrativeInputIsNeverSentToRemoteEndpoints(string endpoint)
    {
        var handler = new AdminHandler();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            LocalOpenBaoSession.CreateAsync(LocalProfile(endpoint), "fixture-bootstrap", handler));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task NonLocalEnvironmentIsRejectedBeforeAuthentication()
    {
        var handler = new AdminHandler();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalOpenBaoSession.CreateAsync(InstallerTests.Profile(), "fixture-bootstrap", handler));
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData("not-root")]
    [InlineData("malformed")]
    [InlineData("extra-policy")]
    [InlineData("permanent")]
    [InlineData("renewable")]
    [InlineData("create-failure")]
    [InlineData("policy-failure")]
    public async Task FailedBootstrapIsSanitizedAndCleansAnyAttemptedPolicy(string mode)
    {
        var handler = new AdminHandler { Mode = mode };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalOpenBaoSession.CreateAsync(LocalProfile(), "fixture-bootstrap", handler));
        Assert.DoesNotContain("fixture-sensitive-response", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-bootstrap", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(mode == "not-root" ? 0 : 1, handler.Deletions);
    }

    [Theory]
    [InlineData("revoke-failure")]
    [InlineData("delete-failure")]
    public async Task CleanupFailureFailsClosedAndStillAttemptsBothOperations(string mode)
    {
        var handler = new AdminHandler { Mode = mode };
        var session = await LocalOpenBaoSession.CreateAsync(LocalProfile(), "fixture-bootstrap", handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
        Assert.Equal("LOCAL_BOOTSTRAP_CLEANUP_UNVERIFIED", error.Message);
        Assert.Equal(1, handler.Revocations);
        Assert.Equal(1, handler.Deletions);
    }

    [Fact]
    public async Task CleanupIsIndependentOfTheCanceledOperation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new AdminHandler();
        var session = await LocalOpenBaoSession.CreateAsync(LocalProfile(), "fixture-bootstrap", handler, cancellation.Token);
        cancellation.Cancel();
        await session.DisposeAsync();
        Assert.Equal(1, handler.Revocations);
        Assert.Equal(1, handler.Deletions);
    }

    private sealed class AdminHandler : HttpMessageHandler
    {
        public string? Mode { get; init; }
        public List<string> Paths { get; } = [];
        public List<string> Headers { get; } = [];
        public string? Policy { get; private set; }
        public string? IssueBody { get; private set; }
        public int Revocations { get; private set; }
        public int Deletions { get; private set; }
        private string policyName = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            Headers.Add(request.Headers.GetValues("X-Vault-Token").Single());
            if (path.EndsWith("lookup-self", StringComparison.Ordinal))
                return Json(new { data = new { policies = new[] { Mode == "not-root" ? "fixture" : "root" } } });
            if (request.Method == HttpMethod.Delete)
            {
                Deletions++;
                return Mode == "delete-failure" ? Reject() : Json(new { });
            }
            if (path.EndsWith("revoke-accessor", StringComparison.Ordinal))
            {
                Revocations++;
                return Mode == "revoke-failure" ? Reject() : Json(new { });
            }
            if (path.Contains("/sys/policies/acl/", StringComparison.Ordinal))
            {
                policyName = path[(path.LastIndexOf('/') + 1)..];
                using var policyDocument = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Policy = policyDocument.RootElement.GetProperty("policy").GetString();
                return Mode == "policy-failure" ? Reject() : Json(new { });
            }
            IssueBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Mode == "create-failure") return Reject();
            if (Mode == "malformed") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture-sensitive-response") };
            return Json(new
            {
                auth = new
                {
                    accessor = "fixture-accessor", client_token = "fixture-issued-identity",
                    policies = Mode == "extra-policy" ? new[] { policyName, "root" } : [policyName],
                    lease_duration = Mode == "permanent" ? 0 : 1800, renewable = Mode == "renewable"
                }
            });
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        private static HttpResponseMessage Reject() => new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("fixture-sensitive-response")
        };
    }
}

public sealed class LocalCredentialSetupTests
{
    [Fact]
    public async Task FirstRunCollectsOnceAndRepeatRunReusesWithoutOverwriting()
    {
        var handler = new VaultHandler();
        using var store = new OpenBaoSecretStore(new Uri("http://127.0.0.1:8200"), "fixture-vault", "fixture-identity", handler);
        var collected = 0;
        var inputs = new Dictionary<string, string> { ["fixture-id"] = "fixture-key-id", ["fixture-secret"] = "fixture-key-value" };
        Assert.True(await LocalR2Setup.EnsureCredentialsAsync(LocalBootstrapTests.LocalProfile(), store, () => { collected++; return inputs; }));
        Assert.Empty(inputs);
        Assert.False(await LocalR2Setup.EnsureCredentialsAsync(LocalBootstrapTests.LocalProfile(), store, () => { collected++; throw new InvalidOperationException("should not collect again"); }));
        Assert.Equal(1, collected);
        Assert.Equal(1, handler.Writes);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("missing-identity")]
    [InlineData("incomplete-record")]
    public async Task AuthenticationOrRecordErrorsNeverCollectOrOverwriteCredentials(string mode)
    {
        var handler = new VaultHandler { Mode = mode };
        using var store = new OpenBaoSecretStore(new Uri("http://127.0.0.1:8200"), "fixture-vault", "fixture-identity", handler);
        var collected = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => LocalR2Setup.EnsureCredentialsAsync(
            LocalBootstrapTests.LocalProfile(), store, () => { collected = true; return []; }));
        Assert.False(collected);
        Assert.Equal(0, handler.Writes);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CombinedOutcomePreservesRecoveryKeyAndRequiresBothCleanupResults(bool probePassed, bool bootstrapCleaned)
    {
        var handler = new VaultHandler();
        var session = new FixtureSession(handler, bootstrapCleaned);
        var result = await LocalR2Setup.RunAsync(LocalBootstrapTests.LocalProfile(), session,
            () => new() { ["fixture-id"] = "fixture-key-id", ["fixture-secret"] = "fixture-key-value" },
            (_, _, _) => Task.FromResult(new R2ProbeResult("fixture-recovery-key", probePassed, probePassed,
                probePassed ? null : "CLEANUP_FAILED")));
        Assert.Equal("fixture-recovery-key", result.ObjectKey);
        Assert.Equal(probePassed && bootstrapCleaned, result.Passed);
        Assert.Equal(bootstrapCleaned, result.BootstrapCleanupVerified);
        Assert.Equal(probePassed ? null : "CLEANUP_FAILED", result.ProbeFailureCode);
        Assert.Equal(bootstrapCleaned ? result.ProbeFailureCode : "BOOTSTRAP_CLEANUP_FAILED", result.FailureCode);
        Assert.Equal(1, session.CleanupCalls);
    }

    [Fact]
    public async Task SetupFailureStillCleansSessionWithoutCallingTheProbe()
    {
        var session = new FixtureSession(new VaultHandler { Mode = "forbidden" }, true);
        var called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => LocalR2Setup.RunAsync(LocalBootstrapTests.LocalProfile(),
            session, () => [], (_, _, _) => { called = true; return Task.FromResult(new R2ProbeResult("unused", true, true, null)); }));
        Assert.False(called);
        Assert.Equal(1, session.CleanupCalls);
    }

    [Fact]
    public async Task CanceledSetupStillCleansSession()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = new FixtureSession(new VaultHandler(), true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalR2Setup.RunAsync(LocalBootstrapTests.LocalProfile(),
            session, () => [], (_, _, _) => Task.FromResult(new R2ProbeResult("unused", true, true, null)), cancellation.Token));
        Assert.Equal(1, session.CleanupCalls);
    }

    private sealed class FixtureSession(VaultHandler handler, bool cleanupSucceeds) : ILocalOpenBaoSession
    {
        public OpenBaoSecretStore Secrets { get; } = new(new Uri("http://127.0.0.1:8200"), "fixture-vault", "fixture-identity", handler);
        public int CleanupCalls { get; private set; }
        public ValueTask DisposeAsync()
        {
            CleanupCalls++;
            Secrets.Dispose();
            return cleanupSucceeds ? ValueTask.CompletedTask : ValueTask.FromException(new InvalidOperationException("fixture cleanup failure"));
        }
    }

    private sealed class VaultHandler : HttpMessageHandler
    {
        private static readonly string[] FixtureReadPolicies = ["fixture"];
        public string? Mode { get; init; }
        public int Writes { get; private set; }
        private Dictionary<string, string>? saved;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath.EndsWith("lookup-self", StringComparison.Ordinal))
                return Mode == "missing-identity" ? new(HttpStatusCode.NotFound) :
                    Json(new { data = new { ttl = 600, policies = FixtureReadPolicies } });
            if (Mode == "forbidden") return new(HttpStatusCode.Forbidden);
            if (Mode == "incomplete-record") return Json(new { data = new { data = new { unrelated = "fixture" } } });
            if (request.Method == HttpMethod.Post)
            {
                Writes++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal(0, body.RootElement.GetProperty("options").GetProperty("cas").GetInt32());
                saved = body.RootElement.GetProperty("data").Deserialize<Dictionary<string, string>>();
                return Json(new { });
            }
            return saved is null ? new(HttpStatusCode.NotFound) : Json(new { data = new { data = saved } });
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }
}
