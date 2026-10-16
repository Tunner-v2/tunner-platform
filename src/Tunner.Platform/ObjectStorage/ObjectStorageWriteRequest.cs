namespace Tunner.Platform.ObjectStorage;

public sealed record ObjectStorageWriteRequest
{
    public ObjectStorageWriteRequest(ObjectStorageAddress address, Stream content)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Content = content ?? throw new ArgumentNullException(nameof(content));

        if (!Content.CanRead)
        {
            throw new ArgumentException("Object-storage content must be readable.", nameof(content));
        }
    }

    public ObjectStorageAddress Address { get; }

    public Stream Content { get; }
}
