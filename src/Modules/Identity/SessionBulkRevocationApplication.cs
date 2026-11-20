namespace Tunner.Identity.Persistence;

/// <summary>One complete original-set operation; later-created sessions are outside this manifest.</summary>
internal sealed class SessionBulkRevocationApplication(AuthenticationApplication authentication, StepUpApplication stepUp,
    ISessionBulkRevocationDiscoverySource discovery, IAssurancePolicySource assurance)
{
    internal static readonly AssurancePolicyScope Scope = new("SESSION_REVOKE_OTHERS", "USER_BFF");
    internal static bool Owns(AssurancePolicyScope scope) => scope == Scope;
    internal Task<SessionBulkRevocationPreparation> PrepareAsync(StepUpCaller caller, Guid requestId, string correlationId, CancellationToken ct = default)
        => authentication.PrepareSessionBulkRevocationAsync(caller, requestId, correlationId, discovery, assurance, ct);
    internal Task<SessionBulkRevocationResult> ReadResultAsync(StepUpCaller caller, SessionBulkRevocationManifest manifest, CancellationToken ct = default)
        => authentication.ReadSessionBulkRevocationAsync(caller, manifest, ct);
    internal Task<SessionBulkRevocationResult> ReadOriginalAsync(StepUpCaller caller, SessionBulkRevocationManifest manifest, CancellationToken ct = default)
        => authentication.ReadSessionBulkRevocationOriginalAsync(caller, manifest, discovery, assurance, ct);
    internal async Task<SessionBulkRevocationResult> RevokeAsync(StepUpCaller caller, SessionBulkRevocationManifest manifest, string proofKey, CancellationToken ct = default)
    {
        manifest.RequireActor(caller);
        using var result = await stepUp.ExecuteSessionBulkRevocationAsync(caller, manifest, discovery, proofKey, ct).ConfigureAwait(false);
        return result.Applied ? await ReadResultAsync(caller, manifest, ct).ConfigureAwait(false) : new(result.ErrorCode);
    }
}
