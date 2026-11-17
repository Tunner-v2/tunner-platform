using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedSessionRevocationPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactSeparateActionBundlePreservesFallbackLocaleAndAllPublishedProvenanceWithoutSynchronousIo(bool bulk)
    {
        var f = new Fixture(bulk); var result = await f.ReadAsync(); Assert.Equal("en", result.Locale);
        Assert.Equal(f.Keys.Count, result.Content.Count);
        foreach (var expected in f.Resolver.Result.Content.Values)
        {
            var actual = result.Content[expected.Key]; Assert.Equal(expected.Text, actual.Text); Assert.Equal(expected.Locale, actual.Locale);
            Assert.Equal(expected.VariantReference, actual.VariantRef); Assert.Equal(expected.VersionReference, actual.VersionRef);
            Assert.Equal(expected.PublicationReference, actual.PublicationRef); Assert.Equal("PUBLISHED", actual.State);
        }
        Assert.Equal(2, f.Reads); Assert.Equal(1, f.Resolver.AsyncReads); Assert.Equal(0, f.Resolver.SyncReads);
        Assert.Equal(new ContentResolutionRequest(f.Binding.ContentScope, f.Binding.VariantReference, f.Clock.GetUtcNow()), f.Resolver.LastRequest);
        Assert.Same(f.Policy, f.LastPolicy);
        Assert.DoesNotContain(SessionListingPresentation.RequiredKeys[0], result.Content.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnqualifiedPolicyScopeVersionAndTimeCannotReachContentStorage(bool bulk)
    {
        foreach (var failure in new[] { "policy", "configuration", "qualification", "scope", "variant", "future", "expired" })
        {
            var f = new Fixture(bulk); var b = f.Binding;
            f.Binding = failure switch
            {
                "policy" => b with { AuthenticationPolicyVersion = "fixture-other-policy" },
                "configuration" => b with { ConfigurationVersionReference = "" },
                "qualification" => b with { QualificationReference = "" },
                "scope" => b with { ContentScope = "" },
                "variant" => b with { VariantReference = "" },
                "future" => b with { EffectiveInterval = new(f.Clock.GetUtcNow().AddSeconds(1), true, null, false) },
                _ => b with { EffectiveInterval = new(f.Clock.GetUtcNow().AddSeconds(-1), true, f.Clock.GetUtcNow(), false) }
            };
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync()); Assert.Equal(0, f.Resolver.AsyncReads); Assert.Equal(0, f.Resolver.SyncReads);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDomainConstructorAndSharedEnvelopeRejectIncompleteOrSubstitutedPublication(bool bulk)
    {
        foreach (var failure in new[] { "scope", "variant", "locale", "effective-variant", "dictionary-key", "version", "publication", "text", "required-copy" })
        {
            var f = new Fixture(bulk); var r = f.Resolver.Result; var content = r.Content.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal); var key = f.Keys[0];
            content[key] = failure switch
            {
                "locale" => content[key] with { Locale = "fr" },
                "effective-variant" => content[key] with { VariantReference = "fixture-other" },
                "dictionary-key" => content[key] with { Key = "fixture-other" },
                "version" => content[key] with { VersionReference = "" },
                "publication" => content[key] with { PublicationReference = "" },
                "text" => content[key] with { Text = "" },
                _ => content[key]
            };
            if (failure == "required-copy") content.Remove(key);
            f.Resolver.Result = r with { Content = content, Scope = failure == "scope" ? "fixture-other" : r.Scope,
                RequestedVariantReference = failure == "variant" ? "fixture-other" : r.RequestedVariantReference };
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync()); Assert.Equal(0, f.Resolver.SyncReads);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalAsyncBindingReadCannotHideBindingChangesOrActualContentExpiry(bool bulk)
    {
        foreach (var failure in new[] { "changed-binding", "binding-expiry", "content-expiry", "missing-bounds" })
        {
            var f = new Fixture(bulk); var end = f.Clock.GetUtcNow().AddSeconds(1);
            if (failure == "binding-expiry") { f.Binding = f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow(), true, end, false) }; f.Resolver.AfterRead = () => f.Clock.Advance(TimeSpan.FromSeconds(1)); }
            if (failure == "content-expiry") { f.Resolver.Result = f.Resolver.Result with { EffectiveInterval = new(f.Clock.GetUtcNow(), true, end, false) }; f.AfterFinalRead = () => f.Clock.Advance(TimeSpan.FromSeconds(1)); }
            if (failure == "missing-bounds") f.Resolver.Result = f.Resolver.Result with { EffectiveInterval = null };
            if (failure == "changed-binding") f.AfterFinalRead = () => f.Binding = f.Binding with { ConfigurationVersionReference = "fixture-changed-version" };
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync()); Assert.Equal(0, f.Resolver.SyncReads);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationRemainsCancellationBeforeOrDuringContentRead(bool duringRead)
    {
        foreach (var bulk in new[] { false, true })
        {
            var f = new Fixture(bulk); using var cancellation = new CancellationTokenSource();
            if (duringRead) f.Resolver.AfterRead = cancellation.Cancel; else cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await f.ReadAsync(cancellation.Token)); Assert.Equal(0, f.Resolver.SyncReads);
        }
    }

    [Fact]
    public void SeparateExplicitOptInsInstallOnlyTheirPresentationAndRejectWrongOrMissingProviders()
    {
        foreach (var bulk in new[] { false, true })
        {
            var f = new Fixture(bulk); var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(f.Clock); services.AddSingleton<IPresentationContentResolver>(f.Resolver);
            if (bulk) services.AddSingleton<ISessionBulkRevocationContentBindingSource>(f); else services.AddSingleton<ISessionRevocationContentBindingSource>(f);
            if (bulk) services.AddTunnerPublishedSessionBulkRevocationPresentation(); else services.AddTunnerPublishedSessionRevocationPresentation();
            using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
            if (bulk)
            {
                var source = Assert.IsType<PublishedSessionBulkRevocationPresentationSource>(scope.ServiceProvider.GetRequiredService<ISessionBulkRevocationPresentationSource>());
                Assert.Throws<InvalidOperationException>(() => source.RequirePublished(f.Policy));
                Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedSessionBulkRevocationPresentation());
                Assert.DoesNotContain(services, service => service.ServiceType == typeof(ISessionRevocationPresentationSource));
            }
            else
            {
                var source = Assert.IsType<PublishedSessionRevocationPresentationSource>(scope.ServiceProvider.GetRequiredService<ISessionRevocationPresentationSource>());
                Assert.Throws<InvalidOperationException>(() => source.RequirePublished(f.Policy));
                Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedSessionRevocationPresentation());
                Assert.DoesNotContain(services, service => service.ServiceType == typeof(ISessionBulkRevocationPresentationSource));
            }
            Assert.DoesNotContain(services, service => service.ServiceType == typeof(IAuthenticationPolicySource) || service.ServiceType == typeof(ISessionListingPresentationSource) ||
                service.ServiceType == typeof(ISessionListingHttpBudgetSource) || service.ServiceType == typeof(ISessionBulkRevocationDiscoverySource) ||
                service.ServiceType == typeof(ISessionBulkRevocationHttpBudgetSource) || service.ServiceType == typeof(IAssurancePolicySource) || service.ServiceType == typeof(IStepUpPresentationSource));
            Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTunnerPublishedSessionRevocationPresentation());
            Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTunnerPublishedSessionBulkRevocationPresentation());
            Assert.Equal(0, f.Resolver.SyncReads);
        }
    }

    private sealed class Fixture : ISessionRevocationContentBindingSource, ISessionBulkRevocationContentBindingSource
    {
        internal readonly bool Bulk; internal readonly AuthenticationTestHost.TestClock Clock = new();
        internal readonly AuthenticationPolicy Policy = new AuthenticationTestHost.TestPolicies().Policy;
        internal readonly ReadOnlyCollection<string> Keys; internal readonly Resolver Resolver;
        internal SessionActionContentBinding Binding; internal int Reads; internal AuthenticationPolicy? LastPolicy; internal Action? AfterFinalRead;
        internal Fixture(bool bulk)
        {
            Bulk = bulk; Keys = bulk ? SessionBulkRevocationPresentation.RequiredKeys : SessionRevocationPresentation.RequiredKeys;
            var interval = new EffectivePolicyInterval(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false);
            var content = Keys.ToDictionary(key => key, key => new PublishedContent(key, "fixture-action-fallback", "fixture-version-" + key, "fixture-publication-" + key, "en", "Synthetic published action: " + key), StringComparer.Ordinal);
            Resolver = new(new("fixture-action-content", "fixture-action-requested", "fixture-action-fallback", "en", content) { EffectiveInterval = interval });
            Binding = new(Policy.Version, "fixture-action-configuration", "fixture-action-qualification", Resolver.Result.Scope, Resolver.Result.RequestedVariantReference, interval);
        }
        internal async Task<(string Locale, IReadOnlyDictionary<string, RegistrationPublishedText> Content)> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (Bulk) { var value = await new PublishedSessionBulkRevocationPresentationSource(Resolver, this, Clock).RequirePublishedAsync(Policy, cancellationToken); return (value.Locale, value.Content); }
            else { var value = await new PublishedSessionRevocationPresentationSource(Resolver, this, Clock).RequirePublishedAsync(Policy, cancellationToken); return (value.Locale, value.Content); }
        }
        public ValueTask<SessionActionContentBinding> RequireQualifiedAsync(AuthenticationPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); LastPolicy = policy; Reads++; if (Reads == 2) AfterFinalRead?.Invoke(); return ValueTask.FromResult(Binding); }
    }
    private sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result; internal ContentResolutionRequest? LastRequest; internal int AsyncReads; internal int SyncReads; internal Action? AfterRead;
        public ContentResolution Resolve(ContentResolutionRequest request) { SyncReads++; throw new InvalidOperationException("Synchronous Content access is not admitted by this test."); }
        public async Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); AsyncReads++; LastRequest = request; await Task.Yield(); AfterRead?.Invoke(); cancellationToken.ThrowIfCancellationRequested(); return Result; }
    }
}
