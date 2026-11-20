using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// Public tests contain qualified publication metadata/copy only. No Recovery code, credential, wire repair or private native fixture.
public sealed class PublishedRecoveryPasswordPresentationTests
{
    [Fact]
    public async Task PublishedCopyPreservesExactQualifiedGoalEffectAndUnresolvedReferenceMapping()
    {
        var f = new Fixture(); var value = await f.ReadAsync();
        Assert.Equal(f.Binding.ConfigurationVersionReference, value.Version); Assert.Equal(f.Binding.GoalLabelKey, value.GoalLabelKey);
        Assert.Equal(f.Binding.GoalDescriptionKey, value.GoalDescriptionKey); Assert.Equal(f.Binding.EffectLabelKey, value.EffectLabelKey); Assert.Equal(f.Binding.EffectValueKey, value.EffectValueKey);
        Assert.Equal(f.Policy.UnresolvedDownstreamEffects.Order(StringComparer.Ordinal), value.FollowUps.Select(follow => follow.Reference).Order(StringComparer.Ordinal));
        Assert.Equal(f.Binding.FollowUps, value.FollowUps); value.Require(f.Policy, f.Clock.GetUtcNow());
        foreach (var expected in f.Resolver.Result.Content.Values)
        { var actual = value.Content[expected.Key]; Assert.Equal(expected.Text, actual.Text); Assert.Equal(expected.PublicationReference, actual.PublicationRef); Assert.Equal(expected.VersionReference, actual.VersionRef); Assert.Equal(expected.VariantReference, actual.VariantRef); Assert.Equal(expected.Locale, actual.Locale); }
        Assert.Equal(2, f.Reads); Assert.Equal(1, f.Resolver.AsyncReads); Assert.Equal(0, f.Resolver.SyncReads);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("goal")]
    [InlineData("effect")]
    [InlineData("status")]
    [InlineData("future-binding")]
    [InlineData("expired-binding")]
    [InlineData("qualification")]
    public async Task DifferentOwnerPolicyOrUnqualifiedBindingCannotReachContent(string failure)
    {
        var f = new Fixture();
        if (failure == "policy") f.Policy = f.Policy with { Version = "fixture-other-policy" };
        if (failure == "goal") f.Policy = f.Policy with { GoalReference = "fixture-other-goal" };
        if (failure == "effect") f.Policy = f.Policy with { SessionEffect = (RecoveryPasswordSessionEffect)999 };
        if (failure == "status") f.Binding = f.Binding with { Policy = new(new(f.Policy.Version, "DRAFT", "ACCOUNT_RECOVERY_PASSWORD", f.Interval, null, ["SYNTHETIC-ONLY"]), f.Policy) };
        if (failure == "future-binding") f.Binding = f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow().AddSeconds(1), true, null, false) };
        if (failure == "expired-binding") f.Binding = f.Binding with { EffectiveInterval = new(f.Clock.GetUtcNow().AddMinutes(-1), true, f.Clock.GetUtcNow(), false) };
        if (failure == "qualification") f.Binding = f.Binding with { QualificationReference = "" };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync()); Assert.Equal(0, f.Resolver.AsyncReads);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("missing-label")]
    public async Task WordsCannotSubstituteForExactOwnerDeclaredFollowUps(string failure)
    {
        var f = new Fixture();
        f.Binding = f.Binding with { FollowUps = failure switch
        {
            "missing" => f.Binding.FollowUps.RemoveAt(0),
            "extra" => f.Binding.FollowUps.Add(new("fixture-unqualified-account-restoration", "fixture.follow-label-0", "fixture.follow-value-0")),
            _ => f.Binding.FollowUps.SetItem(0, f.Binding.FollowUps[0] with { LabelKey = "fixture.missing-label" })
        } };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync());
    }

    [Theory]
    [InlineData("locale")]
    [InlineData("scope")]
    [InlineData("publication")]
    [InlineData("required-goal")]
    public async Task IncompleteOrCrossContextPublishedCopyFailsClosed(string failure)
    {
        var f = new Fixture(); var result = f.Resolver.Result; var content = result.Content.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (failure == "locale") content["fixture.goal"] = content["fixture.goal"] with { Locale = "fr" };
        if (failure == "publication") content["fixture.goal"] = content["fixture.goal"] with { PublicationReference = "" };
        if (failure == "required-goal") content.Remove("fixture.goal");
        f.Resolver.Result = result with { Content = content, Scope = failure == "scope" ? "fixture-other" : result.Scope };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync());
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("binding")]
    [InlineData("content")]
    public async Task FinalBindingAwaitCannotOutliveAnyQualifiedInterval(string boundary)
    {
        var f = new Fixture(); var end = f.Clock.GetUtcNow().AddSeconds(1); var interval = new EffectivePolicyInterval(f.Clock.GetUtcNow(), true, end, false);
        if (boundary == "policy") f.Binding = f.Binding with { Policy = new(new(f.Policy.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", interval, null, ["SYNTHETIC-ONLY"]), f.Policy) };
        if (boundary == "binding") f.Binding = f.Binding with { EffectiveInterval = interval };
        if (boundary == "content") f.Resolver.Result = f.Resolver.Result with { EffectiveInterval = interval };
        f.AfterFinalRead = () => f.Clock.Advance(TimeSpan.FromSeconds(1)); await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync());
    }

    [Fact]
    public async Task MutableInputEffectsCannotChangeAcrossTheFirstQualifiedOwnerAwait()
    {
        var f = new Fixture(); var original = f.Policy.UnresolvedDownstreamEffects.ToList(); f.Policy = f.Policy with { UnresolvedDownstreamEffects = original };
        f.AfterInitialRead = () => original.Add("fixture-unqualified-effect");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync()); Assert.Equal(0, f.Resolver.AsyncReads);
    }

    [Fact]
    public async Task CallerCancellationIsPreservedWithoutSynchronousFallback()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await f.ReadAsync(cancellationToken: cancellation.Token)); Assert.Equal(0, f.Resolver.SyncReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyCancellationIsUnavailableWhileTheCallerTokenRemainsLive(bool contentDependency)
    {
        var f = new Fixture(); using var caller = new CancellationTokenSource(); using var dependency = new CancellationTokenSource(); dependency.Cancel();
        Action fail = () => throw new OperationCanceledException(dependency.Token);
        if (contentDependency) f.Resolver.AfterRead = fail; else f.AfterInitialRead = fail;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.ReadAsync(cancellationToken: caller.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException); Assert.False(caller.IsCancellationRequested); Assert.Equal(0, f.Resolver.SyncReads);
    }
    [Fact]
    public async Task CallerCancellationDuringQualifiedOwnerAwaitIsNeverTranslatedToUnavailability()
    {
        var f = new Fixture(); using var caller = new CancellationTokenSource(); f.AfterInitialRead = caller.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await f.ReadAsync(cancellationToken: caller.Token));
        Assert.True(caller.IsCancellationRequested); Assert.Equal(0, f.Resolver.SyncReads);
    }

    [Fact]
    public void ExplicitOptInInstallsOnlyPresentationAndRequiresUniqueQualifiedDependencies()
    {
        var f = new Fixture(); var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(f.Clock); services.AddSingleton<IPresentationContentResolver>(f.Resolver); services.AddSingleton<IRecoveryPasswordContentBindingSource>(f);
        services.AddTunnerPublishedRecoveryPasswordPresentation(); using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var source = Assert.IsType<PublishedRecoveryPasswordPresentationSource>(scope.ServiceProvider.GetRequiredService<IRecoveryPasswordPresentationSource>());
        Assert.Throws<InvalidOperationException>(() => source.RequirePublished(f.Policy, f.Clock.GetUtcNow()));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IRecoveryPasswordPolicySource) || service.ServiceType == typeof(IRecoveryCaseHierarchySource) ||
            service.ServiceType == typeof(IRegistrationPasswordBlocklist) || service.ServiceType == typeof(RecoveryBrowserService));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPublishedRecoveryPasswordPresentation()); Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTunnerPublishedRecoveryPasswordPresentation());
    }

    internal sealed class Fixture : IRecoveryPasswordContentBindingSource
    {
        private static readonly string[] UnresolvedEffects = ["ACCESS_RESTORATION", "NOTIFICATION_DELIVERY"];
        internal readonly AuthenticationTestHost.TestClock Clock = new(); internal readonly EffectivePolicyInterval Interval;
        internal RecoveryPasswordPolicy Policy; internal RecoveryPasswordContentBinding Binding; internal readonly Resolver Resolver;
        internal int Reads; internal Action? AfterInitialRead; internal Action? AfterFinalRead;
        internal Fixture()
        {
            Interval = new(Clock.GetUtcNow().AddMinutes(-1), true, Clock.GetUtcNow().AddHours(1), false);
            var raw = new RecoveryPasswordPolicy("fixture-published-recovery-v1", AuthenticationTestHost.Origin, "fixture-repair-password", 15, 128, 10_000,
                RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), 32, 100, 100,
                UnresolvedEffects.ToArray(), "fixture-processing", "fixture-retention");
            var qualified = new RecoveryPasswordPolicyVersion(new(raw.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", Interval, null, ["SYNTHETIC-ONLY"]), raw); Policy = qualified.Values;
            ImmutableArray<RecoveryPasswordFollowUp> follow = [new("ACCESS_RESTORATION", "fixture.follow-label-0", "fixture.follow-value-0"), new("NOTIFICATION_DELIVERY", "fixture.follow-label-1", "fixture.follow-value-1")];
            string[] keys = ["fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value", "fixture.follow-label-0", "fixture.follow-value-0", "fixture.follow-label-1", "fixture.follow-value-1"];
            var content = keys.ToDictionary(key => key, key => new PublishedContent(key, "fixture-recovery-fallback", "fixture-version-" + key, "fixture-publication-" + key, "en", "Synthetic published copy only"), StringComparer.Ordinal);
            Resolver = new(new("fixture-recovery-content", "fixture-recovery-requested", "fixture-recovery-fallback", "en", content) { EffectiveInterval = Interval });
            Binding = new(qualified, "fixture-published-recovery-content-v1", "fixture-recovery-content-qualification", Resolver.Result.Scope, Resolver.Result.RequestedVariantReference, Interval,
                "fixture.goal", "fixture.description", "fixture.effect", "fixture.effect-value", follow);
        }
        internal ValueTask<RecoveryPasswordPresentation> ReadAsync(IPresentationContentResolver? resolver = null, CancellationToken cancellationToken = default) =>
            new PublishedRecoveryPasswordPresentationSource(resolver ?? Resolver, this, Clock).RequirePublishedAsync(Policy, Clock.GetUtcNow(), cancellationToken);
        public async ValueTask<RecoveryPasswordContentBinding> RequireQualifiedAsync(RecoveryPasswordPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Reads++; await Task.Yield(); if (Reads == 1) AfterInitialRead?.Invoke(); if (Reads == 2) AfterFinalRead?.Invoke(); return Binding; }
    }
    internal sealed class Resolver(ContentResolution result) : IPresentationContentResolver
    {
        internal ContentResolution Result = result; internal int AsyncReads; internal int SyncReads; internal Action? AfterRead;
        public ContentResolution Resolve(ContentResolutionRequest request) { SyncReads++; throw new InvalidOperationException("No synchronous Content fallback."); }
        public async Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); AsyncReads++; await Task.Yield(); AfterRead?.Invoke(); cancellationToken.ThrowIfCancellationRequested(); return Result; }
    }
}
