using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Tunner.Platform.ObjectStorage;
using Tunner.Platform.ObjectStorage.R2;
using Tunner.Platform.Secrets;

namespace Tunner.Installer;

/// <summary>Owned SDK lifetime, created only after the provider-neutral secret store resolves credentials.</summary>
public sealed class ConfiguredR2Storage : IDisposable
{
    private readonly IAmazonS3 client;
    private ConfiguredR2Storage(IAmazonS3 client)
    {
        this.client = client;
        Storage = new R2ObjectStorage(new AwsR2S3Client(client));
    }

    public IObjectStorage Storage { get; }

    public static async Task<ConfiguredR2Storage> CreateAsync(R2InstallationProfile profile, ISecretStore secrets,
        CancellationToken cancellationToken = default)
        => await CreateAsync(profile, secrets, static (credentials, config) => new AmazonS3Client(credentials, config), cancellationToken).ConfigureAwait(false);

    internal static async Task<ConfiguredR2Storage> CreateAsync(R2InstallationProfile profile, ISecretStore secrets,
        Func<AWSCredentials, AmazonS3Config, IAmazonS3> createClient, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(createClient);
        cancellationToken.ThrowIfCancellationRequested();
        var reference = profile.Validate();
        using var values = await secrets.ReadAsync(reference, cancellationToken).ConfigureAwait(false);
        var credentials = new BasicAWSCredentials(values.GetRequired(profile.AccessKeyField), values.GetRequired(profile.SecretKeyField));
        var client = createClient(credentials, new AmazonS3Config
        {
            ServiceURL = profile.Endpoint, AuthenticationRegion = "auto", ForcePathStyle = true,
            Timeout = TimeSpan.FromSeconds(30), MaxErrorRetry = 1
        });
        return new ConfiguredR2Storage(client);
    }

    public async Task<bool> IsAbsentAsync(ObjectStorageAddress address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        try
        {
            _ = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = address.Bucket, Key = address.Key
            }, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound) { return true; }
    }

    public void Dispose() => client.Dispose();
}
