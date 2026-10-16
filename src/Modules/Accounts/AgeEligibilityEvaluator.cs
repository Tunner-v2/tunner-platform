namespace Tunner.Accounts;

public enum AgeEligibilityStatus
{
    Ineligible,
    Eligible
}

/// <summary>Derived PDR-0007 evidence; raw date of birth is deliberately absent.</summary>
public sealed record AgeEligibilityEvidence
{
    internal AgeEligibilityEvidence(
        AgeEligibilityStatus status,
        DateTimeOffset ageEvaluatedAt,
        DateOnly evaluationDate,
        string policyVersion,
        string evidenceReference,
        string correlationId)
    {
        Status = status;
        AgeEvaluatedAt = ageEvaluatedAt;
        EvaluationDate = evaluationDate;
        PolicyVersion = policyVersion;
        EvidenceReference = evidenceReference;
        CorrelationId = correlationId;
    }

    public AgeEligibilityStatus Status { get; }
    public string? FailureCode => Status == AgeEligibilityStatus.Ineligible ? "AGE_INELIGIBLE" : null;
    public DateTimeOffset AgeEvaluatedAt { get; }
    public DateOnly EvaluationDate { get; }
    public string PolicyFamily { get; } = "AgeAssurancePolicy";
    public string PolicyVersion { get; }
    public string EvidenceReference { get; }
    public string CorrelationId { get; }
}

/// <summary>
/// Implements FRD 17 section 11 and PDR-0007. The owning server application supplies
/// the clock, resolved active policy version and opaque operation evidence references.
/// </summary>
public sealed class AgeEligibilityEvaluator
{
    private const int RequiredAgeYears = 18;
    private readonly TimeProvider timeProvider;

    public AgeEligibilityEvaluator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    public AgeEligibilityEvidence Evaluate(
        DateOnly dateOfBirth,
        string policyVersion,
        string evidenceReference,
        string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var evaluatedAt = timeProvider.GetUtcNow().ToUniversalTime();
        var evaluationDate = DateOnly.FromDateTime(evaluatedAt.UtcDateTime);

        // An eighteenth birthday beyond DateOnly's range is later than every
        // representable evaluation date; do not overflow or approximate with days.
        var eligible = dateOfBirth.Year <= DateOnly.MaxValue.Year - RequiredAgeYears &&
            dateOfBirth.AddYears(RequiredAgeYears) <= evaluationDate;

        return new AgeEligibilityEvidence(
            eligible ? AgeEligibilityStatus.Eligible : AgeEligibilityStatus.Ineligible,
            evaluatedAt,
            evaluationDate,
            policyVersion,
            evidenceReference,
            correlationId);
    }
}
