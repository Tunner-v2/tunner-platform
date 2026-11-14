using System.Text.Json;

namespace Tunner.Identity.Persistence;

/// <summary>Private owner request. No route, policy default, bearer or Product-session effect.</summary>
internal sealed record SessionRevocationRequest(Guid RequestId, Guid TargetSessionId, long ExpectedVersion, string CorrelationId)
{
    internal void Validate(StepUpCaller caller)
    {
        if (RequestId == Guid.Empty || TargetSessionId == Guid.Empty || TargetSessionId == caller.SessionId || ExpectedVersion < 1 || ExpectedVersion == long.MaxValue)
            throw new ArgumentException("An exact other-session identity, version and request are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(CorrelationId);
    }
    public override string ToString() => nameof(SessionRevocationRequest);
}

internal sealed record SessionRevocationReceipt(Guid SessionReference, long PriorVersion, long RevokedVersion,
    string OperationId, string CorrelationId, DateTimeOffset RevokedAt)
{
    public override string ToString() => nameof(SessionRevocationReceipt);
}
internal sealed record SessionRevocationResult(string? ErrorCode, SessionRevocationReceipt? Receipt = null)
{
    internal bool Applied => Receipt is not null;
    public override string ToString() => nameof(SessionRevocationResult);
}

internal static class SessionRevocationBinding
{
    internal static readonly AssurancePolicyScope Scope = new("SESSION_REVOKE", "USER_BFF");
    internal static StepUpOperation Operation(StepUpCaller caller, SessionRevocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(request); request.Validate(caller);
        return new("OP-SESSION-REVOKE-" + request.RequestId.ToString("N"), request.CorrelationId, Scope,
            request.TargetSessionId.ToString("N"), request.ExpectedVersion,
            AuthenticationIdentity.Digest(JsonSerializer.Serialize(new
            {
                caller.AccountId, caller.SessionId, caller.Origin, request.RequestId,
                request.TargetSessionId, request.ExpectedVersion, request.CorrelationId,
                operation = Scope.Operation, context = Scope.Context
            })));
    }
}
