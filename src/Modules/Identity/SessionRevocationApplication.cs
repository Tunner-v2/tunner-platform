namespace Tunner.Identity.Persistence;

/// <summary>One private owner operation, sharing Identity's existing proof/transaction lifecycle.</summary>
internal sealed class SessionRevocationApplication(AuthenticationApplication authentication, StepUpApplication stepUp)
{
    internal static bool Owns(AssurancePolicyScope scope) => scope == SessionRevocationBinding.Scope;

    internal async Task<StepUpOperation?> PrepareAsync(StepUpCaller caller, SessionRevocationRequest request, CancellationToken ct = default)
    {
        var operation = SessionRevocationBinding.Operation(caller, request);
        return await authentication.AuthorizeSessionRevocationAsync(caller, request, ct).ConfigureAwait(false) ? operation : null;
    }

    internal Task<SessionRevocationResult> ReadResultAsync(StepUpCaller caller, SessionRevocationRequest request, CancellationToken ct = default)
        => authentication.ReadSessionRevocationResultAsync(caller, request, ct);

    internal async Task<SessionRevocationResult> RevokeAsync(StepUpCaller caller, SessionRevocationRequest request, string proofKey,
        CancellationToken ct = default)
    {
        var operation = SessionRevocationBinding.Operation(caller, request);
        using var result = await stepUp.ExecuteSessionRevocationAsync(caller, operation, request, proofKey, ct).ConfigureAwait(false);
        if (!result.Applied) return new(result.ErrorCode);
        // A committed proof alone is never the owner outcome. This separately authorized read
        // proves the exact persisted target transition and both causal outbox records.
        return await ReadResultAsync(caller, request, ct).ConfigureAwait(false);
    }
}
