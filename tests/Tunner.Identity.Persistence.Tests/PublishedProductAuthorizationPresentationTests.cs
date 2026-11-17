using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedProductAuthorizationPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactOwnerSemanticsAndContentProvenanceArePreservedForReviewAndAnonymousLogin(bool login)
    {
        var f = new Fixture(login); var result = await f.Source().RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(f.Binding.Semantics.ProductName, result.ProductName); Assert.Equal(f.Binding.Semantics.AccountLabel, result.AccountLabel);
        Assert.Equal(f.Binding.Semantics.TermsUri, result.TermsUri); Assert.Equal(f.Binding.Semantics.PrivacyUri, result.PrivacyUri);
        Assert.Equal(f.Binding.Semantics.ProcessingReference, result.ProcessingReference); Assert.Equal(f.Binding.Semantics.RetentionReference, result.RetentionReference);
        Assert.Equal(f.Facts.Digest, result.FactsDigest); Assert.Equal(f.Facts.Scopes, result.ScopeDescriptions.Select(text => text.Key));
        foreach (var text in result.Content)
        {
            var expected = f.Resolver.Result.Content[text.Key]; Assert.Equal(expected.Text, text.Text); Assert.Equal(expected.PublicationReference, text.PublicationRef);
            Assert.Equal(expected.VersionReference, text.VersionRef); Assert.Equal(expected.VariantReference, text.VariantRef); Assert.Equal(expected.Locale, text.Locale);
        }
        Assert.Equal(2, f.Reads); Assert.Same(f.Facts, f.LastFacts);
        if (login) { Assert.Null(result.AccountLabel); Assert.Null(f.Facts.AccountId); Assert.Empty(f.Facts.IssuedClaimNames); }
    }

    [Theory]
    [InlineData("facts")]
    [InlineData("origin")]
    [InlineData("product-name")]
    [InlineData("account-label")]
    [InlineData("callback")]
    [InlineData("terms")]
    [InlineData("expired")]
    public async Task MissingOrSubstitutedSemanticAuthorityFailsClosed(string failure)
    {
        var f = new Fixture(); var s = f.Binding.Semantics;
        s = failure switch
        {
            "facts" => s with { FactsDigest = new string('b', 64) },
            "origin" => s with { Metadata = new(s.Metadata.VersionId, "ACTIVE", "https://other.example.test", f.Interval, null, ["SYNTHETIC-ONLY"]) },
            "product-name" => s with { ProductName = "" },
            "account-label" => s with { AccountLabel = null },
            "callback" => s with { RegisteredReturnOrigin = "https://other.example.test" },
            "terms" => s with { TermsUri = new("http://fixture.example.test/terms") },
            _ => s with { Metadata = new(s.Metadata.VersionId, "ACTIVE", f.Facts.Origin, new(f.Clock.GetUtcNow().AddMinutes(-1), true, f.Clock.GetUtcNow(), false), null, ["SYNTHETIC-ONLY"]) }
        };
        f.Binding = f.Binding with { Semantics = s };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Source().RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None));
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("variant")]
    [InlineData("locale")]
    [InlineData("publication")]
    [InlineData("bounds")]
    [InlineData("permission")]
    [InlineData("required-copy")]
    public async Task IncompleteOrCrossContextPublicationIsRejected(string failure)
    {
        var f = new Fixture(); var r = f.Resolver.Result; var content = r.Content.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (failure == "locale") content["authorization.heading"] = content["authorization.heading"] with { Locale = "fr" };
        if (failure == "publication") content["authorization.heading"] = content["authorization.heading"] with { PublicationReference = "" };
        if (failure == "permission") content.Remove("product.read");
        if (failure == "required-copy") content.Remove("authorization.allow");
        f.Resolver.Result = r with { Scope = failure == "scope" ? "fixture-other" : r.Scope,
            RequestedVariantReference = failure == "variant" ? "fixture-other" : r.RequestedVariantReference,
            EffectiveInterval = failure == "bounds" ? null : r.EffectiveInterval, Content = content };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Source().RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None));
    }

    [Fact]
    public async Task ContentAvailabilityClipsSemanticIntervalWithExactBoundaryInclusion()
    {
        var f = new Fixture(); var end = f.Clock.GetUtcNow().AddSeconds(1);
        f.Resolver.Result = f.Resolver.Result with { EffectiveInterval = new(f.Clock.GetUtcNow(), true, end, false) };
        var result = await f.Source().RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(end, result.Metadata.EffectiveInterval.Until); Assert.False(result.Metadata.EffectiveInterval.IncludesUntil);
        Assert.False(result.Metadata.EffectiveInterval.Contains(end)); Assert.True(f.Interval.Contains(end));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalBindingAwaitCannotHideChangedSemanticsOrContentExpiry(bool expiry)
    {
        var f = new Fixture(); var end = f.Clock.GetUtcNow().AddSeconds(1);
        f.Resolver.Result = f.Resolver.Result with { EffectiveInterval = new(f.Clock.GetUtcNow(), true, end, false) };
        f.AfterFinalRead = () => { if (expiry) f.Clock.Advance(TimeSpan.FromSeconds(1)); else f.Binding = f.Binding with { Semantics = f.Binding.Semantics with { AccountLabel = "Changed qualified Account" } }; };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Source().RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None));
    }

    [Fact]
    public void ResolutionWitnessPreservesExistingTransportBytes()
    {
        var f = new Fixture(); var resolution = f.Resolver.Result with { EffectiveInterval = null };
        Assert.Equal(JsonSerializer.Serialize(resolution), JsonSerializer.Serialize(resolution with { EffectiveInterval = f.Interval }));
    }

    [Fact]
    public void ExplicitOptInInstallsOnlyPresentationAndRequiresAllQualifiedDependencies()
    {
        var f = new Fixture(); var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(f.Clock);
        services.AddSingleton<IPresentationContentResolver>(f.Resolver); services.AddSingleton<IProductAuthorizationPresentationContentBindingSource>(f);
        services.AddTunnerPublishedProductAuthorizationPresentation();
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IProductAuthorizationPolicySource) || service.ServiceType == typeof(IProductAuthorizationRegistrySource) || service.ServiceType == typeof(IProductAuthorizationRelationshipSource));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); Assert.IsType<PublishedProductAuthorizationPresentationSource>(scope.ServiceProvider.GetRequiredService<IProductAuthorizationPresentationSource>());
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedProductAuthorizationPresentation());
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTunnerPublishedProductAuthorizationPresentation());
    }

    internal sealed class Fixture : IProductAuthorizationPresentationContentBindingSource
    {
        internal readonly AuthenticationTestHost.TestClock Clock = new();
        internal ProductAuthorizationPresentationFacts Facts;
        internal readonly EffectivePolicyInterval Interval;
        internal ProductAuthorizationPresentationContentBinding Binding;
        internal readonly Resolver Resolver;
        internal int Reads; internal ProductAuthorizationPresentationFacts? LastFacts; internal Action? AfterFinalRead;
        internal Fixture(bool login = false)
        {
            Interval = new(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false);
            Facts = new(new string('a', 64), "fixture-product", 1, "SANDBOX", 1, "fixture-client", 1,
                login ? null : "fixture-account", login ? null : 1, login ? null : Guid.NewGuid(), AuthenticationTestHost.Origin,
                "https://fixture-product.example.test", ["openid", "product.read"], [], login ? [] : ["sub"], "fixture-authorization-policy") { LoginOnly = login };
            string[] keys = ["brand.tunner", "authorization.heading", "authorization.description", "authorization.product", "authorization.account", "authorization.permissions",
                "authorization.assurance", "authorization.callback", "authorization.allow", "authorization.deny", "authorization.switchAccount", "authorization.working",
                "authorization.changed", "authorization.unavailable", "authorization.proofRequired", "authorization.loginRequired", "authorization.boundary",
                "authorization.footer.privacy", "authorization.footer.terms", "authorization.footer.support", "authorization.loginDescription", "authorization.loginPermissions", "authorization.loginSummary", "authorization.verify"];
            var content = keys.Concat(Facts.Scopes).ToDictionary(key => key, key => new PublishedContent(key, "fixture-product-fallback", "fixture-version-" + key,
                "fixture-publication-" + key, "en", "Synthetic published UI only: " + key), StringComparer.Ordinal);
            Resolver = new(new("fixture-product-content", "fixture-requested-product", "fixture-product-fallback", "en", content) { EffectiveInterval = Interval });
            var semantics = new ProductAuthorizationPresentation(new("fixture-product-presentation-v1", "ACTIVE", Facts.Origin, Interval, null, ["SYNTHETIC-ONLY"]), Facts.Digest,
                "Synthetic qualified Product", login ? null : "Synthetic qualified Account", Facts.RegisteredReturnOrigin, null, "en", [], [],
                new("https://fixture.example.test/terms"), new("https://fixture.example.test/privacy"), null, "fixture-product-evidence", "fixture-product-processing", "fixture-product-retention",
                "RESTRICTED_SECURITY", TimeSpan.FromSeconds(30), 32768, 16384);
            Binding = new(semantics, Resolver.Result.Scope, Resolver.Result.RequestedVariantReference, "fixture-product-content-qualification");
        }
        internal PublishedProductAuthorizationPresentationSource Source(IPresentationContentResolver? resolver = null) => new(resolver ?? Resolver, this, Clock);
        public ValueTask<ProductAuthorizationPresentationContentBinding> RequireQualifiedAsync(ProductAuthorizationPresentationFacts facts, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Reads++; LastFacts = facts; if (Reads == 2) AfterFinalRead?.Invoke(); return ValueTask.FromResult(Binding); }
    }
    internal sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result;
        public ContentResolution Resolve(ContentResolutionRequest request) => Result;
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result); }
    }
}
