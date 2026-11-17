using System.Security.Claims;
using System.Text.Json.Serialization;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

namespace Tunner.Admin.Bff;

public sealed record AdminIdentityConfigurationHttpProfile(PolicyVersionCandidate<string> Metadata,
    IReadOnlyList<IdentityConfigurationScope> Scopes, int MaximumRequestBytes)
{
    public AdminIdentityConfigurationPresentationBinding? Presentation { get; init; }
    public const string Scope = "ADMIN_IDENTITY_CONFIGURATION_HTTP";
    internal void Require(DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != Scope || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) ||
            Scopes is null || Scopes.Count == 0 || Scopes.Any(x => x is null) || Scopes.Distinct().Count() != Scopes.Count || MaximumRequestBytes < 1)
            throw new IdentityConfigurationUnavailableException();
        try { foreach (var scope in Scopes) _ = AdminIdentityConfigurationScope.From(scope).Value(); }
        catch (ArgumentException) { throw new IdentityConfigurationUnavailableException(); }
        if (Presentation is { } binding && (string.IsNullOrWhiteSpace(binding.Scope) || string.IsNullOrWhiteSpace(binding.VariantReference) ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at))) throw new IdentityConfigurationUnavailableException();
    }
}
public sealed record AdminIdentityConfigurationPresentationBinding(string Scope, string VariantReference, EffectivePolicyInterval EffectiveInterval);
public interface IAdminIdentityConfigurationHttpProfileSource { AdminIdentityConfigurationHttpProfile RequireEffective(DateTimeOffset at); }
/// <summary>Actual Admin/workforce owner receipt. This supplies no Content or Identity capability by itself.</summary>
public sealed record AdminIdentityConfigurationSession(string ActorReference, string SessionReference, IdentityConfigurationActorKind ActorKind,
    string QualificationReference, EffectivePolicyInterval EffectiveInterval);
public interface IAdminIdentityConfigurationSessionSource
{ ValueTask<AdminIdentityConfigurationSession?> RequireCurrentAsync(ClaimsPrincipal principal, DateTimeOffset at, CancellationToken cancellationToken); }
public interface IAdminIdentityConfigurationAuthorizationSource
{
    ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(AdminIdentityConfigurationSession session,
        IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationScope
{
    public required IdentityConfigurationFamily Family { get; init; }
    public AdminIdentityAssuranceScope? AssuranceScope { get; init; }
    public string? AuthenticationPolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RegistrationPolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TotpPolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PasskeyPolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AssurancePolicyVersion { get; init; }
    internal IdentityConfigurationScope Value()
    {
        if (Family != IdentityConfigurationFamily.TotpContent && TotpPolicyVersion is not null ||
            Family != IdentityConfigurationFamily.PasskeyContent && PasskeyPolicyVersion is not null ||
            Family != IdentityConfigurationFamily.StepUpContent && AssurancePolicyVersion is not null)
            throw new ArgumentException("An exact typed Identity configuration scope is required.");
        return Family switch
    {
        IdentityConfigurationFamily.Authentication when AssuranceScope is null && AuthenticationPolicyVersion is null && RegistrationPolicyVersion is null => new(Family),
        IdentityConfigurationFamily.Registration or IdentityConfigurationFamily.Totp or IdentityConfigurationFamily.Passkey or IdentityConfigurationFamily.RecoveryCodes or IdentityConfigurationFamily.SessionListing when AssuranceScope is null && AuthenticationPolicyVersion is null && RegistrationPolicyVersion is null => new(Family),
        IdentityConfigurationFamily.Assurance when AssuranceScope is not null && AuthenticationPolicyVersion is null && RegistrationPolicyVersion is null => new(Family, AssuranceScope.Value()),
        IdentityConfigurationFamily.AuthenticationContent or IdentityConfigurationFamily.SessionListingContent or IdentityConfigurationFamily.SessionRevocationContent or IdentityConfigurationFamily.SessionBulkRevocationContent when AssuranceScope is null && !string.IsNullOrWhiteSpace(AuthenticationPolicyVersion) && RegistrationPolicyVersion is null => new(Family, AuthenticationPolicyVersion: AuthenticationPolicyVersion),
        IdentityConfigurationFamily.RegistrationContent when AssuranceScope is null && AuthenticationPolicyVersion is null && !string.IsNullOrWhiteSpace(RegistrationPolicyVersion) => new(Family, RegistrationPolicyVersion: RegistrationPolicyVersion),
        IdentityConfigurationFamily.TotpContent when AssuranceScope is null && AuthenticationPolicyVersion is null && RegistrationPolicyVersion is null && !string.IsNullOrWhiteSpace(TotpPolicyVersion) => new(Family, TotpPolicyVersion: TotpPolicyVersion),
        IdentityConfigurationFamily.PasskeyContent when AssuranceScope is null && AuthenticationPolicyVersion is null && RegistrationPolicyVersion is null && !string.IsNullOrWhiteSpace(PasskeyPolicyVersion) => new(Family, PasskeyPolicyVersion: PasskeyPolicyVersion),
        IdentityConfigurationFamily.StepUpContent when AssuranceScope is not null && AuthenticationPolicyVersion is null && RegistrationPolicyVersion is null && !string.IsNullOrWhiteSpace(AssurancePolicyVersion) => new(Family, AssuranceScope.Value(), AssurancePolicyVersion: AssurancePolicyVersion),
        _ => throw new ArgumentException("An exact typed Identity configuration scope is required.")
        };
    }
    internal static AdminIdentityConfigurationScope From(IdentityConfigurationScope scope) => new()
    { Family = scope.Family, AssuranceScope = scope.AssuranceScope is { } assurance ? new() { Operation = assurance.Operation, Context = assurance.Context } : null,
        AuthenticationPolicyVersion = scope.AuthenticationPolicyVersion, RegistrationPolicyVersion = scope.RegistrationPolicyVersion,
        TotpPolicyVersion = scope.TotpPolicyVersion, PasskeyPolicyVersion = scope.PasskeyPolicyVersion, AssurancePolicyVersion = scope.AssurancePolicyVersion };
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityAssuranceScope
{
    public required string Operation { get; init; }
    public required string Context { get; init; }
    internal AssurancePolicyScope Value() => new(Operation, Context);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationInterval
{
    public required DateTimeOffset From { get; init; }
    public required bool IncludesFrom { get; init; }
    public DateTimeOffset? Until { get; init; }
    public required bool IncludesUntil { get; init; }
    internal EffectivePolicyInterval Value() => new(From, IncludesFrom, Until, IncludesUntil);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityPolicyMetadata<TScope> where TScope : notnull
{
    public required string VersionId { get; init; }
    public required string Status { get; init; }
    public required TScope Scope { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    public string? SupersedesVersionId { get; init; }
    public required IReadOnlyList<string> ApprovalEvidenceReferences { get; init; }
    internal PolicyVersionCandidate<T> Value<T>(Func<TScope, T> project) where T : notnull => new(VersionId, Status, project(Scope),
        (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value(), SupersedesVersionId, ApprovalEvidenceReferences);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminAuthenticationConfigurationCandidate
{
    public required AdminIdentityPolicyMetadata<string> Metadata { get; init; }
    public required AuthenticationPolicy Values { get; init; }
    internal AuthenticationPolicyVersion Value() => new((Metadata ?? throw new ArgumentException("Metadata is required.")).Value(static x => x), Values ?? throw new ArgumentException("Values are required."));
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminRegistrationConfigurationCandidate
{
    public required AdminIdentityPolicyMetadata<string> Metadata { get; init; }
    public required RegistrationPolicy Values { get; init; }
    internal RegistrationPolicyVersion Value()
    {
        var values = Values ?? throw new ArgumentException("Values are required.");
        try { values.Validate(); }
        catch (InvalidOperationException error) { throw new ArgumentException("Unsupported registration candidate values.", nameof(Values), error); }
        return new((Metadata ?? throw new ArgumentException("Metadata is required.")).Value(static x => x), values);
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminPasskeyConfigurationCandidate
{
    public required AdminIdentityPolicyMetadata<string> Metadata { get; init; }
    public required PasskeyConfigurationValues Values { get; init; }
    internal PasskeyConfigurationVersion Value()
    {
        var values = Values ?? throw new ArgumentException("Values are required."); _ = values.ToPolicy();
        return new((Metadata ?? throw new ArgumentException("Metadata is required.")).Value(static x => x), values);
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminTotpConfigurationCandidate
{
    public required AdminIdentityPolicyMetadata<string> Metadata { get; init; }
    public required TotpPolicy Values { get; init; }
    internal TotpPolicyVersion Value()
    {
        var values = Values ?? throw new ArgumentException("Values are required.");
        try { values.Validate(); }
        catch (InvalidOperationException error) { throw new ArgumentException("Unsupported TOTP candidate values.", nameof(Values), error); }
        return new((Metadata ?? throw new ArgumentException("Metadata is required.")).Value(static x => x), values);
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminAssuranceConfigurationCandidate
{
    public required AdminIdentityPolicyMetadata<AdminIdentityAssuranceScope> Metadata { get; init; }
    public required string RequiredAssurance { get; init; }
    public required TimeSpan ProofLifetime { get; init; }
    public required IReadOnlyDictionary<string, string> MethodAssurances { get; init; }
    internal AssurancePolicyVersion Value() => new((Metadata ?? throw new ArgumentException("Metadata is required.")).Value(static x => (x ?? throw new ArgumentException("Scope is required.")).Value()), RequiredAssurance, ProofLifetime, MethodAssurances);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminAuthenticationContentBindingCandidate
{
    public required string AuthenticationPolicyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    public required Uri TermsUri { get; init; }
    public required Uri PrivacyUri { get; init; }
    public Uri? SupportUri { get; init; }
    internal AuthenticationContentBinding Value() => new(AuthenticationPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope,
        VariantReference, (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value(), TermsUri, PrivacyUri, SupportUri);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminRegistrationContentBindingCandidate
{
    public required string RegistrationPolicyVersion { get; init; }
    public required string TermsVersion { get; init; }
    public required string PrivacyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    public required Uri TermsUri { get; init; }
    public required Uri PrivacyUri { get; init; }
    public Uri? SupportUri { get; init; }
    internal RegistrationContentBinding Value()
    {
        if (new[] { RegistrationPolicyVersion, TermsVersion, PrivacyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace) ||
            TermsUri is null || PrivacyUri is null || new[] { TermsUri, PrivacyUri, SupportUri }.OfType<Uri>().Any(uri => !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0))
            throw new ArgumentException("Complete qualified Registration content references and HTTPS links are required.");
        return new(RegistrationPolicyVersion, TermsVersion, PrivacyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value(), TermsUri, PrivacyUri, SupportUri);
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminTotpContentBindingCandidate
{
    public required string TotpPolicyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    internal TotpContentBinding Value()
    {
        if (new[] { TotpPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Complete qualified TOTP content references are required.");
        return new(TotpPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminPasskeyContentBindingCandidate
{
    public required string PasskeyPolicyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    internal PasskeyContentBinding Value()
    {
        if (new[] { PasskeyPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Complete qualified Passkey content references are required.");
        return new(PasskeyPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminStepUpContentBindingCandidate
{
    public required AdminIdentityAssuranceScope AssuranceScope { get; init; }
    public required string AssurancePolicyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    internal StepUpContentBinding Value()
    {
        if (new[] { AssurancePolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Complete qualified operation proof content references are required.");
        return new((AssuranceScope ?? throw new ArgumentException("An exact assurance scope is required.")).Value(), AssurancePolicyVersion,
            ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminSessionBulkRevocationContentBindingCandidate
{
    public required string AuthenticationPolicyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    internal SessionActionContentBinding Value()
    {
        if (new[] { AuthenticationPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Complete qualified bulk session revocation content references are required.");
        return new(AuthenticationPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminSessionRevocationContentBindingCandidate
{
    public required string AuthenticationPolicyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    internal SessionActionContentBinding Value()
    {
        if (new[] { AuthenticationPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Complete qualified session revocation content references are required.");
        return new(AuthenticationPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminSessionListingContentBindingCandidate
{
    public required string AuthenticationPolicyVersion { get; init; }
    public required string ConfigurationVersionReference { get; init; }
    public required string QualificationReference { get; init; }
    public required string ContentScope { get; init; }
    public required string VariantReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    internal SessionListingContentBinding Value()
    {
        if (new[] { AuthenticationPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Complete qualified session listing content references are required.");
        return new(AuthenticationPolicyVersion, ConfigurationVersionReference, QualificationReference, ContentScope, VariantReference,
            (EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminSessionListingConfigurationCandidate
{
    public required AdminIdentityPolicyMetadata<string> Metadata { get; init; }
    public required int MaximumRows { get; init; }
    public required int MaximumRequestBytes { get; init; }
    internal SessionListingHttpBudget Value() => new((Metadata ?? throw new ArgumentException("Metadata is required.")).Value(static x => x), MaximumRows, MaximumRequestBytes);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminRecoveryCodeConfigurationCandidate
{
    public required AdminIdentityPolicyMetadata<string> Metadata { get; init; }
    public required RecoveryCodeConfigurationValues Values { get; init; }
    internal RecoveryCodeConfigurationVersion Value()
    {
        var values = Values ?? throw new ArgumentException("Values are required.");
        try { _ = values.ToPolicy(); }
        catch (InvalidOperationException error) { throw new ArgumentException("Unsupported recovery-code candidate values.", nameof(Values), error); }
        return new((Metadata ?? throw new ArgumentException("Metadata is required.")).Value(static x => x), values);
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationDraft<T>
{
    public required Guid IdempotencyKey { get; init; }
    public Guid? ProposalReference { get; init; }
    public long? ExpectedRevision { get; init; }
    public required T Candidate { get; init; }
    internal void RequireTarget()
    {
        if (Candidate is null || (ProposalReference is null ? ExpectedRevision is not null : ProposalReference == Guid.Empty || ExpectedRevision is null or < 1))
            throw new ArgumentException("An exact draft target and candidate are required.");
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationSearch
{
    public required AdminIdentityConfigurationScope Scope { get; init; }
    public string? Search { get; init; }
    public required int PageSize { get; init; }
    public required int Offset { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationHistoryQuery
{
    public required AdminIdentityConfigurationScope Scope { get; init; }
    public required int PageSize { get; init; }
    public required int Offset { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationHistoryPreviewQuery
{
    public required AdminIdentityConfigurationScope Scope { get; init; }
    public required Guid PublicationReference { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationProposalQuery
{
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationWrite
{
    public required Guid IdempotencyKey { get; init; }
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminPublishIdentityConfiguration
{
    public required Guid IdempotencyKey { get; init; }
    public required Guid ProposalReference { get; init; }
    public required long ExpectedRevision { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
    public Guid? SupersedesPublicationReference { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminRollbackIdentityConfiguration
{
    public required Guid IdempotencyKey { get; init; }
    public required Guid ApprovedVersionReference { get; init; }
    public required Guid SupersedesPublicationReference { get; init; }
    public required AdminIdentityConfigurationInterval EffectiveInterval { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminIdentityConfigurationCapabilityQuery
{
    public required AdminIdentityConfigurationScope Scope { get; init; }
    public required IdentityConfigurationCapabilityTarget Target { get; init; }
    public Guid? ProposalReference { get; init; }
    public long? ExpectedRevision { get; init; }
    public Guid? PublicationReference { get; init; }
}
