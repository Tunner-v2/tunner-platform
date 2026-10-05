using Amazon.S3;
using Amazon.S3.Model;

namespace Tunner.Platform.ObjectStorage.R2;

public sealed class AwsR2S3Client : IR2S3Client
{
    private readonly IAmazonS3 client;

    public AwsR2S3Client(IAmazonS3 client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task PutAsync(ObjectStorageWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = request.Address.Bucket,
            Key = request.Address.Key,
            InputStream = request.Content,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ObjectStorageReadResult> OpenReadAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        cancellationToken.ThrowIfCancellationRequested();

        var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = address.Bucket,
            Key = address.Key,
        }, cancellationToken).ConfigureAwait(false);

        return new ObjectStorageReadResult(response.ResponseStream, response.Dispose);
    }

    public async Task DeleteAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        cancellationToken.ThrowIfCancellationRequested();

        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = address.Bucket,
            Key = address.Key,
        }, cancellationToken).ConfigureAwait(false);
    }
}
