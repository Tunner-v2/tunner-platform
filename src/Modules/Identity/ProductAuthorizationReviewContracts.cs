using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Owner-validated presentation facts. Names/content come from an explicit publication source.</summary>
public sealed record ProductAuthorizationPresentationFacts(string RequestDigest, string ProductId, long ProductVersion,
    string Environment, long EnvironmentVersion, string ClientId, long ClientVersion, string? AccountId, long? AccountVersion,
    Guid? SessionId, string Origin, string RegisteredReturnOrigin, ImmutableArray<string> Scopes,
    ImmutableArray<string> RequestedAcrValues, ImmutableArray<string> IssuedClaimNames, string AuthorizationPolicyVersion)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool LoginOnly { get; init; }
    [JsonIgnore] public string Digest => AuthenticationIdentity.Digest(JsonSerializer.Serialize(this));
    public override string ToString() => nameof(ProductAuthorizationPresentationFacts);
}

/// <summary>Versioned publication and explicit private-review transport qualification; no Product or policy defaults.</summary>
public sealed record ProductAuthorizationPresentation(PolicyVersionCandidate<string> Metadata, string FactsDigest,
    string ProductName, string? AccountLabel, string RegisteredReturnOrigin, string? RequestedAssuranceLabel,
    string Locale, ImmutableArray<RegistrationPublishedText> ScopeDescriptions, ImmutableArray<RegistrationPublishedText> Content,
    Uri TermsUri, Uri PrivacyUri, Uri? SupportUri, string EvidenceReference, string ProcessingReference,
    string RetentionReference, string Classification, TimeSpan ReviewLifetime, int MaximumDecisionBytes, int MaximumReferenceBytes)
{
    private static readonly string[] RequiredKeys = ["brand.tunner", "authorization.heading", "authorization.description",
        "authorization.product", "authorization.account", "authorization.permissions", "authorization.assurance",
        "authorization.callback", "authorization.allow", "authorization.deny", "authorization.switchAccount",
        "authorization.working", "authorization.changed", "authorization.unavailable", "authorization.proofRequired",
        "authorization.loginRequired", "authorization.boundary", "authorization.footer.privacy",
        "authorization.footer.terms", "authorization.footer.support"];
    private static readonly string[] LoginKeys = ["brand.tunner", "authorization.heading", "authorization.loginDescription",
        "authorization.loginPermissions", "authorization.loginSummary", "authorization.boundary"];
    internal string Snapshot => JsonSerializer.Serialize(this with
    {
        ScopeDescriptions = ScopeDescriptions.OrderBy(text => text.Key, StringComparer.Ordinal).ToImmutableArray(),
        Content = Content.OrderBy(text => text.Key, StringComparer.Ordinal).ToImmutableArray()
    });

    internal void Require(ProductAuthorizationPresentationFacts facts, DateTimeOffset at)
    {
        if (facts.LoginOnly || string.IsNullOrWhiteSpace(facts.AccountId) || facts.AccountVersion is not > 0 ||
            facts.SessionId is not { } session || session == Guid.Empty || string.IsNullOrWhiteSpace(AccountLabel) ||
            AccountLabel.Any(char.IsControl) || facts.RequestedAcrValues.Length > 0 && string.IsNullOrWhiteSpace(RequestedAssuranceLabel))
            throw new InvalidOperationException("Product review requires exact current Account and session presentation facts.");
        RequireCommon(facts, at, RequiredKeys);
    }

    internal void RequireLogin(ProductAuthorizationPresentationFacts facts, DateTimeOffset at)
    {
        if (!facts.LoginOnly || facts.AccountId is not null || facts.AccountVersion is not null || facts.SessionId is not null ||
            AccountLabel is not null || facts.IssuedClaimNames.IsDefaultOrEmpty == false)
            throw new InvalidOperationException("Product login context must not assert Account, session or issued-claim facts.");
        RequireCommon(facts, at, LoginKeys);
    }

    private void RequireCommon(ProductAuthorizationPresentationFacts facts, DateTimeOffset at, string[] requiredKeys)
    {
        if (Metadata is null || Metadata.Scope != facts.Origin || Metadata.Status != "ACTIVE" ||
            string.IsNullOrWhiteSpace(Metadata.VersionId) || !Metadata.EffectiveInterval.Contains(at) || FactsDigest != facts.Digest ||
            new[] { ProductName, Locale, EvidenceReference, ProcessingReference, RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            ProductName.Any(char.IsControl) ||
            RegisteredReturnOrigin != facts.RegisteredReturnOrigin ||
            !Uri.TryCreate(RegisteredReturnOrigin, UriKind.Absolute, out var callback) || callback.Scheme != Uri.UriSchemeHttps ||
            callback.UserInfo.Length != 0 || callback.GetLeftPart(UriPartial.Authority) != RegisteredReturnOrigin ||
            RequestedAssuranceLabel?.Any(char.IsControl) == true ||
            Classification is not ("INTERNAL" or "CONFIDENTIAL" or "RESTRICTED_PERSONAL" or "RESTRICTED_SECURITY") ||
            ReviewLifetime <= TimeSpan.Zero || ReviewLifetime.Ticks > DateTimeOffset.MaxValue.UtcTicks - at.UtcTicks ||
            MaximumDecisionBytes <= 0 || MaximumReferenceBytes <= 0 ||
            MaximumReferenceBytes > MaximumDecisionBytes || ScopeDescriptions.IsDefault || Content.IsDefault)
            throw new InvalidOperationException("Current explicit Product authorization publication is unavailable.");
        RequirePublished(ScopeDescriptions); RequirePublished(Content);
        if (!ScopeDescriptions.Select(text => text.Key).Order(StringComparer.Ordinal).SequenceEqual(facts.Scopes, StringComparer.Ordinal) ||
            requiredKeys.Any(key => !Content.Any(text => text.Key == key)))
            throw new InvalidOperationException("Published descriptions for the exact requested Product scopes and review controls are required.");
        foreach (var uri in new[] { TermsUri, PrivacyUri, SupportUri }.OfType<Uri>())
            if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
                throw new InvalidOperationException("Published credential-free HTTPS links are required.");
        if (TermsUri is null || PrivacyUri is null)
            throw new InvalidOperationException("Published terms and privacy links are required.");
    }

    private void RequirePublished(ImmutableArray<RegistrationPublishedText> values)
    {
        if (values.Any(text => text is null || text.State != "PUBLISHED" || text.Locale != Locale ||
                new[] { text.Key, text.VariantRef, text.VersionRef, text.PublicationRef, text.Text }.Any(string.IsNullOrWhiteSpace)) ||
            values.Select(text => text.Key).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException("Review text must be published for the exact locale with provenance.");
    }
    public override string ToString() => nameof(ProductAuthorizationPresentation);
}

/// <summary>Anonymous Product-origin login display only; no Account, authorization reference or authentication claims.</summary>
internal sealed record ProductAuthorizationLoginContext(string ProductName, string Locale,
    IReadOnlyDictionary<string, RegistrationPublishedText> Content,
    IReadOnlyDictionary<string, RegistrationPublishedText> ScopeDescriptions)
{
    public override string ToString() => nameof(ProductAuthorizationLoginContext);
}

public interface IProductAuthorizationPresentationSource
{
    ValueTask<ProductAuthorizationPresentation> RequirePublishedAsync(ProductAuthorizationPresentationFacts facts,
        DateTimeOffset at, CancellationToken cancellationToken);
}
