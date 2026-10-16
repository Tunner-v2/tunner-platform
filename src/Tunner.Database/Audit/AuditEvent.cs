using System.Globalization;
using System.Text.Json;

namespace Tunner.Database.Audit;

/// <summary>Owner-supplied authenticated identity references, never a client authorization claim.</summary>
public sealed class AuditActor
{
    public AuditActor(string principalId, string actorType, IEnumerable<string> identityReferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        PrincipalId = principalId;
        ActorType = actorType;
        IdentityReferences = AuditEvent.CopyReferences(identityReferences);
    }

    public string PrincipalId { get; }
    public string ActorType { get; }
    public IReadOnlyList<string> IdentityReferences { get; }
}

public sealed class AuditTargetVersion
{
    public AuditTargetVersion(string resourceId, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (version is not null) ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ResourceId = resourceId;
        Version = version;
    }

    public string ResourceId { get; }
    public string? Version { get; }
}

/// <summary>Immutable Document 28 audit metadata; action, result and policy meaning stay with the owner.</summary>
public sealed class AuditEvent
{
    public AuditEvent(
        Guid eventId, DateTimeOffset occurredAtUtc, AuditActor actor,
        string action, string operationId, string correlationId,
        IEnumerable<AuditTargetVersion> targets, string result,
        string dataClassification, string retentionClassReference,
        string? causationEventId = null, string? reason = null,
        IEnumerable<string>? permissionVersionReferences = null,
        IEnumerable<string>? scopeVersionReferences = null,
        IEnumerable<string>? policyVersionReferences = null,
        IEnumerable<string>? stepUpReferences = null,
        IEnumerable<string>? approvalReferences = null,
        IEnumerable<AuditTargetVersion>? beforeVersions = null,
        IEnumerable<AuditTargetVersion>? afterVersions = null,
        IEnumerable<string>? providerReferences = null,
        IEnumerable<string>? evidenceReferences = null)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("A stable audit event ID is required.", nameof(eventId));
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataClassification);
        ArgumentException.ThrowIfNullOrWhiteSpace(retentionClassReference);
        if (causationEventId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(causationEventId);
        if (reason is not null) ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        EventId = eventId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Actor = actor;
        Action = action;
        OperationId = operationId;
        CorrelationId = correlationId;
        Targets = CopyTargets(targets);
        Result = result;
        DataClassification = dataClassification;
        RetentionClassReference = retentionClassReference;
        CausationEventId = causationEventId;
        Reason = reason;
        PermissionVersionReferences = CopyReferences(permissionVersionReferences ?? []);
        ScopeVersionReferences = CopyReferences(scopeVersionReferences ?? []);
        PolicyVersionReferences = CopyReferences(policyVersionReferences ?? []);
        StepUpReferences = CopyReferences(stepUpReferences ?? []);
        ApprovalReferences = CopyReferences(approvalReferences ?? []);
        BeforeVersions = CopyTargets(beforeVersions ?? []);
        AfterVersions = CopyTargets(afterVersions ?? []);
        ProviderReferences = CopyReferences(providerReferences ?? []);
        EvidenceReferences = CopyReferences(evidenceReferences ?? []);
    }

    public Guid EventId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public AuditActor Actor { get; }
    public string Action { get; }
    public string OperationId { get; }
    public string CorrelationId { get; }
    public IReadOnlyList<AuditTargetVersion> Targets { get; }
    public string Result { get; }
    public string DataClassification { get; }
    public string RetentionClassReference { get; }
    public string? CausationEventId { get; }
    public string? Reason { get; }
    public IReadOnlyList<string> PermissionVersionReferences { get; }
    public IReadOnlyList<string> ScopeVersionReferences { get; }
    public IReadOnlyList<string> PolicyVersionReferences { get; }
    public IReadOnlyList<string> StepUpReferences { get; }
    public IReadOnlyList<string> ApprovalReferences { get; }
    public IReadOnlyList<AuditTargetVersion> BeforeVersions { get; }
    public IReadOnlyList<AuditTargetVersion> AfterVersions { get; }
    public IReadOnlyList<string> ProviderReferences { get; }
    public IReadOnlyList<string> EvidenceReferences { get; }

    /// <summary>Versioned exact envelope bytes; reference order is preserved, never given business precedence.</summary>
    public string ToEnvelope() => JsonSerializer.Serialize(new
    {
        schema_version = 1,
        event_id = EventId,
        occurred_at_utc = OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
        actor = new { principal_id = Actor.PrincipalId, actor_type = Actor.ActorType, identity_refs = Actor.IdentityReferences },
        action = Action,
        operation_id = OperationId,
        correlation_id = CorrelationId,
        causation_event_id = CausationEventId,
        targets = Targets.Select(x => new { resource_id = x.ResourceId, version = x.Version }),
        permission_version_refs = PermissionVersionReferences,
        scope_version_refs = ScopeVersionReferences,
        policy_version_refs = PolicyVersionReferences,
        reason = Reason,
        step_up_refs = StepUpReferences,
        approval_refs = ApprovalReferences,
        before_versions = BeforeVersions.Select(x => new { resource_id = x.ResourceId, version = x.Version }),
        after_versions = AfterVersions.Select(x => new { resource_id = x.ResourceId, version = x.Version }),
        provider_refs = ProviderReferences,
        evidence_refs = EvidenceReferences,
        result = Result,
        data_classification = DataClassification,
        retention_class_ref = RetentionClassReference
    });

    internal static IReadOnlyList<string> CopyReferences(IEnumerable<string> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var snapshot = references.ToArray();
        if (snapshot.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Audit references cannot be blank.", nameof(references));
        return Array.AsReadOnly(snapshot);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<AuditTargetVersion> CopyTargets(IEnumerable<AuditTargetVersion> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var snapshot = targets.ToArray();
        if (snapshot.Any(x => x is null)) throw new ArgumentException("Audit target references cannot be null.", nameof(targets));
        return Array.AsReadOnly(snapshot);
    }
}

