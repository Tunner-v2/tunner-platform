namespace Tunner.Platform.ObjectStorage;

public sealed record ObjectStorageAddress
{
    public ObjectStorageAddress(string bucket, string key)
    {
        Bucket = RequireValue(bucket, nameof(bucket));
        Key = RequireValue(key, nameof(key));
    }

    public string Bucket { get; }

    public string Key { get; }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An object-storage address value is required.", parameterName);
        }

        return value;
    }
}
