using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit owner inputs for the single supported native crypto shape. No production defaults.</summary>
public sealed class RecoveryCodeCryptoProfile
{
    public RecoveryCodeCryptoProfile(string version, string nativeIdentityVersion, int codesPerSet, int maximumCodesPerSet,
        int hashIterations, long maximumHashWork, int maximumGenerationAttempts, int maximumConcurrentWork,
        TimeSpan maximumWorkDuration)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 256 || version.Any(char.IsControl) || nativeIdentityVersion is not ("10.0.13" or "10.0.15") || codesPerSet <= 0 ||
            maximumCodesPerSet < codesPerSet || hashIterations <= 0 || maximumHashWork <= 0 ||
            (long)maximumCodesPerSet * hashIterations > maximumHashWork || maximumGenerationAttempts < codesPerSet ||
            maximumConcurrentWork <= 0 || maximumWorkDuration <= TimeSpan.Zero)
            throw new ArgumentException("A complete supported recovery-code crypto/work profile is required.");
        Version = version; NativeIdentityVersion = nativeIdentityVersion; CodesPerSet = codesPerSet;
        MaximumCodesPerSet = maximumCodesPerSet; HashIterations = hashIterations; MaximumHashWork = maximumHashWork;
        MaximumGenerationAttempts = maximumGenerationAttempts; MaximumConcurrentWork = maximumConcurrentWork;
        MaximumWorkDuration = maximumWorkDuration;
    }
    public string Version { get; }
    public string NativeIdentityVersion { get; }
    public int CodesPerSet { get; }
    public int MaximumCodesPerSet { get; }
    public int HashIterations { get; }
    public long MaximumHashWork { get; }
    public int MaximumGenerationAttempts { get; }
    public int MaximumConcurrentWork { get; }
    public TimeSpan MaximumWorkDuration { get; }
    public override string ToString() => nameof(RecoveryCodeCryptoProfile);
}

/// <summary>The immutable set reference is the recovery-code generation boundary.</summary>
internal sealed record RecoveryCodeVerifierBinding(string AccountId, Guid SetReference, Guid EntryReference, string ProfileVersion)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountId) || SetReference == Guid.Empty || EntryReference == Guid.Empty || string.IsNullOrWhiteSpace(ProfileVersion))
            throw new ArgumentException("A current recovery-code owner binding is required.");
    }
    public override string ToString() => nameof(RecoveryCodeVerifierBinding);
}

/// <summary>Bounded transient value. It is never a command log, record, storage value or general JSON DTO.</summary>
internal sealed class RecoveryCodeValue : IDisposable
{
    private char[]? value;
    internal RecoveryCodeValue(char[] value) => this.value = value;
    [JsonIgnore] internal ReadOnlySpan<char> Span => value ?? throw new ObjectDisposedException(nameof(RecoveryCodeValue));
    internal string NativeInput() => new(Span);
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref value, null);
        if (owned is not null) Array.Clear(owned);
    }
    public override string ToString() => nameof(RecoveryCodeValue);
}

internal sealed class RecoveryCodePreparedEntry(Guid reference, string verifier, RecoveryCodeValue code) : IDisposable
{
    internal Guid Reference { get; } = reference;
    [JsonIgnore] internal string Verifier { get; } = verifier;
    [JsonIgnore] internal RecoveryCodeValue Code { get; } = code;
    public void Dispose() => Code.Dispose();
    public override string ToString() => nameof(RecoveryCodePreparedEntry);
}

internal sealed class RecoveryCodePreparedSet(IEnumerable<RecoveryCodePreparedEntry> entries) : IDisposable
{
    private readonly ReadOnlyCollection<RecoveryCodePreparedEntry> entries = Array.AsReadOnly(entries.ToArray());
    [JsonIgnore] internal IReadOnlyList<RecoveryCodePreparedEntry> Entries => entries;
    public void Dispose() { foreach (var entry in entries) entry.Dispose(); }
    public override string ToString() => nameof(RecoveryCodePreparedSet);
}

internal sealed class RecoveryCodeVerifierCandidate(RecoveryCodeVerifierBinding binding, string verifier)
{
    internal RecoveryCodeVerifierBinding Binding { get; } = binding;
    [JsonIgnore] internal string Verifier { get; } = verifier;
    public override string ToString() => nameof(RecoveryCodeVerifierCandidate);
}
