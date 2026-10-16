using System.Collections.Concurrent;

namespace Tunner.Platform.Operations;

public static class TechnicalProblemCodes
{
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string ForbiddenScope = "FORBIDDEN_SCOPE";
    public const string VersionConflict = "VERSION_CONFLICT";
}

public sealed record TechnicalCommandEnvelope<TPayload>(
    string OperationId,
    string? IdempotencyKey,
    string CorrelationId,
    string? ResourceId,
    long? ExpectedVersion,
    DateTimeOffset RequestedAt,
    IReadOnlyDictionary<string, string> PolicyContextReferences,
    TPayload Payload);

/// <summary>Server-derived actor data. Request payloads never construct this value.</summary>
public sealed record ServerActorContext(string PrincipalReference);

public interface IServerActorContextAccessor
{
    ServerActorContext? GetCurrent();
}

public sealed record TechnicalAuthorizationRequest(string CapabilityReference, string? ResourceId, IReadOnlyDictionary<string, string> PolicyContextReferences);
public sealed record TechnicalAuthorizationResult(bool IsAuthorized, string? ProblemCode)
{
    public static TechnicalAuthorizationResult Allow() => new(true, null);
    public static TechnicalAuthorizationResult Deny(string problemCode) => new(false, problemCode);
}

public interface ITechnicalAuthorizationEvaluator
{
    Task<TechnicalAuthorizationResult> EvaluateAsync(ServerActorContext actor, TechnicalAuthorizationRequest request, CancellationToken cancellationToken);
}

public interface ITechnicalAuthorizationEvaluatorResolver
{
    ITechnicalAuthorizationEvaluator? Resolve(TechnicalAuthorizationRequest request);
}

/// <summary>Fails closed when no server actor or owner-domain evaluator is available.</summary>
public sealed class TechnicalAuthorizationGate(
    IServerActorContextAccessor actorContextAccessor,
    ITechnicalAuthorizationEvaluatorResolver evaluatorResolver)
{
    public async Task<TechnicalAuthorizationResult> AuthorizeAsync(TechnicalAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = actorContextAccessor.GetCurrent();
        if (actor is null || string.IsNullOrWhiteSpace(actor.PrincipalReference))
            return TechnicalAuthorizationResult.Deny(TechnicalProblemCodes.AuthenticationRequired);

        var evaluator = evaluatorResolver.Resolve(request);
        return evaluator is null
            ? TechnicalAuthorizationResult.Deny(TechnicalProblemCodes.ForbiddenScope)
            : await evaluator.EvaluateAsync(actor, request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Opaque owner-domain scope used only to separate idempotency semantics.</summary>
public sealed record IdempotencyScope(string Value);

public sealed class InMemoryIdempotencyExecutor
{
    private readonly ConcurrentDictionary<(string Scope, string Key), Lazy<Task<object>>> executions = new();

    public async Task<T> ExecuteAsync<T>(IdempotencyScope scope, string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(operation);
        var execution = executions.GetOrAdd((scope.Value, key), _ => new Lazy<Task<object>>(async () => { var result = await operation(cancellationToken).ConfigureAwait(false); return (object)result!; }));
        return (T)await execution.Value.ConfigureAwait(false);
    }
}

public sealed record ExpectedVersionCheckResult(bool IsCurrent, string? ProblemCode, long? CurrentVersion, long? ExpectedVersion)
{
    public static ExpectedVersionCheckResult NotRequired() => new(true, null, null, null);
    public static ExpectedVersionCheckResult Current(long version) => new(true, null, version, version);
    public static ExpectedVersionCheckResult Conflict(long expected, long current) => new(false, TechnicalProblemCodes.VersionConflict, current, expected);
}

public static class ExpectedVersionGuard
{
    /// <summary>The owner decides whether version protection applies; this guard only compares supplied values.</summary>
    public static ExpectedVersionCheckResult Check(bool ownerRequiresVersionCheck, long? expectedVersion, long currentVersion)
    {
        if (!ownerRequiresVersionCheck) return ExpectedVersionCheckResult.NotRequired();
        return expectedVersion == currentVersion
            ? ExpectedVersionCheckResult.Current(currentVersion)
            : ExpectedVersionCheckResult.Conflict(expectedVersion ?? -1, currentVersion);
    }
}