using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Family-owned transport/persistence values. Native policy serialization and crypto admission remain unchanged.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecoveryCodeConfigurationCrypto(string Version, string NativeIdentityVersion, int CodesPerSet, int MaximumCodesPerSet,
    int HashIterations, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long MaximumHashWork,
    int MaximumGenerationAttempts, int MaximumConcurrentWork, TimeSpan MaximumWorkDuration)
{
    public RecoveryCodeCryptoProfile ToProfile() => new(Version, NativeIdentityVersion, CodesPerSet, MaximumCodesPerSet,
        HashIterations, MaximumHashWork, MaximumGenerationAttempts, MaximumConcurrentWork, MaximumWorkDuration);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecoveryCodeConfigurationValues(string Version, string Origin, RecoveryCodeConfigurationCrypto Crypto,
    string FormatProfileVersion, string DeliveryProfileVersion, TimeSpan OperationLifetime, TimeSpan ResultRecoveryLifetime,
    int MaximumActiveSets, int MaximumPendingOperations, RecoveryCodeGenerationAdmission GenerationAdmission,
    RecoveryCodeReplacementMechanism ReplacementMechanism, RecoveryCodeSessionEffect SessionEffect,
    RecoveryCodeDeliveryMechanism DeliveryMechanism, string GenerateAssurancePolicyVersion, string ReplaceAssurancePolicyVersion,
    int ConsumptionAccountAttemptLimit, int ConsumptionNetworkAttemptLimit, string ProcessingReference, string RetentionReference)
{
    public RecoveryCodePolicy ToPolicy()
    {
        if (Crypto is null) throw new ArgumentException("An explicit recovery-code crypto profile is required.");
        var policy = new RecoveryCodePolicy(Version, Origin, Crypto.ToProfile(), FormatProfileVersion, DeliveryProfileVersion,
            OperationLifetime, ResultRecoveryLifetime, MaximumActiveSets, MaximumPendingOperations, GenerationAdmission,
            ReplacementMechanism, SessionEffect, DeliveryMechanism, GenerateAssurancePolicyVersion, ReplaceAssurancePolicyVersion,
            ConsumptionAccountAttemptLimit, ConsumptionNetworkAttemptLimit, ProcessingReference, RetentionReference);
        policy.Validate(); return policy;
    }
}
public sealed record RecoveryCodeConfigurationVersion(PolicyVersionCandidate<string> Metadata, RecoveryCodeConfigurationValues Values);
