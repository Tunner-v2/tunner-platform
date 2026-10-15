using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Tunner.Platform.ObjectStorage;
using Tunner.Platform.ObjectStorage.R2;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class AwsR2S3ClientTests
{
    [Fact]
    public async Task TranslatesOpaqueAddressesAndR2UploadFlagsAtTheSdkBoundary()
    {
        using var sdk = new RecordingS3Client();
        var client = new AwsR2S3Client(sdk);
        var address = new ObjectStorageAddress("fixture-bucket", "opaque/a b");
        using var content = new MemoryStream([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        await client.PutAsync(new ObjectStorageWriteRequest(address, content), cancellation.Token);
        Assert.Equal(address.Bucket, sdk.Put!.BucketName);
        Assert.Equal(address.Key, sdk.Put.Key);
        Assert.Same(content, sdk.Put.InputStream);
        Assert.True(sdk.Put.DisablePayloadSigning);
        Assert.True(sdk.Put.DisableDefaultChecksumValidation);
        Assert.Equal(cancellation.Token, sdk.Token);
        await client.DeleteAsync(address, cancellation.Token);
        Assert.Equal(address.Bucket, sdk.Delete!.BucketName);
        Assert.Equal(address.Key, sdk.Delete.Key);
        Assert.Equal(cancellation.Token, sdk.Token);
    }

    [Fact]
    public async Task ReadOwnsSdkResponseAndReleasesItsStreamExactlyOnce()
    {
        using var sdk = new RecordingS3Client();
        var client = new AwsR2S3Client(sdk);
        using var cancellation = new CancellationTokenSource();
        var result = await client.OpenReadAsync(new ObjectStorageAddress("fixture-bucket", "read-key"), cancellation.Token);
        Assert.Equal("fixture-bucket", sdk.Get!.BucketName);
        Assert.Equal("read-key", sdk.Get.Key);
        Assert.Equal(cancellation.Token, sdk.Token);
        Assert.Same(sdk.ReadStream, result.Content);
        Assert.Equal(42, result.Content.ReadByte());
        result.Dispose();
        result.Dispose();
        Assert.Equal(1, sdk.ReadStream.DisposeCount);
    }

    [Fact]
    public async Task PreCancelledOperationsNeverReachTheSdk()
    {
        using var sdk = new RecordingS3Client();
        var client = new AwsR2S3Client(sdk);
        var address = new ObjectStorageAddress("fixture-bucket", "cancelled");
        using var content = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PutAsync(new ObjectStorageWriteRequest(address, content), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.OpenReadAsync(address, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DeleteAsync(address, cancellation.Token));
        Assert.Null(sdk.Put);
        Assert.Null(sdk.Get);
        Assert.Null(sdk.Delete);
    }

    private sealed class RecordingS3Client() : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public PutObjectRequest? Put { get; private set; }
        public GetObjectRequest? Get { get; private set; }
        public DeleteObjectRequest? Delete { get; private set; }
        public CancellationToken Token { get; private set; }
        public TrackedStream ReadStream { get; } = new();

        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Put = request;
            Token = cancellationToken;
            return Task.FromResult(new PutObjectResponse());
        }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            Get = request;
            Token = cancellationToken;
            return Task.FromResult(new GetObjectResponse { ResponseStream = ReadStream });
        }
        public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
        {
            Delete = request;
            Token = cancellationToken;
            return Task.FromResult(new DeleteObjectResponse());
        }
    }

    private sealed class TrackedStream() : MemoryStream([42])
    {
        public int DisposeCount { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
