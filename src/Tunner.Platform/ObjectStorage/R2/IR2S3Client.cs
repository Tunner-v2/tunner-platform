namespace Tunner.Platform.ObjectStorage.R2;

public interface IR2S3Client
{
    Task PutAsync(ObjectStorageWriteRequest request, CancellationToken cancellationToken = default);

    Task<ObjectStorageReadResult> OpenReadAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default);

    Task DeleteAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default);
}
