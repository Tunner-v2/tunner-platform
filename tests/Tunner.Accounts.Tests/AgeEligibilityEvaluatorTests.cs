using System.Globalization;
using System.Text.Json;
using Tunner.Accounts;
using Xunit;

namespace Tunner.Accounts.Tests;

public sealed class AgeEligibilityEvaluatorTests
{
    [Theory]
    [InlineData("2008-10-16", "2026-10-15", false)]
    [InlineData("2008-10-16", "2026-10-16", true)]
    [InlineData("2008-10-16", "2026-10-17", true)]
    [InlineData("2008-01-01", "2025-12-27", false)]
    [InlineData("2008-01-01", "2025-12-31", false)]
    [InlineData("2008-01-01", "2026-01-01", true)]
    [InlineData("2004-02-29", "2022-02-27", false)]
    [InlineData("2004-02-29", "2022-02-28", true)]
    [InlineData("2004-02-29", "2022-03-01", true)]
    [InlineData("2082-03-01", "2100-02-28", false)]
    [InlineData("2082-03-01", "2100-03-01", true)]
    [InlineData("2084-02-29", "2102-02-28", true)]
    [InlineData("0001-01-01", "0018-12-31", false)]
    [InlineData("0001-01-01", "0019-01-01", true)]
    [InlineData("9981-12-31", "9999-12-30", false)]
    [InlineData("9981-12-31", "9999-12-31", true)]
    [InlineData("9982-01-01", "9999-12-31", false)]
    [InlineData("9999-12-31", "9999-12-31", false)]
    [InlineData("2027-01-01", "2026-10-16", false)]
    public void ApprovedCalendarRuleHandlesBirthdaysLeapYearsAndRepresentableLimits(
        string birthDate, string evaluationDate, bool eligible)
    {
        var clock = new ProbeClock(AtUtc(evaluationDate));
        var evidence = new AgeEligibilityEvaluator(clock).Evaluate(
            DateOnly.ParseExact(birthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            "age-policy-v1", "evidence-1", "correlation-1");

        Assert.Equal(eligible ? AgeEligibilityStatus.Eligible : AgeEligibilityStatus.Ineligible, evidence.Status);
        Assert.Equal(eligible ? null : "AGE_INELIGIBLE", evidence.FailureCode);
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public void OneServerClockReadKeepsOutcomeDateAndInstantConsistentAcrossMidnight()
    {
        var before = new DateTimeOffset(2026, 10, 15, 23, 59, 59, TimeSpan.Zero);
        var clock = new ProbeClock(before, before.AddSeconds(1));
        var evaluator = new AgeEligibilityEvaluator(clock);
        var birthDate = new DateOnly(2008, 10, 16);

        var first = evaluator.Evaluate(birthDate, "v1", "e1", "c1");
        Assert.Equal(AgeEligibilityStatus.Ineligible, first.Status);
        Assert.Equal(before, first.AgeEvaluatedAt);
        Assert.Equal(new DateOnly(2026, 10, 15), first.EvaluationDate);
        Assert.Equal(1, clock.Reads);

        var next = evaluator.Evaluate(birthDate, "v1", "e2", "c1");
        Assert.Equal(AgeEligibilityStatus.Eligible, next.Status);
        Assert.Equal(before.AddSeconds(1), next.AgeEvaluatedAt);
        Assert.Equal(new DateOnly(2026, 10, 16), next.EvaluationDate);
        Assert.Equal(2, clock.Reads);
    }

    [Theory]
    [InlineData("2025-12-31T23:30:00-05:00", true)]
    [InlineData("2026-01-01T00:30:00+05:00", false)]
    public void EvaluationUsesUtcCalendarDateRatherThanClockOffsetLocalDate(string instant, bool eligible)
    {
        var clock = new ProbeClock(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));
        var evidence = new AgeEligibilityEvaluator(clock).Evaluate(new DateOnly(2008, 1, 1), "v1", "e1", "c1");

        Assert.Equal(eligible ? AgeEligibilityStatus.Eligible : AgeEligibilityStatus.Ineligible, evidence.Status);
        Assert.Equal(TimeSpan.Zero, evidence.AgeEvaluatedAt.Offset);
        Assert.Equal(DateOnly.FromDateTime(evidence.AgeEvaluatedAt.UtcDateTime), evidence.EvaluationDate);
    }

    [Fact]
    public void EvidencePreservesExactPolicyOperationProvenanceAndDeterministicReplay()
    {
        var now = AtUtc("2026-10-16");
        var evaluator = new AgeEligibilityEvaluator(new ProbeClock(now));
        var first = evaluator.Evaluate(new DateOnly(2000, 6, 1), "policy/version-7", "evidence/ref-3", "correlation/ref-4");
        var replay = evaluator.Evaluate(new DateOnly(2000, 6, 1), "policy/version-7", "evidence/ref-3", "correlation/ref-4");

        Assert.Equal(first, replay);
        Assert.Equal("AgeAssurancePolicy", first.PolicyFamily);
        Assert.Equal("policy/version-7", first.PolicyVersion);
        Assert.Equal("evidence/ref-3", first.EvidenceReference);
        Assert.Equal("correlation/ref-4", first.CorrelationId);
        Assert.Equal(now, first.AgeEvaluatedAt);
    }

    [Theory]
    [InlineData("2000-06-01")]
    [InlineData("2015-06-01")]
    public void ReturnedSerializedAndDiagnosticEvidenceContainsNoRawBirthDate(string birthDate)
    {
        var result = new AgeEligibilityEvaluator(new ProbeClock(AtUtc("2026-10-16"))).Evaluate(
            DateOnly.ParseExact(birthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), "v1", "e1", "c1");
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain(birthDate, json, StringComparison.Ordinal);
        Assert.DoesNotContain(birthDate, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(AgeEligibilityEvidence).GetProperties(), property =>
            property.Name.Contains("birth", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null, "e1", "c1")]
    [InlineData("", "e1", "c1")]
    [InlineData(" ", "e1", "c1")]
    [InlineData("v1", null, "c1")]
    [InlineData("v1", "", "c1")]
    [InlineData("v1", " ", "c1")]
    [InlineData("v1", "e1", null)]
    [InlineData("v1", "e1", "")]
    [InlineData("v1", "e1", " ")]
    public void MissingProvenanceProducesNoEvidenceOrClockReadAndDoesNotEchoDob(
        string? policyVersion, string? evidenceReference, string? correlationId)
    {
        var clock = new ProbeClock(AtUtc("2026-10-16"));
        var exception = Assert.ThrowsAny<ArgumentException>(() => new AgeEligibilityEvaluator(clock).Evaluate(
            new DateOnly(2000, 6, 1), policyVersion!, evidenceReference!, correlationId!));

        Assert.Equal(0, clock.Reads);
        Assert.DoesNotContain("2000-06-01", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2007, 2, 29)]
    [InlineData(2008, 13, 1)]
    [InlineData(2008, 1, 0)]
    public void InvalidCalendarInputsCannotReachTypedEvaluatorOrProduceEvidence(int year, int month, int day)
    {
        var clock = new ProbeClock(AtUtc("2026-10-16"));
        var evaluator = new AgeEligibilityEvaluator(clock);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            evaluator.Evaluate(new DateOnly(year, month, day), "v1", "e1", "c1"));
        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public void ServerClockIsRequired() =>
        Assert.Throws<ArgumentNullException>(() => new AgeEligibilityEvaluator(null!));

    [Fact]
    public void AccountsCapabilityHasNoHostDatabaseProviderOrFrameworkDependency()
    {
        var references = typeof(AgeEligibilityEvaluator).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty).ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("Tunner.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("AWSSDK", StringComparison.Ordinal));
    }

    private static DateTimeOffset AtUtc(string date) =>
        DateTimeOffset.ParseExact(date + "T12:00:00+00:00", "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private sealed class ProbeClock(DateTimeOffset first, DateTimeOffset? later = null) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return Reads == 1 ? first : later ?? first;
        }
    }
}
