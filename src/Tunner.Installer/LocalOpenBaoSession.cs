using System.Net.Http.Json;
using System.Text.Json;
using Tunner.Platform.Secrets;

namespace Tunner.Installer;

/// <summary>Local operator bootstrap only. Application secrets always use the bounded child identity.</summary>
public sealed class LocalOpenBaoSession : ILocalOpenBaoSession
{
    private readonly HttpClient client;
    private readonly string bootstrapCredential;
    private readonly string policyName = "tunner-r2-local-" + Guid.NewGuid().ToString("N");
    private OpenBaoSecretStore? secrets;
    private string? accessor;
    private bool policyAttempted;
    private bool tokenAttempted;
    private bool disposed;

    private LocalOpenBaoSession(Uri endpoint, string bootstrapCredential, HttpMessageHandler handler)
    {
        this.bootstrapCredential = bootstrapCredential;
        client = new HttpClient(handler)
        {
            BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 65536
        };
    }

    public OpenBaoSecretStore Secrets => secrets ?? throw new InvalidOperationException("LOCAL_SESSION_UNAVAILABLE");

    public static Task<LocalOpenBaoSession> CreateAsync(R2InstallationProfile profile, string bootstrapCredential,
        CancellationToken cancellationToken = default)
        => CreateAsync(profile, bootstrapCredential, new HttpClientHandler { AllowAutoRedirect = false }, cancellationToken);

    internal static async Task<LocalOpenBaoSession> CreateAsync(R2InstallationProfile profile, string bootstrapCredential,
        HttpMessageHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(handler);
        try
        {
            profile.Validate();
            if (profile.EnvironmentClass != "LOCAL_DEVELOPMENT" || !new Uri(profile.OpenBaoEndpoint).IsLoopback)
                throw new InvalidOperationException("LOCAL_LOOPBACK_BOOTSTRAP_REQUIRED");
            if (string.IsNullOrWhiteSpace(bootstrapCredential) || bootstrapCredential.Any(char.IsControl))
                throw new InvalidOperationException("LOCAL_BOOTSTRAP_INPUT_REQUIRED");
        }
        catch { handler.Dispose(); throw; }
        var session = new LocalOpenBaoSession(new Uri(profile.OpenBaoEndpoint), bootstrapCredential, handler);
        try
        {
            await session.InitializeAsync(profile, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            if (exception is OperationCanceledException) throw;
            throw new InvalidOperationException("LOCAL_BOOTSTRAP_FAILED");
        }
    }

    private async Task InitializeAsync(R2InstallationProfile profile, CancellationToken cancellationToken)
    {
        using (var identity = await RequestAsync(HttpMethod.Get, "v1/auth/token/lookup-self", null, cancellationToken).ConfigureAwait(false))
        {
            if (!identity.RootElement.GetProperty("data").GetProperty("policies").EnumerateArray().Any(p => p.GetString() == "root"))
                throw new InvalidOperationException("LOCAL_ADMIN_BOOTSTRAP_REQUIRED");
        }
        var policy = "path \"" + profile.CredentialResourceReference +
            "\" { capabilities = [\"create\", \"update\", \"read\"] }\n" +
            "path \"auth/token/lookup-self\" { capabilities = [\"read\"] }";
        policyAttempted = true; // A timed-out write can still have created the uniquely named policy.
        using (await RequestAsync(HttpMethod.Post, "v1/sys/policies/acl/" + policyName,
            new { policy }, cancellationToken).ConfigureAwait(false)) { }
        tokenAttempted = true;
        using var issued = await RequestAsync(HttpMethod.Post, "v1/auth/token/create", new
        {
            policies = new[] { policyName }, ttl = "30m", explicit_max_ttl = "30m",
            renewable = false, no_default_policy = true
        }, cancellationToken).ConfigureAwait(false);
        var auth = issued.RootElement.GetProperty("auth");
        accessor = auth.GetProperty("accessor").GetString();
        var issuedCredential = auth.GetProperty("client_token").GetString();
        var policies = auth.GetProperty("policies").EnumerateArray().Select(p => p.GetString()).ToArray();
        var lifetime = auth.GetProperty("lease_duration").GetInt64();
        if (string.IsNullOrWhiteSpace(accessor) || string.IsNullOrWhiteSpace(issuedCredential) ||
            lifetime is <= 0 or > 1800 || auth.GetProperty("renewable").GetBoolean() ||
            policies.Length != 1 || policies[0] != policyName)
            throw new InvalidOperationException("LOCAL_CHILD_IDENTITY_INVALID");
        secrets = new OpenBaoSecretStore(new Uri(profile.OpenBaoEndpoint), profile.SecretStoreReference, issuedCredential);
    }

    private async Task<JsonDocument> RequestAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Vault-Token", bootstrapCredential);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("LOCAL_BOOTSTRAP_REQUEST_REJECTED");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return bytes.Length == 0 ? JsonDocument.Parse("{}") : JsonDocument.Parse(bytes);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new InvalidOperationException("LOCAL_BOOTSTRAP_RESPONSE_INVALID");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        secrets?.Dispose();
        var revoked = !tokenAttempted;
        var removed = !policyAttempted;
        try
        {
            if (!string.IsNullOrWhiteSpace(accessor))
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    using var response = await RequestAsync(HttpMethod.Post, "v1/auth/token/revoke-accessor",
                        new { accessor }, deadline.Token).ConfigureAwait(false);
                    revoked = true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException) { revoked = false; }
            }
            if (policyAttempted)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    using var response = await RequestAsync(HttpMethod.Delete, "v1/sys/policies/acl/" + policyName,
                        null, deadline.Token).ConfigureAwait(false);
                    removed = true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException) { removed = false; }
            }
        }
        finally { client.Dispose(); GC.SuppressFinalize(this); }
        if (!revoked || !removed) throw new InvalidOperationException("LOCAL_BOOTSTRAP_CLEANUP_UNVERIFIED");
    }

    public override string ToString() => "[local bootstrap session redacted]";
}

internal interface ILocalOpenBaoSession : IAsyncDisposable
{
    OpenBaoSecretStore Secrets { get; }
}
