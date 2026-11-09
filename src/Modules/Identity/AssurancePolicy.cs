using System.Collections.ObjectModel;
using Tunner.Platform.Operations;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Trusted owner identifiers; neither browser claims nor personal information.</summary>
public sealed record AssurancePolicyScope
{
    public AssurancePolicyScope(string operation, string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        Operation = operation; Context = context;
    }
    public string Operation { get; }
    public string Context { get; }
    public override string ToString() => nameof(AssurancePolicyScope);
}

/// <summary>Approved owner values. Assurance identifiers are opaque; no ordering or lifetime is defaulted.</summary>
public sealed class AssurancePolicyVersion
{
    public AssurancePolicyVersion(PolicyVersionCandidate<AssurancePolicyScope> metadata, string requiredAssurance,
        TimeSpan proofLifetime, IReadOnlyDictionary<string, string> methodAssurances)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredAssurance);
        ArgumentNullException.ThrowIfNull(methodAssurances);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(proofLifetime, TimeSpan.Zero);
        if (methodAssurances.Count == 0 || methodAssurances.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value)))
            throw new ArgumentException("Explicit supported method assurance mappings are required.", nameof(methodAssurances));
        Metadata = metadata; RequiredAssurance = requiredAssurance; ProofLifetime = proofLifetime;
        MethodAssurances = new ReadOnlyDictionary<string, string>(methodAssurances.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
    }
    public PolicyVersionCandidate<AssurancePolicyScope> Metadata { get; }
    public string RequiredAssurance { get; }
    public TimeSpan ProofLifetime { get; }
    public IReadOnlyDictionary<string, string> MethodAssurances { get; }
    public string RequireMethodAssurance(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return MethodAssurances.TryGetValue(method, out var assurance) ? assurance
            : throw new InvalidOperationException("The authentication method has no configured assurance mapping.");
    }
    public override string ToString() => nameof(AssurancePolicyVersion);
}

public interface IAssurancePolicySource
{
    AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null);
}

/// <summary>Shared version-resolution mechanics; no authenticator, session upgrade or production policy defaults.</summary>
public sealed class AssurancePolicyCatalog : IAssurancePolicySource
{
    private readonly AssurancePolicyVersion[] versions;
    public AssurancePolicyCatalog(IEnumerable<AssurancePolicyVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        this.versions = versions.ToArray();
        if (this.versions.Any(x => x is null)) throw new ArgumentException("Assurance policy records cannot be null.", nameof(versions));
    }
    public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (expectedVersion is not null) ArgumentException.ThrowIfNullOrWhiteSpace(expectedVersion);
        var selected = EffectivePolicyVersionResolver.Resolve(executedAt, scope, versions.Select(x => x.Metadata),
            static (a, b) => a == b, static x => x.Status == "ACTIVE").RequireResolvedVersion();
        if (expectedVersion is not null && !StringComparer.Ordinal.Equals(expectedVersion, selected.VersionId))
            throw new AssurancePolicyVersionConflictException();
        return versions.Single(x => ReferenceEquals(x.Metadata, selected));
    }
}

public sealed class AssurancePolicyVersionConflictException : InvalidOperationException
{
    public AssurancePolicyVersionConflictException() : base("The effective assurance policy changed.") { }
    public string ProblemCode { get; } = TechnicalProblemCodes.VersionConflict;
}
