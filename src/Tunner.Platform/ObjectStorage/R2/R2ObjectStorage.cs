namespace Tunner.Platform.ObjectStorage.R2;

public sealed class R2ObjectStorage : IObjectStorage
{
    private readonly IR2S3Client client;

    public R2ObjectStorage(IR2S3Client client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public Task PutAsync(ObjectStorageWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return client.PutAsync(request, cancellationToken);
    }

    public Task<ObjectStorageReadResult> OpenReadAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        cancellationToken.ThrowIfCancellationRequested();
        return client.OpenReadAsync(address, cancellationToken);
    }

    public Task DeleteAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        cancellationToken.ThrowIfCancellationRequested();
        return client.DeleteAsync(address, cancellationToken);
    }
}
