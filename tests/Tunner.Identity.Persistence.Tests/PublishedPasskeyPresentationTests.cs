using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedPasskeyPresentationTests
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
            "policy" => f.Binding with { PasskeyPolicyVersion = "fixture-other-policy" },
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
    [InlineData("passkeys.setup.heading")]
    [InlineData("passkeys.revoke.heading")]
    [InlineData("passkeys.operation.pending")]
    public void RejectsIncompleteOrCrossContextPublishedBundle(string failure)
    {
        var f = new Fixture(); var content = f.Bundle.Content.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); const string key = "passkeys.setup.heading";
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
        if (failure.StartsWith("passkeys.", StringComparison.Ordinal)) content.Remove(failure);
        f.Resolver.Result = f.Bundle with { Content = content, Scope = failure == "scope" ? "fixture-other-scope" : f.Bundle.Scope,
            RequestedVariantReference = failure == "requested-variant" ? "fixture-other-request" : f.Bundle.RequestedVariantReference };
        Assert.Throws<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy));
    }

    [Fact]
    public void ExplicitOptInInstallsOnlyPresentationAndRejectsAnExistingProvider()
    {
        var f = new Fixture(); var services = Complete(f); services.AddTunnerPublishedPasskeyPresentation();
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IPasskeyPolicySource) || x.ServiceType == typeof(IPasskeyRevocationEligibilitySource) || x.ServiceType == typeof(PasskeyApplication));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); Assert.IsType<PublishedPasskeyPresentationSource>(scope.ServiceProvider.GetRequiredService<IPasskeyPresentationSource>());
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedPasskeyPresentation());
        var existing = Complete(f); existing.AddSingleton<IPasskeyPresentationSource, ExistingPresentation>(); Assert.Throws<InvalidOperationException>(() => existing.AddTunnerPublishedPasskeyPresentation());
    }

    [Fact]
    public void MissingExplicitDependenciesCannotInstallPresentation()
    {
        var f = new Fixture();
        foreach (var missing in new[] { typeof(TimeProvider), typeof(IPasskeyContentBindingSource), typeof(IPresentationContentResolver) })
        {
            var services = Complete(f); services.Remove(services.Single(x => x.ServiceType == missing)); Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedPasskeyPresentation());
            Assert.DoesNotContain(services, x => x.ServiceType == typeof(IPasskeyPresentationSource));
        }
    }

    // Exact explicit synthetic policy shape from the existing PasskeyTests fixture, never a production selection.
    internal static PasskeyPolicy TestPolicy() => new PasskeyConfigurationValues("fixture-passkey-v1", "authentication.example.test", AuthenticationTestHost.Origin,
        TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), PasskeyRevocationSessionEffect.PreserveExistingSessions,
        "required", new[] { -7 }, 32, 32768, 4, 4, "fixture-enrollment-v1", "fixture-revocation-v1", "fixture-processing", "fixture-retention", "required", "none",
        new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-passkey-assurance", [PasskeyPolicy.AssuranceKey(true, false)] = "fixture-syncable-unbacked-assurance", [PasskeyPolicy.AssuranceKey(true, true)] = "fixture-syncable-backed-assurance" }).ToPolicy();
    internal static PasskeyPresentation ExistingBundle()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "passkeys-content.proposal.json")));
        return new("en", document.RootElement.GetProperty("content").EnumerateObject().Select(x =>
            new RegistrationPublishedText(x.Name, "fixture", "fixture-v1", "fixture-only-publication", "en", "PUBLISHED", x.Value.GetString()!)));
    }
    private sealed class ExistingPresentation : IPasskeyPresentationSource
    { public PasskeyPresentation RequirePublished(PasskeyPolicy policy) => ExistingBundle(); }
    private static ServiceCollection Complete(Fixture f)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(f.Clock); services.AddSingleton<IPresentationContentResolver>(f.Resolver);
        services.AddSingleton<IPasskeyContentBindingSource>(f); return services;
    }
    private sealed class Fixture : IPasskeyContentBindingSource
    {
        internal readonly AuthenticationTestHost.TestClock Clock = new(); internal readonly PasskeyPolicy Policy = TestPolicy();
        internal readonly ContentResolution Bundle; internal readonly Resolver Resolver;
        internal PasskeyContentBinding Binding; internal (PasskeyPolicy Policy, DateTimeOffset At)? BindingRequest;
        internal PublishedPasskeyPresentationSource Source => new(Resolver, this, Clock);
        internal Fixture()
        {
            var existing = ExistingBundle(); var content = existing.Content.Values.ToDictionary(x => x.Key, x => new PublishedContent(x.Key, "fixture-fallback",
                "fixture-version-" + x.Key, "fixture-publication-" + x.Key, existing.Locale, x.Text), StringComparer.Ordinal);
            Bundle = new("fixture-passkey-content", "fixture-request", "fixture-fallback", existing.Locale, content); Resolver = new(Bundle);
            Binding = new(Policy.Version, "fixture-configuration", "fixture-qualification", Bundle.Scope, Bundle.RequestedVariantReference,
                new(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false));
        }
        public PasskeyContentBinding RequireQualified(PasskeyPolicy policy, DateTimeOffset effectiveAt) { BindingRequest = (policy, effectiveAt); return Binding; }
    }
    private sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result; internal ContentResolutionRequest? LastRequest;
        public ContentResolution Resolve(ContentResolutionRequest request) { LastRequest = request; return Result; }
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Resolve(request)); }
    }
}
