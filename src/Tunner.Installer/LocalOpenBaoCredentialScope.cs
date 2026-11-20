using Tunner.Platform.Secrets;

namespace Tunner.Installer;

/// <summary>Trusted coordinator input for one finite local credential namespace; never browser input.</summary>
public sealed class LocalOpenBaoCredentialScope
{
    public LocalOpenBaoCredentialScope(string environmentClass, Uri endpoint, string storeReference,
        string resourcePrefix, Guid instanceId)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        OpenBaoSecretStore.ValidateEndpoint(endpoint);
        if (environmentClass is not ("LOCAL_DEVELOPMENT" or "EPHEMERAL_TEST") || !endpoint.IsLoopback)
            throw new InvalidOperationException("LOCAL_LOOPBACK_BOOTSTRAP_REQUIRED");
        if (string.IsNullOrWhiteSpace(storeReference) || storeReference.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(resourcePrefix) || resourcePrefix.Length > 256 ||
            resourcePrefix.Split('/').Length < 3 || instanceId == Guid.Empty)
            throw new ArgumentException("LOCAL_CREDENTIAL_SCOPE_INVALID");
        OpenBaoSecretStore.ValidateResourceReference(resourcePrefix);
        Endpoint = endpoint;
        StoreReference = storeReference;
        ResourcePrefix = resourcePrefix + "/" + instanceId.ToString("N");
        InstanceId = instanceId;
    }

    public Uri Endpoint { get; }
    public string StoreReference { get; }
    public string ResourcePrefix { get; }
    public Guid InstanceId { get; }
    internal string PolicyResource => ResourcePrefix + "/*";
    public override string ToString() => nameof(LocalOpenBaoCredentialScope);
}
