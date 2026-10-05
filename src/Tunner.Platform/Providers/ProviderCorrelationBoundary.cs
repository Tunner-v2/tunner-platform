using System.Collections.ObjectModel;
using Tunner.Platform.Operations;

namespace Tunner.Platform.Providers;

/// <summary>Exact internal selection evidence supplied by the owner, not routing or authorization.</summary>
public sealed record ProviderBindingReference
{
    public ProviderBindingReference(string providerInstanceId, string providerAccountId, string environmentReference, string contractReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractReference);
        ProviderInstanceId = providerInstanceId;
        ProviderAccountId = providerAccountId;
        EnvironmentReference = environmentReference;
        ContractReference = contractReference;
    }

    public string ProviderInstanceId { get; }
    public string ProviderAccountId { get; }
    public string EnvironmentReference { get; }
    public string ContractReference { get; }
}

/// <summary>Immutable trace identity for a mutating provider request; does not authorize submission.</summary>
public sealed class ProviderOperationContext
{
    private ProviderOperationContext(string operationId, string correlationId, string idempotencyKey,
        DateTimeOffset requestedAt, IdempotencyScope ownerScope, ProviderBindingReference binding)
    {
        OperationId = operationId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        RequestedAt = requestedAt.ToUniversalTime();
        OwnerScope = ownerScope;
        Binding = binding;
    }

    public string OperationId { get; }
    public string CorrelationId { get; }
    public string IdempotencyKey { get; }
    public DateTimeOffset RequestedAt { get; }
    public IdempotencyScope OwnerScope { get; }
    public ProviderBindingReference Binding { get; }

    public static ProviderOperationContext FromCommand<TPayload>(TechnicalCommandEnvelope<TPayload> command,
        IdempotencyScope ownerScope, ProviderBindingReference binding)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(ownerScope);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerScope.Value);
        if (command.RequestedAt == default)
            throw new ArgumentException("A request timestamp is required.", nameof(command));
        return new(command.OperationId, command.CorrelationId, command.IdempotencyKey, command.RequestedAt,
            new IdempotencyScope(ownerScope.Value), binding);
    }
}

/// <summary>The owner fingerprints every effect-relevant input and durably reserves the identity atomically.</summary>
public sealed class ProviderIdempotencyBinding
{
    public ProviderIdempotencyBinding(ProviderOperationContext operation, string requestFingerprint, string providerIdempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        Operation = operation;
        ArgumentException.ThrowIfNullOrWhiteSpace(providerIdempotencyKey);
        RequestFingerprint = requestFingerprint;
        ProviderIdempotencyKey = providerIdempotencyKey;
    }

    public ProviderOperationContext Operation { get; }
    public string RequestFingerprint { get; }
    public string ProviderIdempotencyKey { get; }
}

public enum ProviderIdempotencyComparison
{
    DifferentIdentity,
    EquivalentRequest,
    ConflictingRequest
}

/// <summary>Pure comparison of owner-persisted bindings. No reservation, retry, failover or side effect.</summary>
public static class ProviderIdempotencyGuard
{
    public static ProviderIdempotencyComparison Compare(ProviderIdempotencyBinding previous, ProviderIdempotencyBinding current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var first = previous.Operation;
        var second = current.Operation;
        if (!StringComparer.Ordinal.Equals(first.Binding.ProviderInstanceId, second.Binding.ProviderInstanceId)
            || !StringComparer.Ordinal.Equals(first.Binding.ProviderAccountId, second.Binding.ProviderAccountId)
            || !StringComparer.Ordinal.Equals(first.Binding.EnvironmentReference, second.Binding.EnvironmentReference))
            return ProviderIdempotencyComparison.DifferentIdentity;

        var sameOwnerIdentity = StringComparer.Ordinal.Equals(first.OwnerScope.Value, second.OwnerScope.Value)
            && StringComparer.Ordinal.Equals(first.IdempotencyKey, second.IdempotencyKey);
        var sameProviderKey = StringComparer.Ordinal.Equals(previous.ProviderIdempotencyKey, current.ProviderIdempotencyKey);
        if (!sameOwnerIdentity && !sameProviderKey) return ProviderIdempotencyComparison.DifferentIdentity;
        if (!sameOwnerIdentity || !sameProviderKey) return ProviderIdempotencyComparison.ConflictingRequest;

        return StringComparer.Ordinal.Equals(first.OperationId, second.OperationId)
            && StringComparer.Ordinal.Equals(first.CorrelationId, second.CorrelationId)
            && StringComparer.Ordinal.Equals(first.Binding.ContractReference, second.Binding.ContractReference)
            && StringComparer.Ordinal.Equals(previous.RequestFingerprint, current.RequestFingerprint)
            ? ProviderIdempotencyComparison.EquivalentRequest
            : ProviderIdempotencyComparison.ConflictingRequest;
    }
}

/// <summary>Provider-native object/event evidence; never a Tunner entity or business-state substitute.</summary>
public sealed class ProviderObjectReference
{
    public ProviderObjectReference(ProviderBindingReference binding, string providerObjectType, string providerObjectId,
        string? providerEventId, DateTimeOffset observedAt, IReadOnlyDictionary<string, string>? safeEvidenceMetadataReferences = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerObjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerObjectId);
        if (providerEventId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        if (observedAt == default) throw new ArgumentException("An observation timestamp is required.", nameof(observedAt));
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        if (safeEvidenceMetadataReferences is not null)
        {
            foreach (var (name, reference) in safeEvidenceMetadataReferences)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                ArgumentException.ThrowIfNullOrWhiteSpace(reference);
                snapshot.Add(name, reference);
            }
        }
        Binding = binding;
        ProviderObjectType = providerObjectType;
        ProviderObjectId = providerObjectId;
        ProviderEventId = providerEventId;
        ObservedAt = observedAt.ToUniversalTime();
        SafeEvidenceMetadataReferences = new ReadOnlyDictionary<string, string>(snapshot);
    }

    public ProviderBindingReference Binding { get; }
    public string ProviderObjectType { get; }
    public string ProviderObjectId { get; }
    public string? ProviderEventId { get; }
    public DateTimeOffset ObservedAt { get; }
    public IReadOnlyDictionary<string, string> SafeEvidenceMetadataReferences { get; }
}

/// <summary>Correlated observations only. Empty evidence and provider IDs imply no business outcome.</summary>
public sealed class ProviderCorrelationEvidence
{
    public ProviderCorrelationEvidence(ProviderOperationContext operation, IEnumerable<ProviderObjectReference> references)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(references);
        var snapshot = references.ToArray();
        foreach (var reference in snapshot)
        {
            ArgumentNullException.ThrowIfNull(reference);
            if (reference.Binding != operation.Binding)
                throw new ArgumentException("Provider evidence must match the exact selected binding.", nameof(references));
        }
        Operation = operation;
        References = Array.AsReadOnly(snapshot);
    }

    public ProviderOperationContext Operation { get; }
    public IReadOnlyList<ProviderObjectReference> References { get; }
}
