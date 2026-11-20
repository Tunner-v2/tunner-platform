using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit qualified owner inputs; no provider, assurance, lifetime or retention policy is defaulted.</summary>
public sealed record FederationProfile(string Version, FederationProviderKind Provider, string Origin, string TransportScheme,
    string StableNamespaceId, string Issuer, string ClientId, IReadOnlyCollection<string> AdmittedIssuerAliases,
    string IssuerMappingEvidenceReference, int MaximumNamespaceUtf8Bytes, int MaximumSubjectUtf8Bytes,
    TimeSpan AttemptLifetime, TimeSpan VerifiedLifetime, TimeSpan ResultRecoveryLifetime, int MaximumPendingAttempts,
    string ContinuationPath, IReadOnlyCollection<string> AllowedContinuations, string SignInAdmissionPolicyVersion,
    string PrimaryAssurance, string AssuranceMappingEvidenceReference, string SessionUsePolicyVersion,
    string ProcessingReference, string RetentionReference)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FederationFreshAuthenticationProfile? FreshAuthentication { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FederationRegistrationProfile? Registration { get; init; }
    public override string ToString() => nameof(FederationProfile);
}

public sealed class FederationProfileVersion
{
    public FederationProfileVersion(PolicyVersionCandidate<FederationProviderKind> metadata, FederationProfile values)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(values.AdmittedIssuerAliases); ArgumentNullException.ThrowIfNull(values.AllowedContinuations);
        Values = values with
        {
            AdmittedIssuerAliases = Array.AsReadOnly(values.AdmittedIssuerAliases.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
            AllowedContinuations = Array.AsReadOnly(values.AllowedContinuations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
        };
        if (!Enum.IsDefined(values.Provider) || metadata.Scope != values.Provider || metadata.VersionId != values.Version ||
            new[] { values.Version, values.TransportScheme, values.SignInAdmissionPolicyVersion, values.PrimaryAssurance,
                values.AssuranceMappingEvidenceReference, values.SessionUsePolicyVersion, values.ProcessingReference, values.RetentionReference }.Any(x => string.IsNullOrWhiteSpace(x) || x.Any(char.IsControl)) ||
            !Uri.TryCreate(values.Origin, UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.UserInfo.Length != 0 ||
            values.Origin != origin.GetLeftPart(UriPartial.Authority) || values.AttemptLifetime <= TimeSpan.Zero || values.VerifiedLifetime <= TimeSpan.Zero ||
            values.ResultRecoveryLifetime <= TimeSpan.Zero || values.MaximumPendingAttempts <= 0 || Values.AllowedContinuations.Count == 0 ||
            Values.AllowedContinuations.Any(path => !CleanPath(path)) || !Values.AllowedContinuations.Contains(values.ContinuationPath, StringComparer.Ordinal) ||
            values.FreshAuthentication is { } fresh && !Values.AllowedContinuations.Contains(fresh.ContinuationPath, StringComparer.Ordinal))
            throw new ArgumentException("A complete explicit qualified federation profile is required.");
        if (values.Registration is { Complete: false }) throw new ArgumentException("Registration qualification must be complete.");
        Metadata = metadata;
        Registration = new(values.Provider, values.StableNamespaceId, values.Issuer, values.ClientId, Values.AdmittedIssuerAliases,
            values.Version, values.IssuerMappingEvidenceReference, values.MaximumNamespaceUtf8Bytes, values.MaximumSubjectUtf8Bytes);
        Snapshot = JsonSerializer.Serialize(new { metadata.VersionId, metadata.Status, metadata.Scope, metadata.EffectiveInterval,
            ApprovalEvidence = metadata.ApprovalEvidenceReferences.Order(StringComparer.Ordinal), Values });
    }

    public PolicyVersionCandidate<FederationProviderKind> Metadata { get; }
    public FederationProfile Values { get; }
    internal FederationProviderRegistration Registration { get; }
    internal string Snapshot { get; }
    internal void Require(string origin, DateTimeOffset at)
    {
        if (Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) || Values.Origin != origin)
            throw new InvalidOperationException("Current qualified federation authority is unavailable.");
    }
    internal DateTimeOffset Clip(DateTimeOffset expiry) => Metadata.EffectiveInterval.Until is { } until && until < expiry ? until : expiry;
    internal static bool CleanPath(string? path) => !string.IsNullOrWhiteSpace(path) && path[0] == '/' &&
        !path.Contains("//", StringComparison.Ordinal) && !path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '?' or '#' or '%' or ':') &&
        !path.Split('/').Any(segment => segment is "." or "..");
    public override string ToString() => nameof(FederationProfileVersion);
}

/// <summary>Pure synchronous source, without external I/O. Resolve again at the final instant; no captured fallback.</summary>
public interface IFederationProfileSource
{
    FederationProfileVersion RequireCurrent(FederationProviderKind provider, DateTimeOffset effectiveAt);
}

public sealed class FederationProfileCatalog(IEnumerable<FederationProfileVersion> versions) : IFederationProfileSource
{
    private readonly FederationProfileVersion[] versions = versions?.ToArray() ?? throw new ArgumentNullException(nameof(versions));
    public FederationProfileVersion RequireCurrent(FederationProviderKind provider, DateTimeOffset effectiveAt)
    {
        var selected = EffectivePolicyVersionResolver.Resolve(effectiveAt, provider, versions.Select(x => x.Metadata),
            static (a, b) => a == b, static version => version.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => ReferenceEquals(x.Metadata, selected));
    }
}

/// <summary>Trusted terminal native-handler input ONLY. Never bind this type from an HTTP body or claims bag.</summary>
public sealed class FederationProtocolEvidence(FederationProviderKind provider, string profileVersion, Guid attemptId,
    string issuer, string subject, string clientId, DateTimeOffset? authenticatedAt = null,
    IReadOnlyCollection<string>? authenticationMethods = null, string? providerVerifiedEmail = null,
    string? providerEmailMappingEvidenceReference = null)
{
    internal FederationProviderKind Provider { get; } = provider;
    internal string ProfileVersion { get; } = profileVersion;
    internal Guid AttemptId { get; } = attemptId;
    internal string Issuer { get; } = issuer;
    internal string Subject { get; } = subject;
    internal string ClientId { get; } = clientId;
    internal DateTimeOffset? AuthenticatedAt { get; } = authenticatedAt;
    internal IReadOnlyCollection<string>? AuthenticationMethods { get; } = authenticationMethods is null
        ? null : Array.AsReadOnly(authenticationMethods.ToArray());
    internal string? ProviderVerifiedEmail { get; } = providerVerifiedEmail;
    internal string? ProviderEmailMappingEvidenceReference { get; } = providerEmailMappingEvidenceReference;
    public override string ToString() => nameof(FederationProtocolEvidence);
}

public sealed class FederationChallengeAdmission
{
    internal FederationChallengeAdmission(FederationProviderKind provider, string profileVersion, string scheme, Guid attemptId, FederationAttemptPurpose purpose)
    { Provider = provider; ProfileVersion = profileVersion; Scheme = scheme; AttemptId = attemptId.ToString("N"); Purpose = purpose.ToString(); }
    public FederationProviderKind Provider { get; }
    public string ProfileVersion { get; }
    public string Scheme { get; }
    public string AttemptId { get; }
    public string Purpose { get; }
    public override string ToString() => nameof(FederationChallengeAdmission);
}

public sealed class FederationCallbackResult
{
    internal FederationCallbackResult(string? continuation, bool verified) { Continuation = continuation; Verified = verified; }
    public string? Continuation { get; }
    public bool Verified { get; }
    public override string ToString() => nameof(FederationCallbackResult);
}

public sealed record FederationPendingView(Guid AttemptId, Guid RequestId, string Status, string? ErrorCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Resolution = null)
{ public override string ToString() => nameof(FederationPendingView); }
public sealed record FederationRegistrationAvailability(bool GoogleAvailable, bool MicrosoftAvailable);
public sealed record FederationBootstrap(bool GoogleAvailable, bool MicrosoftAvailable, FederationPendingView? Pending, bool PreserveOriginalBrowser = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FederationRegistrationAvailability? Registration = null)
{ public override string ToString() => nameof(FederationBootstrap); }

internal sealed class FederationCurrentBrowser(AuthenticationCaller caller, Guid browserKey, DateTimeOffset browserExpiresAt,
    string authenticationPolicyVersion, string originalCookieDigest, Guid? originalPreviousSessionId, string actualCookieDigest)
{
    internal AuthenticationCaller Caller { get; } = caller;
    internal Guid BrowserKey { get; } = browserKey;
    internal DateTimeOffset BrowserExpiresAt { get; } = browserExpiresAt;
    internal string AuthenticationPolicyVersion { get; } = authenticationPolicyVersion;
    internal string OriginalCookieDigest { get; } = originalCookieDigest;
    internal Guid? OriginalPreviousSessionId { get; } = originalPreviousSessionId;
    internal string ActualCookieDigest { get; } = actualCookieDigest;
    internal bool OriginalFingerprint => OriginalCookieDigest == ActualCookieDigest && OriginalPreviousSessionId == Caller.PreviousSessionId;
    public override string ToString() => nameof(FederationCurrentBrowser);
}

/// <summary>Exact caller-owned-transaction request to AuthenticationApplication; not a browser DTO or provider proof factory.</summary>
internal sealed class FederationLinkedSignInRequest(FederationAttemptRow attempt, FederationCurrentBrowser browser,
    ExternalIdentityLinkRow link, FederationProfileVersion currentProfile)
{
    internal Guid AttemptId { get; } = attempt.AttemptId;
    internal Guid RequestId { get; } = attempt.RequestId;
    internal string CorrelationId { get; } = attempt.CorrelationId;
    internal AuthenticationCaller Caller { get; } = browser.Caller;
    internal bool RememberMe { get; } = attempt.RememberMe;
    internal Guid? OriginalPreviousSessionId { get; } = attempt.PreviousSessionId;
    internal string OriginalCookieDigest { get; } = attempt.PreviousCookieDigest;
    internal string ActualCookieDigest { get; } = browser.ActualCookieDigest;
    internal Guid LinkId { get; } = link.LinkId;
    internal long LinkVersion { get; } = attempt.LinkVersion!.Value;
    internal string AccountId { get; } = link.AccountId;
    internal string ProviderNamespace { get; } = link.Provider;
    internal string Subject { get; } = link.Subject;
    internal string ValidatedIssuer { get; } = attempt.ValidatedIssuer!;
    internal string ClientId { get; } = attempt.ClientId;
    internal string OriginalProfileVersion { get; } = attempt.ProfileVersion;
    internal string IssuerMappingEvidenceReference { get; } = attempt.MappingEvidenceReference;
    internal string QualificationEvidenceReference { get; } = attempt.LinkQualificationEvidenceReference!;
    internal string QualificationProfileVersion { get; } = attempt.LinkQualificationProfileVersion!;
    // Protocol verification time bounds this pending handoff; it is NOT provider auth_time or fresh step-up evidence.
    internal DateTimeOffset ProtocolVerifiedAt { get; } = attempt.VerifiedAt!.Value;
    internal DateTimeOffset VerificationExpiresAt { get; } = attempt.VerificationExpiresAt!.Value;
    internal FederationProfileVersion CurrentProfile { get; } = currentProfile;
    public override string ToString() => nameof(FederationLinkedSignInRequest);
}

internal sealed class FederationAttemptResult(FederationAttemptRow? row, string? errorCode = null, AuthenticationAttempt? authentication = null, string? projectedState = null, RegistrationResult? registration = null)
{
    internal FederationAttemptRow? Row { get; } = row;
    internal string? ErrorCode { get; } = errorCode;
    internal AuthenticationAttempt? Authentication { get; } = authentication;
    internal string? ProjectedState { get; } = projectedState;
    internal RegistrationResult? Registration { get; } = registration;
    public override string ToString() => nameof(FederationAttemptResult);
}
