using Tunner.Platform.Secrets;

namespace Tunner.Identity.Persistence;

/// <summary>Owner-scoped immutable credential adapter over the existing scoped OpenBao transport.</summary>
public sealed class OpenBaoTotpSeedStore : ITotpSeedStore
{
    private readonly OpenBaoSecretStore transport;
    private readonly string storeReference;
    private readonly string prefix;
    private readonly string bindingVersion;
    public OpenBaoTotpSeedStore(OpenBaoSecretStore transport, string storeReference, string resourcePrefix, string bindingVersion)
    {
        ArgumentNullException.ThrowIfNull(transport); ArgumentException.ThrowIfNullOrWhiteSpace(storeReference); ArgumentException.ThrowIfNullOrWhiteSpace(bindingVersion);
        OpenBaoSecretStore.ValidateResourceReference(resourcePrefix + "/credential");
        this.transport = transport; this.storeReference = storeReference; prefix = resourcePrefix; this.bindingVersion = bindingVersion;
    }
    public TotpSeedReference Allocate(TotpSeedBinding binding)
    {
        Validate(binding); return new(storeReference, prefix + "/" + binding.AuthenticatorReference.ToString("N") + "/" + binding.Generation.ToString("N"), 1);
    }
    public Task CreateOnceAsync(TotpSeedBinding binding, TotpSeedReference reference, string seed, CancellationToken cancellationToken)
    {
        Require(binding, reference); ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        return transport.CreateCredentialVersionAsync(Convert(reference), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["account"] = binding.AccountId, ["authenticator"] = binding.AuthenticatorReference.ToString("N"), ["generation"] = binding.Generation.ToString("N"),
            ["enrollment"] = binding.EnrollmentId.ToString("N"), ["binding"] = binding.BindingVersion, ["seed"] = seed
        }, cancellationToken);
    }
    public async Task<TotpSeedValue?> ReadAsync(TotpSeedBinding binding, TotpSeedReference reference, CancellationToken cancellationToken)
    {
        Require(binding, reference);
        using var values = await transport.ReadCredentialVersionAsync(Convert(reference), cancellationToken).ConfigureAwait(false);
        if (values is null) return null;
        if (values.GetRequired("account") != binding.AccountId || values.GetRequired("authenticator") != binding.AuthenticatorReference.ToString("N") ||
            values.GetRequired("generation") != binding.Generation.ToString("N") || values.GetRequired("enrollment") != binding.EnrollmentId.ToString("N") || values.GetRequired("binding") != binding.BindingVersion)
            throw new InvalidOperationException("SECRET_OWNER_BINDING_MISMATCH");
        return new(values.GetRequired("seed"));
    }
    private void Require(TotpSeedBinding binding, TotpSeedReference reference)
    {
        if (reference != Allocate(binding)) throw new InvalidOperationException("SECRET_OWNER_BINDING_MISMATCH");
    }
    private void Validate(TotpSeedBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(binding.AccountId) || binding.AuthenticatorReference == Guid.Empty || binding.Generation == Guid.Empty ||
            binding.EnrollmentId == Guid.Empty || binding.BindingVersion != bindingVersion) throw new InvalidOperationException("SECRET_OWNER_BINDING_MISMATCH");
    }
    private static CredentialSecretReference Convert(TotpSeedReference reference) => new(reference.StoreReference, reference.ResourceReference, reference.Version);
}
