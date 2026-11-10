namespace Tunner.Platform.Secrets;

/// <summary>Trusted per-credential immutable KV version, separate from deploy-time engineering configuration.</summary>
public sealed record CredentialSecretReference(string StoreReference, string ResourceReference, long Version)
{
    public override string ToString() => nameof(CredentialSecretReference);
}
