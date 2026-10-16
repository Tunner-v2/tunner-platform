using Tunner.Platform.Configuration;

namespace Tunner.Platform.Secrets;

/// <summary>Reads opaque operator-owned references; authorization remains with the secret provider.</summary>
public interface ISecretStore
{
    Task<SecretValues> ReadAsync(EngineeringSecretReference reference, CancellationToken cancellationToken = default);
}

/// <summary>Secret values must never be logged, serialized or included in exception messages.</summary>
public sealed class SecretValues : IDisposable
{
    private Dictionary<string, string>? values;

    public SecretValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = new(values, StringComparer.Ordinal);
    }

    public string GetRequired(string field)
    {
        ObjectDisposedException.ThrowIf(values is null, this);
        if (!values.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("SECRET_FIELD_UNAVAILABLE");
        return value;
    }

    public void Dispose()
    {
        values?.Clear();
        values = null;
    }

    public override string ToString() => "[secret values redacted]";
}
