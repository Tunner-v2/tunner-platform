using System.Text.Json.Serialization;

namespace Tunner.Identity.Persistence;

public sealed record RecoveryCasePasskeySelection(Guid CredentialReference, long Version, string ProfileVersion)
{
    public override string ToString() => nameof(RecoveryCasePasskeySelection);
}
/// <summary>Optional approved native method mapping. No selection, attestation trust, or production fallback is supplied.</summary>
public sealed record RecoveryPasswordPasskeyProfile(string Version, string NativePolicyVersion, string RequiredAssurance,
    IReadOnlyDictionary<string, string> MethodAssurances)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(MethodAssurances);
        var nativeMethods = new[] { PasskeyPolicy.MethodKey(false, false), PasskeyPolicy.MethodKey(true, false), PasskeyPolicy.MethodKey(true, true) };
        if (new[] { Version, NativePolicyVersion, RequiredAssurance }.Any(string.IsNullOrWhiteSpace) ||
            RequiredAssurance.Equals("AAL3", StringComparison.OrdinalIgnoreCase) || MethodAssurances.Count == 0 ||
            MethodAssurances.Any(x => !nativeMethods.Contains(x.Key, StringComparer.Ordinal) || x.Value != RequiredAssurance))
            throw new ArgumentException("Explicit supported native recovery method qualification is required.");
    }
    internal bool Admits(string method, string nativeAssurance) => MethodAssurances.TryGetValue(method, out var mapped) &&
        mapped == RequiredAssurance && nativeAssurance == mapped && !mapped.Equals("AAL3", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => nameof(RecoveryPasswordPasskeyProfile);
}
internal sealed record RecoveryPasskeyOptionsResult(string? ErrorCode, RecoveryEntryRow? Entry = null, PasskeyCeremonyRow? Ceremony = null,
    RecoveryPasskeyPresentation? Presentation = null)
{
    public override string ToString() => nameof(RecoveryPasskeyOptionsResult);
}
internal sealed record RecoveryPasskeyPresentation(Guid CeremonyId, string OptionsJson, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(RecoveryPasskeyPresentation);
}
internal sealed record RecoveryPasskeyCapability(bool Available,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaximumCredentialJsonBytes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class RecoveryPasswordPasskeyRepairCommand
{
    public required Guid RequestId { get; init; }
    public required long ExpectedVersion { get; init; }
    public required Guid CeremonyId { get; init; }
    public required System.Text.Json.JsonElement Credential { get; init; }
    public required string? Password { get; init; }
    public required string? PasswordConfirmation { get; init; }
    public override string ToString() => nameof(RecoveryPasswordPasskeyRepairCommand);
}
