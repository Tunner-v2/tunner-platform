namespace Tunner.Identity.Persistence;

internal enum FederationAttemptPurpose { SignIn, Registration, Link, OperationProof }

/// <summary>Trusted original owner facts, not proof, current authority or a browser DTO.</summary>
internal sealed class FederationAttemptOwner
{
    internal FederationAttemptOwner(StepUpCaller caller, StepUpOperation operation, long accountVersion, long sessionVersion,
        Guid? sourceLinkId = null, long? sourceLinkVersion = null)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegative(accountVersion); ArgumentOutOfRangeException.ThrowIfNegative(sessionVersion);
        if (sourceLinkId.HasValue != sourceLinkVersion.HasValue || sourceLinkId == Guid.Empty || sourceLinkVersion is < 0)
            throw new ArgumentException("An exact source link and version must be supplied together.");
        Caller = caller; Operation = operation; AccountVersion = accountVersion; SessionVersion = sessionVersion;
        SourceLinkId = sourceLinkId; SourceLinkVersion = sourceLinkVersion;
    }

    internal StepUpCaller Caller { get; }
    internal StepUpOperation Operation { get; }
    internal long AccountVersion { get; }
    internal long SessionVersion { get; }
    internal Guid? SourceLinkId { get; }
    internal long? SourceLinkVersion { get; }

    internal bool Matches(FederationAttemptOwner current) =>
        Caller.AccountId == current.Caller.AccountId && Caller.SessionId == current.Caller.SessionId &&
        Caller.Origin == current.Caller.Origin && AccountVersion == current.AccountVersion && SessionVersion == current.SessionVersion &&
        Operation.OperationId == current.Operation.OperationId && Operation.CorrelationId == current.Operation.CorrelationId &&
        Operation.Scope == current.Operation.Scope && Operation.ResourceId == current.Operation.ResourceId &&
        Operation.ExpectedVersion == current.Operation.ExpectedVersion && Operation.ContextDigest == current.Operation.ContextDigest &&
        SourceLinkId == current.SourceLinkId && SourceLinkVersion == current.SourceLinkVersion;

    public override string ToString() => nameof(FederationAttemptOwner);
}

/// <summary>
/// Immutable attempt-binding groundwork only. All inputs and the decision clock come from trusted owners.
/// This performs no protocol validation, current-policy resolution, persistence, consumption or session issuance.
/// A final owner must resolve current profile/link/Account/session authority and consume the attempt atomically;
/// matching the same original inputs twice remains possible and is not replay protection or a safe-result ledger.
/// </summary>
internal sealed class FederationAttemptBinding
{
    private readonly FederationProviderKind provider;
    private readonly string providerNamespace;
    private readonly string clientId;
    private readonly string profileVersion;
    private readonly string mappingEvidence;
    private readonly string[] admittedIssuers;
    private readonly FederationAttemptOwner? owner;

    internal FederationAttemptBinding(Guid attemptId, string requestId, string correlationId, FederationAttemptPurpose purpose,
        FederationProviderRegistration profile, string browserBinding, string continuationReference,
        IReadOnlyCollection<string> allowedContinuations, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        FederationAttemptOwner? owner = null)
    {
        if (attemptId == Guid.Empty) throw new ArgumentException("A stable attempt identity is required.", nameof(attemptId));
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(allowedContinuations);
        RequireOpaque(requestId); RequireOpaque(correlationId); RequireOpaque(browserBinding);
        if (!CleanLocalContinuation(continuationReference) || !allowedContinuations.Contains(continuationReference, StringComparer.Ordinal))
            throw new ArgumentException("An explicitly allowed clean local continuation is required.");
        if (expiresAt <= createdAt) throw new ArgumentException("An explicit nonempty attempt interval is required.");
        var needsOwner = purpose is FederationAttemptPurpose.Link or FederationAttemptPurpose.OperationProof;
        if (needsOwner != (owner is not null) || purpose == FederationAttemptPurpose.OperationProof && owner?.SourceLinkId is null)
            throw new ArgumentException("The attempt purpose requires its exact original owner binding.");

        AttemptId = attemptId; RequestId = requestId; CorrelationId = correlationId; Purpose = purpose;
        BrowserBinding = browserBinding; ContinuationReference = continuationReference; CreatedAt = createdAt; ExpiresAt = expiresAt;
        provider = profile.Provider; providerNamespace = profile.ProviderNamespace; clientId = profile.ClientId;
        profileVersion = profile.RegistrationProfileVersion; mappingEvidence = profile.IssuerMappingEvidenceReference;
        admittedIssuers = profile.AdmittedIssuers.ToArray(); this.owner = owner;
    }

    internal Guid AttemptId { get; }
    internal string RequestId { get; }
    internal string CorrelationId { get; }
    internal FederationAttemptPurpose Purpose { get; }
    internal string BrowserBinding { get; }
    internal string ContinuationReference { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal bool Matches(Guid attemptId, string requestId, string correlationId, FederationAttemptPurpose purpose,
        FederationProviderRegistration currentProfile, string browserBinding, string continuationReference, DateTimeOffset decisionAt,
        FederationAttemptOwner? currentOwner = null) =>
        decisionAt >= CreatedAt && decisionAt < ExpiresAt && AttemptId == attemptId && RequestId == requestId &&
        CorrelationId == correlationId && Purpose == purpose && BrowserBinding == browserBinding && ContinuationReference == continuationReference &&
        currentProfile is not null && provider == currentProfile.Provider && providerNamespace == currentProfile.ProviderNamespace &&
        clientId == currentProfile.ClientId && profileVersion == currentProfile.RegistrationProfileVersion &&
        mappingEvidence == currentProfile.IssuerMappingEvidenceReference && admittedIssuers.SequenceEqual(currentProfile.AdmittedIssuers, StringComparer.Ordinal) &&
        (owner is null ? currentOwner is null : currentOwner is not null && owner.Matches(currentOwner));

    private static void RequireOpaque(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\0')) throw new ArgumentException("An original opaque context value is required.");
    }

    private static bool CleanLocalContinuation(string value) => !string.IsNullOrWhiteSpace(value) && value[0] == '/' &&
        !value.Contains("//", StringComparison.Ordinal) && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '?' or '#' or '%' or ':') &&
        !value.Split('/').Any(segment => segment is "." or "..");

    public override string ToString() => nameof(FederationAttemptBinding);
}
