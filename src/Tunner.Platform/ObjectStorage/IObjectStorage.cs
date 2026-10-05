namespace Tunner.Platform.ObjectStorage;

public interface IObjectStorage
{
    Task PutAsync(ObjectStorageWriteRequest request, CancellationToken cancellationToken = default);

    Task<ObjectStorageReadResult> OpenReadAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default);

    Task DeleteAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default);
}
