using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedSessionListingPresentationTests
{
    [Fact]
    public void ProjectsCompleteExistingBundleWithExactPolicyTimeAndFallbackProvenance()
    {
        var f = new Fixture(); var result = f.Source.RequirePublished(f.Policy); Assert.Equal(f.Bundle.Locale, result.Locale); Assert.Equal(f.Bundle.Content.Count, result.Content.Count);
        foreach (var (key, expected) in f.Bundle.Content)
        {
            var actual = result.Content[key]; Assert.Equal(expected.Text, actual.Text); Assert.Equal(expected.Locale, actual.Locale);
            Assert.Equal(expected.VariantReference, actual.VariantRef); Assert.Equal(expected.VersionReference, actual.VersionRef); Assert.Equal(expected.PublicationReference, actual.PublicationRef); Assert.Equal("PUBLISHED", actual.State);
        }
        Assert.Equal(new ContentResolutionRequest(f.Binding.ContentScope, f.Binding.VariantReference, f.Clock.GetUtcNow()), f.Resolver.LastRequest);
        Assert.Equal(f.Policy, f.BindingRequest!.Value.Policy); Assert.Equal(f.Clock.GetUtcNow(), f.BindingRequest.Value.At);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("configuration")]
    [InlineData("qualification")]
    [InlineData("scope")]
    [InlineData("variant")]
    [InlineData("future")]
    [InlineData("expired")]
    public void RejectsUnqualifiedBindingBeforeReadingContent(string failure)
    {
        var f = new Fixture(); f.Binding = failure switch
        {
            "policy" => f.Binding with { AuthenticationPolicyVersion = "fixture-other-policy" },
            "configuration" => f.Binding with { ConfigurationVersionReference = "" },
            "qualification" => f.Binding with { QualificationReference = "" },
            "scope" => f.Binding with { ContentScope = "" },
            "variant" => f.Binding with { VariantReference = "" },
            "future" => f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow().AddSeconds(1), true, null, false) },
            _ => f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow().AddMinutes(-1), true, f.Clock.GetUtcNow(), false) }
        };
        Assert.Throws<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy)); Assert.Null(f.Resolver.LastRequest);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("requested-variant")]
    [InlineData("locale")]
    [InlineData("effective-variant")]
    [InlineData("dictionary-key")]
    [InlineData("version")]
    [InlineData("publication")]
    [InlineData("text")]
    [InlineData("account.sessions.title")]
    [InlineData("account.sessions.details")]
    [InlineData("account.sessions.state.expired")]
    public void RejectsIncompleteOrCrossContextPublishedBundle(string failure)
    {
        var f = new Fixture(); var content = f.Bundle.Content.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); const string key = "account.sessions.title";
        content[key] = failure switch
        {
            "locale" => content[key] with { Locale = "fixture-other-locale" },
            "effective-variant" => content[key] with { VariantReference = "fixture-other-variant" },
            "dictionary-key" => content[key] with { Key = "fixture-other-key" },
            "version" => content[key] with { VersionReference = "" },
            "publication" => content[key] with { PublicationReference = "" },
            "text" => content[key] with { Text = "" },
            _ => content[key]
        };
        if (failure.StartsWith("account.sessions.", StringComparison.Ordinal)) content.Remove(failure);
        f.Resolver.Result = f.Bundle with { Content = content, Scope = failure == "scope" ? "fixture-other-scope" : f.Bundle.Scope,
            RequestedVariantReference = failure == "requested-variant" ? "fixture-other-request" : f.Bundle.RequestedVariantReference };
        Assert.Throws<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy));
    }

    [Fact]
    public void ResolverCrossingBindingExpiryFailsClosedAtTheActualReturnFrontier()
    {
        var f = new Fixture(); f.Binding = f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow(), true, f.Clock.GetUtcNow().AddSeconds(1), false) };
        f.Resolver.BeforeReturn = () => f.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy)); Assert.Equal(2, f.BindingReads);
    }

    [Fact]
    public void BindingChangedDuringResolutionCannotQualifyTheOriginalBundle()
    {
        var f = new Fixture(); f.Resolver.BeforeReturn = () => f.Binding = f.Binding with { QualificationReference = "changed-owner-qualification" };
        Assert.Throws<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy)); Assert.Equal(2, f.BindingReads);
    }

    [Fact]
    public void FinalBindingLookupCrossingExpiryFailsClosedWithoutAnotherLookupLoop()
    {
        var f = new Fixture(); f.Binding = f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow(), true, f.Clock.GetUtcNow().AddSeconds(1), false) };
        f.BeforeBindingRead = count => { if (count == 2) f.Clock.Advance(TimeSpan.FromSeconds(1)); };
        Assert.Throws<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy)); Assert.Equal(2, f.BindingReads);
    }

    [Fact]
    public void EquivalentReloadedIntervalValuesRemainQualified()
    {
        var f = new Fixture(); var original = f.Binding.EffectiveInterval;
        f.BeforeBindingRead = count => { if (count == 2) f.Binding = f.Binding with { EffectiveInterval = new(original.From, original.IncludesFrom, original.Until, original.IncludesUntil) }; };
        Assert.NotEmpty(f.Source.RequirePublished(f.Policy).Content); Assert.Equal(2, f.BindingReads);
    }

    [Fact]
    public void ExplicitOptInInstallsOnlyPresentationAndRejectsAnExistingProvider()
    {
        var f = new Fixture(); var services = Complete(f); services.AddTunnerPublishedSessionListingPresentation();
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IAuthenticationPolicySource) || x.ServiceType == typeof(ISessionListingHttpBudgetSource) || x.ServiceType == typeof(ISessionRevocationPresentationSource));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); Assert.IsType<PublishedSessionListingPresentationSource>(scope.ServiceProvider.GetRequiredService<ISessionListingPresentationSource>());
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedSessionListingPresentation());
        var existing = Complete(f); existing.AddSingleton<ISessionListingPresentationSource, ExistingPresentation>(); Assert.Throws<InvalidOperationException>(() => existing.AddTunnerPublishedSessionListingPresentation());
    }

    [Fact]
    public void MissingExplicitDependenciesCannotInstallPresentation()
    {
        var f = new Fixture();
        foreach (var missing in new[] { typeof(TimeProvider), typeof(ISessionListingContentBindingSource), typeof(IPresentationContentResolver) })
        {
            var services = Complete(f); services.Remove(services.Single(x => x.ServiceType == missing)); Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedSessionListingPresentation());
            Assert.DoesNotContain(services, x => x.ServiceType == typeof(ISessionListingPresentationSource));
        }
    }

    internal static AuthenticationPolicy TestPolicy() => new AuthenticationTestHost.TestPolicies().Policy;
    internal static SessionListingPresentation ExistingBundle() => new("en", SessionListingPresentation.RequiredKeys.Select(key =>
        new RegistrationPublishedText(key, "fixture-session-variant", "fixture-session-version", "fixture-only-publication", "en", "PUBLISHED", "Synthetic Sessions fixture text: " + key)));
    private sealed class ExistingPresentation : ISessionListingPresentationSource
    { public SessionListingPresentation RequirePublished(AuthenticationPolicy policy) => ExistingBundle(); }
    private static ServiceCollection Complete(Fixture f)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(f.Clock); services.AddSingleton<IPresentationContentResolver>(f.Resolver);
        services.AddSingleton<ISessionListingContentBindingSource>(f); return services;
    }
    private sealed class Fixture : ISessionListingContentBindingSource
    {
        internal readonly AuthenticationTestHost.TestClock Clock = new(); internal readonly AuthenticationPolicy Policy = TestPolicy();
        internal readonly ContentResolution Bundle; internal readonly Resolver Resolver;
        internal SessionListingContentBinding Binding; internal (AuthenticationPolicy Policy, DateTimeOffset At)? BindingRequest;
        internal int BindingReads; internal Action<int>? BeforeBindingRead;
        internal PublishedSessionListingPresentationSource Source => new(Resolver, this, Clock);
        internal Fixture()
        {
            var existing = ExistingBundle(); var content = existing.Content.Values.ToDictionary(x => x.Key, x => new PublishedContent(x.Key, "fixture-fallback",
                "fixture-version-" + x.Key, "fixture-publication-" + x.Key, existing.Locale, x.Text), StringComparer.Ordinal);
            Bundle = new("fixture-session-content", "fixture-request", "fixture-fallback", existing.Locale, content); Resolver = new(Bundle);
            Binding = new(Policy.Version, "fixture-configuration", "fixture-qualification", Bundle.Scope, Bundle.RequestedVariantReference,
                new(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false));
        }
        public SessionListingContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt)
        { BindingReads++; BeforeBindingRead?.Invoke(BindingReads); BindingRequest = (policy, effectiveAt); return Binding; }
    }
    private sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result; internal ContentResolutionRequest? LastRequest;
        internal Action? BeforeReturn;
        public ContentResolution Resolve(ContentResolutionRequest request) { LastRequest = request; BeforeReturn?.Invoke(); return Result; }
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Resolve(request)); }
    }
}
