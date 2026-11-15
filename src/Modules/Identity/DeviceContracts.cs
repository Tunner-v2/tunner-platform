using System.Collections.ObjectModel;
using System.Text.Json;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Private, explicitly qualified collection profile. It never supplies authentication or a production default.</summary>
internal sealed class DeviceEvidencePolicy
{
    internal DeviceEvidencePolicy(PolicyVersionCandidate<string> metadata, string origin, string collectionReference,
        string inventoryReference, string processingReference, string retentionReference, string classification,
        string permissionReference, string eligibilityPolicyVersion, string revokeAssurancePolicyVersion,
        string preserveSessionsEvidenceReference, TimeSpan correlationLifetime, TimeSpan operationLifetime,
        TimeSpan resultLifetime, int maximumListingRows, int maximumPendingOperations,
        IReadOnlyDictionary<string, IReadOnlyList<string>> allowedCharacteristics)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(allowedCharacteristics);
        if (new[] { origin, collectionReference, inventoryReference, processingReference, retentionReference, classification,
                permissionReference, eligibilityPolicyVersion, revokeAssurancePolicyVersion, preserveSessionsEvidenceReference }.Any(string.IsNullOrWhiteSpace) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            new[] { correlationLifetime, operationLifetime, resultLifetime }.Any(x => x <= TimeSpan.Zero) ||
            maximumListingRows <= 0 || maximumListingRows == int.MaxValue || maximumPendingOperations <= 0 ||
            classification is not ("INTERNAL" or "CONFIDENTIAL" or "RESTRICTED_PERSONAL" or "RESTRICTED_SECURITY"))
            throw new ArgumentException("Explicit supported device collection, privacy, permission and security qualification is required.");
        var fields = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (name, values) in allowedCharacteristics)
        {
            // Values are published safe categories selected by the qualified collector, never raw headers, IPs or fingerprints.
            if (string.IsNullOrWhiteSpace(name) || values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("A complete safe characteristic vocabulary is required.", nameof(allowedCharacteristics));
            fields.Add(name, Array.AsReadOnly(values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }
        Metadata = metadata; Origin = origin; CollectionReference = collectionReference; InventoryReference = inventoryReference;
        ProcessingReference = processingReference; RetentionReference = retentionReference; Classification = classification;
        PermissionReference = permissionReference; EligibilityPolicyVersion = eligibilityPolicyVersion;
        RevokeAssurancePolicyVersion = revokeAssurancePolicyVersion; PreserveSessionsEvidenceReference = preserveSessionsEvidenceReference;
        CorrelationLifetime = correlationLifetime; OperationLifetime = operationLifetime; ResultLifetime = resultLifetime;
        MaximumListingRows = maximumListingRows; MaximumPendingOperations = maximumPendingOperations;
        AllowedCharacteristics = new ReadOnlyDictionary<string, IReadOnlyList<string>>(fields);
        Snapshot = JsonSerializer.Serialize(new { Metadata, Origin, CollectionReference, InventoryReference, ProcessingReference,
            RetentionReference, Classification, PermissionReference, EligibilityPolicyVersion, RevokeAssurancePolicyVersion,
            PreserveSessionsEvidenceReference, CorrelationLifetime, OperationLifetime, ResultLifetime, MaximumListingRows,
            MaximumPendingOperations, AllowedCharacteristics });
    }
    internal PolicyVersionCandidate<string> Metadata { get; }
    internal string Origin { get; }
    internal string CollectionReference { get; }
    internal string InventoryReference { get; }
    internal string ProcessingReference { get; }
    internal string RetentionReference { get; }
    internal string Classification { get; }
    internal string PermissionReference { get; }
    internal string EligibilityPolicyVersion { get; }
    internal string RevokeAssurancePolicyVersion { get; }
    internal string PreserveSessionsEvidenceReference { get; }
    internal TimeSpan CorrelationLifetime { get; }
    internal TimeSpan OperationLifetime { get; }
    internal TimeSpan ResultLifetime { get; }
    internal int MaximumListingRows { get; }
    internal int MaximumPendingOperations { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedCharacteristics { get; }
    internal string Snapshot { get; }
    internal void Require(string origin, DateTimeOffset at)
    {
        if (origin != Origin || Metadata.Scope != origin || Metadata.Status != "ACTIVE" ||
            string.IsNullOrWhiteSpace(Metadata.VersionId) || !Metadata.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Current origin-scoped device qualification is unavailable.");
    }
    internal string Characteristics(IReadOnlyDictionary<string, string> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (observed.Any(x => !AllowedCharacteristics.TryGetValue(x.Key, out var values) || !values.Contains(x.Value, StringComparer.Ordinal)))
            throw new InvalidOperationException("Device evidence contains an unqualified characteristic.");
        return JsonSerializer.Serialize(new SortedDictionary<string, string>(observed.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal));
    }
    public override string ToString() => nameof(DeviceEvidencePolicy);
}

/// <summary>Pure local authority sources; none may perform network I/O or infer approval from browser claims.</summary>
internal interface IDeviceEvidencePolicySource { DeviceEvidencePolicy RequireCurrent(string origin, DateTimeOffset at); }
internal interface IDeviceSafeCharacteristicSource
{
    IReadOnlyDictionary<string, string> Observe(Microsoft.AspNetCore.Http.HttpContext context, DeviceEvidencePolicy policy);
}
internal sealed record DeviceTrustEligibilityFacts(string AccountId, long AccountVersion, string Origin, Guid DeviceId,
    long DeviceVersion, long PriorTrustVersion, string? PriorTrustState, Guid AuthenticationSessionId,
    string AuthenticationMethods, string Assurance, DateTimeOffset AuthenticatedAt, string CharacteristicsDigest, string PolicyVersion)
{
    internal string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    public override string ToString() => nameof(DeviceTrustEligibilityFacts);
}
internal sealed record DeviceTrustEligibilityDecision(string FactsDigest, bool Allowed, string PolicyVersion,
    string EvidenceReference, EffectivePolicyInterval EffectiveInterval)
{
    public override string ToString() => nameof(DeviceTrustEligibilityDecision);
}
internal interface IDeviceTrustEligibilitySource
{
    DeviceTrustEligibilityDecision RequireCurrent(DeviceTrustEligibilityFacts facts, DateTimeOffset at);
}

internal sealed record DeviceListingContinuation(string AccountId, Guid ActorSessionId, string Origin, DateTimeOffset CreatedAt, Guid DeviceId)
{
    public override string ToString() => nameof(DeviceListingContinuation);
}
internal sealed record DeviceListingItem(Guid DeviceReference, long Version, long TrustVersion, bool ObservedForCurrentSession,
    DateTimeOffset FirstObservedAt, DateTimeOffset LastObservedAt, IReadOnlyDictionary<string, string> Characteristics,
    string RecordedTrustState, string EffectiveTrustState, DateTimeOffset EvaluatedAt)
{
    public override string ToString() => nameof(DeviceListingItem);
}
internal sealed record DeviceListingPage(IReadOnlyList<DeviceListingItem> Items, bool HasMore, DeviceListingContinuation? Next)
{
    public override string ToString() => nameof(DeviceListingPage);
}
internal sealed record DeviceTrustRemovalRequest(Guid RequestId, Guid DeviceId, long ExpectedDeviceVersion,
    long ExpectedTrustVersion, long ExpectedAccountVersion, string CorrelationId)
{
    internal void Validate()
    {
        if (RequestId == Guid.Empty || DeviceId == Guid.Empty || ExpectedDeviceVersion < 1 || ExpectedTrustVersion < 1 ||
            ExpectedTrustVersion == long.MaxValue || ExpectedAccountVersion < 1 || string.IsNullOrWhiteSpace(CorrelationId))
            throw new ArgumentException("An exact original device, trust, Account and operation identity is required.");
    }
    public override string ToString() => nameof(DeviceTrustRemovalRequest);
}
internal sealed record DeviceTrustRemovalReceipt(Guid DeviceReference, long DeviceVersion, long PriorTrustVersion,
    long RevokedTrustVersion, string OperationId, string CorrelationId, DateTimeOffset RevokedAt)
{
    public override string ToString() => nameof(DeviceTrustRemovalReceipt);
}
internal sealed record DeviceTrustRemovalResult(string? ErrorCode, DeviceTrustRemovalReceipt? Receipt = null)
{
    internal bool Applied => Receipt is not null;
    public override string ToString() => nameof(DeviceTrustRemovalResult);
}
