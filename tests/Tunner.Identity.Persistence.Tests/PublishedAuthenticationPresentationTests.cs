using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedAuthenticationPresentationTests
{
    [Fact]
    public void MapsPublishedBundleWithoutLosingVersionPublicationOrFallbackProvenance()
    {
        var fixture = new Fixture();
        var result = fixture.Source.RequirePublished(fixture.Policy);

        Assert.Equal(fixture.Bundle.Locale, result.Locale);
        Assert.Equal(fixture.Binding.TermsUri, result.TermsUri);
        Assert.Equal(fixture.Binding.PrivacyUri, result.PrivacyUri);
        Assert.Equal(fixture.Bundle.Content.Count, result.Content.Count);
        foreach (var (key, value) in fixture.Bundle.Content)
        {
            var actual = result.Content[key];
            Assert.Equal(value.VariantReference, actual.VariantRef);
            Assert.Equal(value.VersionReference, actual.VersionRef);
            Assert.Equal(value.PublicationReference, actual.PublicationRef);
            Assert.Equal(value.Text, actual.Text);
            Assert.Equal("PUBLISHED", actual.State);
        }
        Assert.Equal(new ContentResolutionRequest(fixture.Binding.ContentScope, fixture.Binding.VariantReference,
            fixture.Clock.GetUtcNow()), fixture.Resolver.LastRequest);
    }

    [Theory]
    [InlineData("another-policy")]
    [InlineData("expired")]
    [InlineData("unqualified")]
    public void RejectsUnqualifiedOrWrongPolicyConfigurationBeforeReadingContent(string condition)
    {
        var fixture = new Fixture();
        fixture.Binding = condition switch
        {
            "another-policy" => fixture.Binding with { AuthenticationPolicyVersion = "fixture-other-policy" },
            "expired" => fixture.Binding with
            {
                EffectiveInterval = new EffectivePolicyInterval(fixture.Clock.GetUtcNow().AddMinutes(-1), true,
                    fixture.Clock.GetUtcNow(), false)
            },
            _ => fixture.Binding with { QualificationReference = "" }
        };

        Assert.Throws<InvalidOperationException>(() => fixture.Source.RequirePublished(fixture.Policy));
        Assert.Null(fixture.Resolver.LastRequest);
    }

    [Theory]
    [InlineData("cross-scope")]
    [InlineData("mixed-locale")]
    [InlineData("missing-publication")]
    [InlineData("missing-login-key")]
    public void RejectsInvalidOwnerProjectionAndReturnsTheExistingSafeDependencyFailure(string condition)
    {
        var fixture = new Fixture();
        var values = fixture.Bundle.Content.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (condition == "mixed-locale")
            values["login.heading"] = values["login.heading"] with { Locale = "fixture-other-locale" };
        if (condition == "missing-publication")
            values["login.heading"] = values["login.heading"] with { PublicationReference = "" };
        if (condition == "missing-login-key")
            values.Remove("login.heading");
        fixture.Resolver.Result = fixture.Bundle with
        {
            Scope = condition == "cross-scope" ? "fixture-other-scope" : fixture.Bundle.Scope,
            Content = values
        };

        Assert.Throws<InvalidOperationException>(() => fixture.Source.RequirePublished(fixture.Policy));
    }

    [Fact]
    public void ExplicitCompositionProvidesThePublishedSourceWithoutReplacingAnExistingProvider()
    {
        var fixture = new Fixture();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(fixture.Clock);
        services.AddSingleton<IPresentationContentResolver>(fixture.Resolver);
        services.AddSingleton<IAuthenticationContentBindingSource>(fixture);
        services.AddTunnerPublishedAuthenticationPresentation();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IAuthenticationPresentationSource>();
        Assert.IsType<PublishedAuthenticationPresentationSource>(source);
        Assert.Equal(fixture.Bundle.Content.Count, source.RequirePublished(fixture.Policy).Content.Count);
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedAuthenticationPresentation());
    }

    private sealed class Fixture : IAuthenticationContentBindingSource
    {
        internal readonly AuthenticationTestHost.TestClock Clock = new();
        internal readonly AuthenticationPolicy Policy = new AuthenticationTestHost.TestPolicies().Policy;
        internal readonly Resolver Resolver;
        internal readonly ContentResolution Bundle;
        internal AuthenticationContentBinding Binding;
        internal PublishedAuthenticationPresentationSource Source => new(Resolver, this, Clock);

        internal Fixture()
        {
            var existing = new AuthenticationTestHost.TestPresentation().RequirePublished(Policy);
            var values = existing.Content.Values.ToDictionary(item => item.Key, item =>
                new PublishedContent(item.Key, "fixture-fallback", "fixture-version-" + item.Key,
                    "fixture-publication-" + item.Key, existing.Locale, item.Text), StringComparer.Ordinal);
            Bundle = new ContentResolution("fixture-auth-content", "fixture-primary", "fixture-fallback", existing.Locale, values);
            Resolver = new Resolver(Bundle);
            Binding = new AuthenticationContentBinding(Policy.Version, "fixture-config-version", "fixture-only-qualification",
                Bundle.Scope, Bundle.RequestedVariantReference,
                new EffectivePolicyInterval(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false),
                existing.TermsUri, existing.PrivacyUri, existing.SupportUri);
        }

        public AuthenticationContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt) => Binding;
    }

    private sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result;
        internal ContentResolutionRequest? LastRequest;
        public ContentResolution Resolve(ContentResolutionRequest request)
        {
            LastRequest = request;
            return Result;
        }
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Resolve(request));
        }
    }
}
