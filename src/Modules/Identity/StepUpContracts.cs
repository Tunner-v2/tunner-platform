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
internal sealed record StepUpExecutionResult(bool Applied, string? ErrorCode, bool SignInRequired = false);
