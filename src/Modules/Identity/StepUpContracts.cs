using System.Security.Claims;

namespace Tunner.Identity.Persistence;

/// <summary>Private trusted owner binding, never a request-body authority.</summary>
internal sealed class StepUpOperation
{
    internal StepUpOperation(string operationId, string correlationId, AssurancePolicyScope scope, string resourceId, long expectedVersion, string contextDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(scope); ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        if (contextDigest is not { Length: 64 } || !contextDigest.All(Uri.IsHexDigit)) throw new ArgumentException("A canonical owner context digest is required.", nameof(contextDigest));
        OperationId = operationId; CorrelationId = correlationId; Scope = scope; ResourceId = resourceId; ExpectedVersion = expectedVersion; ContextDigest = contextDigest.ToUpperInvariant();
    }
    internal string OperationId { get; }
    internal string CorrelationId { get; }
    internal AssurancePolicyScope Scope { get; }
    internal string ResourceId { get; }
    internal long ExpectedVersion { get; }
    internal string ContextDigest { get; }
    public override string ToString() => nameof(StepUpOperation);
}

internal sealed class StepUpCaller
{
    private StepUpCaller(string accountId, Guid sessionId, string origin, string networkDigest)
    { AccountId = accountId; SessionId = sessionId; Origin = origin; NetworkDigest = networkDigest; }
    internal string AccountId { get; }
    internal Guid SessionId { get; }
    internal string Origin { get; }
    internal string NetworkDigest { get; }
    internal static StepUpCaller FromPrincipal(ClaimsPrincipal principal, string origin, string networkDigest)
    {
        ArgumentNullException.ThrowIfNull(principal); ArgumentException.ThrowIfNullOrWhiteSpace(origin); ArgumentException.ThrowIfNullOrWhiteSpace(networkDigest);
        if (principal.Identity?.IsAuthenticated != true || principal.Identity.AuthenticationType != AuthenticationIdentity.Scheme ||
            AuthenticationIdentity.SessionId(principal) is not { } session ||
            principal.FindFirstValue(ClaimTypes.NameIdentifier) is not { Length: > 0 } account ||
            principal.FindFirstValue(AuthenticationIdentity.OriginClaim) != origin)
            throw new ArgumentException("A current server-authenticated User principal is required.", nameof(principal));
        new AuthenticationCaller(networkDigest, networkDigest, origin, session).Validate();
        return new(account, session, origin, networkDigest);
    }
    public override string ToString() => nameof(StepUpCaller);
}

internal sealed class StepUpProofGrant(string key, DateTimeOffset expiresAt)
{
    internal string Key { get; } = key;
    internal DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => nameof(StepUpProofGrant);
}
internal sealed record StepUpIssueResult(string? ErrorCode, StepUpProofGrant? Grant);
internal sealed class StepUpExecutionResult(bool applied, string? errorCode, bool signInRequired = false) : IDisposable
{
    private RecoveryCodeFirstDelivery? recoveryDelivery;
    internal bool Applied { get; } = applied;
    internal string? ErrorCode { get; } = errorCode;
    internal bool SignInRequired { get; } = signInRequired;
    internal bool HasRecoveryDelivery => recoveryDelivery is not null;
    internal static StepUpExecutionResult Committed(bool signInRequired, RecoveryCodeFirstDelivery? delivery)
    {
        if (signInRequired && delivery is not null) throw new InvalidOperationException("Recovery delivery cannot accompany a terminal session effect.");
        return new(true, null, signInRequired) { recoveryDelivery = delivery };
    }
    internal RecoveryCodeFirstDelivery? TakeRecoveryDelivery() => Interlocked.Exchange(ref recoveryDelivery, null);
    public void Dispose() => TakeRecoveryDelivery()?.Dispose();
    public override string ToString() => nameof(StepUpExecutionResult);
}
