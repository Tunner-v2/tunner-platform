using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Tunner.Platform.Configuration;
using Tunner.Platform.Secrets;

namespace Tunner.Installer;

/// <summary>Non-secret installation input, supplied explicitly by the operator. No deployed defaults.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class R2InstallationProfile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    public int SchemaVersion { get; init; } = 1;
    public required string EnvironmentClass { get; init; }
    public required string EnvironmentReference { get; init; }
    public required string Endpoint { get; init; }
    public required string Bucket { get; init; }
    public required string SecretStoreReference { get; init; }
    public required string OpenBaoEndpoint { get; init; }
    public required string CredentialResourceReference { get; init; }
    public required string AccessKeyField { get; init; }
    public required string SecretKeyField { get; init; }
    public bool DedicatedNonProductionTestBucket { get; init; }

    public EngineeringSecretReference Validate()
    {
        if (SchemaVersion != 1) throw new InvalidOperationException("PROFILE_VERSION_UNSUPPORTED");
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" ||
            !endpoint.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
            endpoint.AbsolutePath != "/" || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || !endpoint.IsDefaultPort)
            throw new InvalidOperationException("R2_ENDPOINT_INVALID");
        if (string.IsNullOrEmpty(Bucket) || Bucket.Length is < 3 or > 63 ||
            !char.IsAsciiLetterOrDigit(Bucket[0]) || !char.IsAsciiLetterOrDigit(Bucket[^1]) ||
            Bucket.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-') ||
            string.IsNullOrWhiteSpace(AccessKeyField) || string.IsNullOrWhiteSpace(SecretKeyField) ||
            AccessKeyField == SecretKeyField || AccessKeyField.Any(char.IsControl) || SecretKeyField.Any(char.IsControl))
            throw new InvalidOperationException("R2_PROFILE_INVALID");
        if (!Uri.TryCreate(OpenBaoEndpoint, UriKind.Absolute, out var bao))
            throw new InvalidOperationException("SECRET_ENDPOINT_INVALID");
        OpenBaoSecretStore.ValidateEndpoint(bao);
        OpenBaoSecretStore.ValidateResourceReference(CredentialResourceReference);
        var prefix = EngineeringEnvironmentConfiguration.SectionName;
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [prefix + ":Class"] = EnvironmentClass,
            [prefix + ":Reference"] = EnvironmentReference,
            [prefix + ":Endpoints:R2"] = Endpoint,
            [prefix + ":SecretReferences:R2:StoreReference"] = SecretStoreReference,
            [prefix + ":SecretReferences:R2:ResourceReference"] = CredentialResourceReference
        });
        return EngineeringEnvironmentConfiguration.Load(configuration).SecretReferences["R2"];
    }

    public static async Task<R2InstallationProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > 65536) throw new InvalidOperationException("PROFILE_TOO_LARGE");
        var profile = await JsonSerializer.DeserializeAsync<R2InstallationProfile>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PROFILE_INVALID");
        profile.Validate();
        return profile;
    }

    public async Task SaveNewAsync(string path, CancellationToken cancellationToken = default)
    {
        Validate();
        // Same-directory staging avoids publishing partial JSON. Move never overwrites an existing profile.
        var fullPath = Path.GetFullPath(path);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, this, WriteOptions, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
