using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedStepUpPresentationTests
{
    [Fact]
    public void ProjectsExistingCompleteBundleWithExactPolicyScopeClockAndFallbackProvenance()
    {
        var f = new Fixture(); var result = f.Source.RequirePublished(f.Policy);
        Assert.Equal(f.Bundle.Locale, result.Locale); Assert.Equal(f.Bundle.Content.Count, result.Content.Count);
        foreach (var (key, value) in f.Bundle.Content)
        {
            var actual = result.Content[key]; Assert.Equal(value.Text, actual.Text); Assert.Equal(value.Locale, actual.Locale);
            Assert.Equal(value.VariantReference, actual.VariantRef); Assert.Equal(value.VersionReference, actual.VersionRef); Assert.Equal(value.PublicationReference, actual.PublicationRef);
        }
        Assert.Equal(new ContentResolutionRequest(f.Binding.ContentScope, f.Binding.VariantReference, f.Clock.GetUtcNow()), f.Resolver.LastRequest);
        Assert.Same(f.Policy, f.Request!.Value.Policy); Assert.Equal(f.Clock.GetUtcNow(), f.Request.Value.At);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("context")]
    [InlineData("version")]
    [InlineData("configuration")]
    [InlineData("qualification")]
    [InlineData("scope")]
    [InlineData("variant")]
    [InlineData("future")]
    [InlineData("expired")]
    public void RejectsWrongOrUnqualifiedBindingBeforeReadingContent(string failure)
    {
        var f = new Fixture(); f.Binding = failure switch
        {
            "operation" => f.Binding with { AssuranceScope = new("other-operation", f.Policy.Metadata.Scope.Context) },
            "context" => f.Binding with { AssuranceScope = new(f.Policy.Metadata.Scope.Operation, "other-context") },
            "version" => f.Binding with { AssurancePolicyVersion = "other-version" },
            "configuration" => f.Binding with { ConfigurationVersionReference = "" },
            "qualification" => f.Binding with { QualificationReference = "" },
            "scope" => f.Binding with { ContentScope = "" },
            "variant" => f.Binding with { VariantReference = "" },
            "future" => f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow().AddSeconds(1), true, null, false) },
            _ => f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow().AddSeconds(-1), true, f.Clock.GetUtcNow(), false) }
        };
        Assert.ThrowsAny<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy)); Assert.Null(f.Resolver.LastRequest);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("future")]
    [InlineData("expired")]
    public void RequiresActualActiveCurrentAssurancePolicyBeforeBindingLookup(string failure)
    {
        var f = new Fixture(); var interval = failure == "future" ? new EffectivePolicyInterval(f.Clock.GetUtcNow().AddSeconds(1), true, null, false)
            : new EffectivePolicyInterval(f.Clock.GetUtcNow().AddSeconds(-1), true, failure == "expired" ? f.Clock.GetUtcNow() : null, false);
        var policy = new AssurancePolicyVersion(new(f.Policy.Metadata.VersionId, failure == "status" ? "DRAFT" : "ACTIVE", f.Policy.Metadata.Scope, interval, null, ["SYNTHETIC-ONLY"]),
            f.Policy.RequiredAssurance, f.Policy.ProofLifetime, f.Policy.MethodAssurances);
        Assert.ThrowsAny<InvalidOperationException>(() => f.Source.RequirePublished(policy)); Assert.Null(f.Request); Assert.Null(f.Resolver.LastRequest);
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
    [InlineData("stepUp.heading")]
    [InlineData("stepUp.passkey.resume")]
    [InlineData("stepUp.error.changed")]
    public void RejectsCrossContextOrIncompletePublishedContent(string failure)
    {
        var f = new Fixture(); var content = f.Bundle.Content.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); const string key = "stepUp.heading";
        content[key] = failure switch
        {
            "locale" => content[key] with { Locale = "other-locale" }, "effective-variant" => content[key] with { VariantReference = "other-variant" },
            "dictionary-key" => content[key] with { Key = "other-key" }, "version" => content[key] with { VersionReference = "" },
            "publication" => content[key] with { PublicationReference = "" }, "text" => content[key] with { Text = "" }, _ => content[key]
        };
        if (failure.StartsWith("stepUp.", StringComparison.Ordinal)) content.Remove(failure);
        f.Resolver.Result = f.Bundle with { Content = content, Scope = failure == "scope" ? "other-scope" : f.Bundle.Scope,
            RequestedVariantReference = failure == "requested-variant" ? "other-request" : f.Bundle.RequestedVariantReference };
        Assert.ThrowsAny<InvalidOperationException>(() => f.Source.RequirePublished(f.Policy));
    }

    [Fact]
    public void OptionalMethodContentIsNotPromotedToAnUnconditionalConstructorRequirement()
    {
        var f = new Fixture(); f.Resolver.Result = f.Bundle with { Content = f.Bundle.Content.Where(x => !x.Key.StartsWith("stepUp.totp.", StringComparison.Ordinal) &&
            !x.Key.StartsWith("totp.selection.", StringComparison.Ordinal) && !x.Key.StartsWith("stepUp.federation.", StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) };
        var result = f.Source.RequirePublished(f.Policy); Assert.NotEmpty(result.Content); Assert.Throws<ArgumentException>(result.RequireTotpPublished);
        Assert.DoesNotContain(result.Content, x => x.Key.StartsWith("stepUp.federation.", StringComparison.Ordinal));
    }

    [Fact]
    public void PresentationOnlyOptInRequiresExplicitDependenciesAndRejectsExistingSources()
    {
        var f = new Fixture(); ServiceCollection Complete()
        { var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(f.Clock); services.AddSingleton<IPresentationContentResolver>(f.Resolver); services.AddSingleton<IStepUpContentBindingSource>(f); return services; }
        foreach (var missing in new[] { typeof(TimeProvider), typeof(IPresentationContentResolver), typeof(IStepUpContentBindingSource) })
        { var incomplete = Complete(); incomplete.Remove(incomplete.Single(x => x.ServiceType == missing)); Assert.Throws<InvalidOperationException>(() => incomplete.AddTunnerPublishedStepUpPresentation()); }
        var services = Complete(); services.AddTunnerPublishedStepUpPresentation();
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IAssurancePolicySource) || x.ServiceType == typeof(IUserStepUpOperationSource) || x.ServiceType == typeof(StepUpApplication) || x.ServiceType == typeof(StepUpBrowserService));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); Assert.IsType<PublishedStepUpPresentationSource>(scope.ServiceProvider.GetRequiredService<IStepUpPresentationSource>());
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedStepUpPresentation());
        var existing = Complete(); existing.AddSingleton<IStepUpPresentationSource, StepUpBrowserTests.Presentation>(); Assert.Throws<InvalidOperationException>(() => existing.AddTunnerPublishedStepUpPresentation());
    }

    internal static AssurancePolicyVersion Policy(TimeProvider clock) => new(new("fixture-step-v1", "ACTIVE", StepUpBrowserTests.Scope,
        new(clock.GetUtcNow().AddHours(-1), true, clock.GetUtcNow().AddHours(1), false), null, ["SYNTHETIC-ONLY"]),
        "fixture-password", TimeSpan.FromSeconds(30), new Dictionary<string, string> { ["pwd"] = "fixture-password" });
    internal static StepUpPresentation ExistingBundle(TimeProvider clock) => new StepUpBrowserTests.Presentation().RequirePublished(Policy(clock));
    private sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result; internal ContentResolutionRequest? LastRequest;
        public ContentResolution Resolve(ContentResolutionRequest request) { LastRequest = request; return Result; }
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Resolve(request));
    }
    private sealed class Fixture : IStepUpContentBindingSource
    {
        internal readonly AuthenticationTestHost.TestClock Clock = new(); internal readonly AssurancePolicyVersion Policy;
        internal StepUpContentBinding Binding; internal readonly ContentResolution Bundle; internal readonly Resolver Resolver;
        internal (AssurancePolicyVersion Policy, DateTimeOffset At)? Request;
        internal Fixture()
        {
            Policy = PublishedStepUpPresentationTests.Policy(Clock); Binding = new(Policy.Metadata.Scope, Policy.Metadata.VersionId, "fixture-binding", "fixture-qualification", "fixture-stepup-content", "fixture-requested-variant", Policy.Metadata.EffectiveInterval);
            var values = ExistingBundle(Clock).Content.ToDictionary(x => x.Key, x => new PublishedContent(x.Key, "fixture-effective-variant", "fixture-version", "fixture-publication", "en", x.Value.Text), StringComparer.Ordinal);
            Bundle = new(Binding.ContentScope, Binding.VariantReference, "fixture-effective-variant", "en", values); Resolver = new(Bundle);
        }
        internal PublishedStepUpPresentationSource Source => new(Resolver, this, Clock);
        public StepUpContentBinding RequireQualified(AssurancePolicyVersion policy, DateTimeOffset effectiveAt) { Request = (policy, effectiveAt); return Binding; }
    }
}
