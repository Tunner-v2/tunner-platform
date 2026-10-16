using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tunner.Platform.Configuration;

namespace Tunner.Platform.Secrets;

/// <summary>Explicit OpenBao KV-v2 adapter. Caller supplies a short-lived scoped token out of band.</summary>
public sealed class OpenBaoSecretStore : ISecretStore, IDisposable
{
    private readonly HttpClient client;
    private readonly string storeReference;
    private readonly string token;

    public OpenBaoSecretStore(Uri endpoint, string storeReference, string token)
        : this(endpoint, storeReference, token, new HttpClientHandler { AllowAutoRedirect = false }) { }

    internal OpenBaoSecretStore(Uri endpoint, string storeReference, string token, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(handler);
        ValidateEndpoint(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeReference);
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl))
            throw new ArgumentException("SECRET_BOOTSTRAP_INVALID", nameof(token));
        this.storeReference = storeReference;
        this.token = token;
        client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };
    }

    public static void ValidateEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)) ||
            endpoint.AbsolutePath != "/" || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("SECRET_ENDPOINT_INVALID", nameof(endpoint));
    }

    public static void ValidateResourceReference(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource)) throw new ArgumentException("SECRET_REFERENCE_INVALID", nameof(resource));
        var parts = resource.Split('/');
        if (parts.Length < 3 || parts[1] != "data" ||
            parts.Any(p => p.Length == 0 || p is "." or ".." || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')))
            throw new ArgumentException("SECRET_REFERENCE_INVALID", nameof(resource));
    }

    public async Task<SecretValues> ReadAsync(EngineeringSecretReference reference, CancellationToken cancellationToken = default)
        => await ReadIfPresentAsync(reference, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("SECRET_REQUEST_REJECTED");

    /// <summary>Only a missing credential resource returns null; authentication and authorization failures still fail closed.</summary>
    public async Task<SecretValues?> ReadIfPresentAsync(EngineeringSecretReference reference, CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        await ValidateTokenAsync(cancellationToken).ConfigureAwait(false);
        using var document = await SendOptionalAsync(HttpMethod.Get, "v1/" + reference.ResourceReference, null, allowNotFound: true, cancellationToken).ConfigureAwait(false);
        if (document is null) return null;
        try
        {
            var data = document.RootElement.GetProperty("data").GetProperty("data");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in data.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || !values.TryAdd(property.Name, property.Value.GetString()!))
                    throw new InvalidOperationException("SECRET_RESPONSE_INVALID");
            }
            return new SecretValues(values);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("SECRET_RESPONSE_INVALID");
        }
    }

    /// <summary>Creates only an absent KV-v2 record; CAS=0 never overwrites an existing version.</summary>
    public async Task CreateAsync(EngineeringSecretReference reference, IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 || values.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
            throw new ArgumentException("SECRET_INPUT_INVALID", nameof(values));
        await ValidateTokenAsync(cancellationToken).ConfigureAwait(false);
        using var content = JsonContent.Create(new { options = new { cas = 0 }, data = values });
        using var document = await SendAsync(HttpMethod.Post, "v1/" + reference.ResourceReference, content, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateReference(EngineeringSecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!StringComparer.Ordinal.Equals(storeReference, reference.StoreReference))
            throw new ArgumentException("SECRET_STORE_MISMATCH", nameof(reference));
        ValidateResourceReference(reference.ResourceReference);
    }

    private async Task ValidateTokenAsync(CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/auth/token/lookup-self", null, cancellationToken).ConfigureAwait(false);
        try
        {
            var data = document.RootElement.GetProperty("data");
            if (data.GetProperty("ttl").GetInt64() <= 0 ||
                data.GetProperty("policies").EnumerateArray().Any(p => p.GetString() == "root"))
                throw new InvalidOperationException("SECRET_BOOTSTRAP_REQUIRES_SCOPED_EXPIRING_TOKEN");
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException("SECRET_RESPONSE_INVALID");
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
        => await SendOptionalAsync(method, path, content, allowNotFound: false, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("SECRET_REQUEST_REJECTED");

    private async Task<JsonDocument?> SendOptionalAsync(HttpMethod method, string path, HttpContent? content,
        bool allowNotFound, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            request.Headers.Add("X-Vault-Token", token);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("SECRET_REQUEST_REJECTED");
            return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("SECRET_REQUEST_TIMEOUT");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new InvalidOperationException("SECRET_REQUEST_FAILED");
        }
    }

    public void Dispose() => client.Dispose();
    public override string ToString() => "[OpenBao secret store]";
}
