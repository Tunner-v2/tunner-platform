using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedRegistrationPresentationTests
{
    [Fact]
    public void MapsCompleteExistingBundleAndPreservesExactFallbackPublicationAndNoticeBinding()
    {
        var fixture = new Fixture(); var result = fixture.Source.RequirePublished(fixture.Policy);
        Assert.Equal(fixture.Bundle.Locale, result.Locale); Assert.Equal(fixture.Binding.TermsUri, result.TermsUri);
        Assert.Equal(fixture.Binding.PrivacyUri, result.PrivacyUri); Assert.Equal(fixture.Binding.SupportUri, result.SupportUri);
        Assert.Equal(fixture.Bundle.Content.Count, result.Content.Count);
        foreach (var (key, item) in fixture.Bundle.Content)
        {
            var actual = result.Content[key]; Assert.Equal(item.VariantReference, actual.VariantRef); Assert.Equal(item.VersionReference, actual.VersionRef);
            Assert.Equal(item.PublicationReference, actual.PublicationRef); Assert.Equal(item.Locale, actual.Locale); Assert.Equal(item.Text, actual.Text); Assert.Equal("PUBLISHED", actual.State);
        }
        Assert.Equal(new ContentResolutionRequest(fixture.Binding.ContentScope, fixture.Binding.VariantReference, fixture.Clock.GetUtcNow()), fixture.Resolver.LastRequest);
        Assert.Equal(fixture.Policy, fixture.BindingRequest!.Value.Policy); Assert.Equal(fixture.Clock.GetUtcNow(), fixture.BindingRequest.Value.At);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("terms")]
    [InlineData("privacy")]
    [InlineData("expired")]
    [InlineData("qualification")]
    [InlineData("missing-terms")]
    [InlineData("missing-privacy")]
    [InlineData("credential-link")]
    public void RejectsUnqualifiedPolicyNoticeOrLinkBindingBeforeReadingContent(string failure)
    {
        var fixture = new Fixture(); fixture.Binding = failure switch
        {
            "policy" => fixture.Binding with { RegistrationPolicyVersion = "fixture-other-policy" },
            "terms" => fixture.Binding with { TermsVersion = "fixture-other-terms" },
            "privacy" => fixture.Binding with { PrivacyVersion = "fixture-other-privacy" },
            "expired" => fixture.Binding with { EffectiveInterval = new(fixture.Clock.GetUtcNow().AddMinutes(-1), true, fixture.Clock.GetUtcNow(), false) },
            "qualification" => fixture.Binding with { QualificationReference = "" },
            "missing-terms" => fixture.Binding with { TermsUri = null! },
            "missing-privacy" => fixture.Binding with { PrivacyUri = null! },
            _ => fixture.Binding with { TermsUri = new("https://fixture:private@fixture.example.test/terms") }
        };
        Assert.Throws<InvalidOperationException>(() => fixture.Source.RequirePublished(fixture.Policy)); Assert.Null(fixture.Resolver.LastRequest);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("requested-variant")]
    [InlineData("locale")]
    [InlineData("effective-variant")]
    [InlineData("dictionary-key")]
    [InlineData("publication")]
    [InlineData("registration.heading")]
    [InlineData("verification.heading")]
    [InlineData("personal.birth.label")]
    [InlineData("consent.heading")]
    public void RejectsCrossContextOrIncompleteExistingPresentationWithoutSyntheticFallback(string failure)
    {
        var fixture = new Fixture(); var content = fixture.Bundle.Content.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (failure == "locale") content["registration.heading"] = content["registration.heading"] with { Locale = "fixture-other-locale" };
        if (failure == "effective-variant") content["registration.heading"] = content["registration.heading"] with { VariantReference = "fixture-other-variant" };
        if (failure == "dictionary-key") content["registration.heading"] = content["registration.heading"] with { Key = "fixture-other-key" };
        if (failure == "publication") content["registration.heading"] = content["registration.heading"] with { PublicationReference = "" };
        if (failure.Contains('.', StringComparison.Ordinal)) content.Remove(failure);
        fixture.Resolver.Result = fixture.Bundle with { Content = content,
            Scope = failure == "scope" ? "fixture-other-scope" : fixture.Bundle.Scope,
            RequestedVariantReference = failure == "requested-variant" ? "fixture-other-request" : fixture.Bundle.RequestedVariantReference };
        Assert.Throws<InvalidOperationException>(() => fixture.Source.RequirePublished(fixture.Policy));
    }

    [Fact]
    public void PresentationOptInDoesNotRegisterPolicyOrWorkflowAndRejectsExistingProvider()
    {
        var fixture = new Fixture(); var services = CompleteServices(fixture); services.AddTunnerPublishedRegistrationPresentation();
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IRegistrationPolicySource) || x.ServiceType == typeof(IRegistrationApplication));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.IsType<PublishedRegistrationPresentationSource>(scope.ServiceProvider.GetRequiredService<IRegistrationPresentationSource>());
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedRegistrationPresentation());
        var existing = CompleteServices(fixture); existing.AddSingleton<IRegistrationPresentationSource, RegistrationBffTests.Presentation>();
        Assert.Throws<InvalidOperationException>(() => existing.AddTunnerPublishedRegistrationPresentation());
    }

    [Fact]
    public void MissingQualifiedDependencyDoesNotInstallPresentation()
    {
        var fixture = new Fixture();
        foreach (var missing in new[] { typeof(IPresentationContentResolver), typeof(IRegistrationContentBindingSource), typeof(TimeProvider) })
        {
            var services = CompleteServices(fixture); services.Remove(services.Single(x => x.ServiceType == missing));
            Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedRegistrationPresentation());
            Assert.DoesNotContain(services, x => x.ServiceType == typeof(IRegistrationPresentationSource));
        }
    }

    private static ServiceCollection CompleteServices(Fixture fixture)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(fixture.Clock);
        services.AddSingleton<IPresentationContentResolver>(fixture.Resolver); services.AddSingleton<IRegistrationContentBindingSource>(fixture); return services;
    }
    private sealed class Fixture : IRegistrationContentBindingSource
    {
        internal readonly AuthenticationTestHost.TestClock Clock = new();
        // Existing RegistrationBffTests synthetic shape; no production policy or legal values are selected.
        internal readonly RegistrationPolicy Policy = new("fixture-registration-v2", "PDR-0011", "fixture-processing", "fixture-retention", "fixture-terms", "fixture-privacy",
            "fixture-proof", "fixture-existing", "PASSWORD_AND_VERIFIED_EMAIL", 15, 128, 10000, 6, TimeSpan.FromMinutes(5), 2, TimeSpan.FromSeconds(30), 2,
            TimeSpan.FromMinutes(1), 1000, 1000, 1000, 1000, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
        internal readonly ContentResolution Bundle;
        internal readonly Resolver Resolver;
        internal RegistrationContentBinding Binding;
        internal (RegistrationPolicy Policy, DateTimeOffset At)? BindingRequest;
        internal PublishedRegistrationPresentationSource Source => new(Resolver, this, Clock);
        internal Fixture()
        {
            var existing = new RegistrationBffTests.Presentation().RequirePublished(Policy);
            var content = existing.Content.Values.ToDictionary(item => item.Key, item => new PublishedContent(item.Key, "fixture-registration-fallback",
                "fixture-version-" + item.Key, "fixture-publication-" + item.Key, existing.Locale, item.Text), StringComparer.Ordinal);
            Bundle = new("fixture-registration-content", "fixture-registration-request", "fixture-registration-fallback", existing.Locale, content);
            Resolver = new(Bundle); Binding = new(Policy.Version, Policy.TermsVersion, Policy.PrivacyVersion, "fixture-registration-config", "fixture-registration-qualification",
                Bundle.Scope, Bundle.RequestedVariantReference, new(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false), existing.TermsUri, existing.PrivacyUri, existing.SupportUri);
        }
        public RegistrationContentBinding RequireQualified(RegistrationPolicy policy, DateTimeOffset effectiveAt)
        { BindingRequest = (policy, effectiveAt); return Binding; }
    }
    private sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result;
        internal ContentResolutionRequest? LastRequest;
        public ContentResolution Resolve(ContentResolutionRequest request) { LastRequest = request; return Result; }
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Resolve(request)); }
    }
}
