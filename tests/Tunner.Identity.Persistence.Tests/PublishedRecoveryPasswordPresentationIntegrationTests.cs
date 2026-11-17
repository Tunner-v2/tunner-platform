using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// Publication reads only; private Recovery wire/effect cases remain in the fixed private cohort.
public sealed class PublishedRecoveryPasswordPresentationIntegrationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ActualPublicationReplacementAndHistoryPreserveQualifiedRecoveryEffectMappingWithoutIdentityWrites()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var f = new PublishedRecoveryPasswordPresentationTests.Fixture();
        var content = new PresentationContentTestEnvironment(database, f.Clock, f.Resolver.Result.Content.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal)); await content.InitializeAsync();
        ContentHistoryEntry? goal = null;
        foreach (var text in f.Resolver.Result.Content.Values) { var p = await content.PublishTextAsync(text.Key, text.Text); if (text.Key == "fixture.goal") goal = p; }
        f.Binding = f.Binding with { ContentScope = content.Scope, VariantReference = content.Variant }; await using var owner = content.Open();
        var original = await f.ReadAsync(owner.Application); Assert.Equal(goal!.PublicationReference.ToString("N"), original.Content["fixture.goal"].PublicationRef);
        f.Clock.Advance(TimeSpan.FromSeconds(1)); var replacement = await content.PublishTextAsync("fixture.goal", "Synthetic reviewed Recovery goal copy", supersedes: goal.PublicationReference);
        var current = await f.ReadAsync(owner.Application); Assert.Equal("Synthetic reviewed Recovery goal copy", current.Content["fixture.goal"].Text); Assert.Equal(replacement.PublicationReference.ToString("N"), current.Content["fixture.goal"].PublicationRef);
        Assert.Equal(original.FollowUps, current.FollowUps); Assert.Equal(original.EffectValueKey, current.EffectValueKey); current.Require(f.Policy, f.Clock.GetUtcNow());
        var history = await owner.Application.HistoryAsync(content.Scope, "fixture.goal", content.Variant, "OP-fixture-recovery-copy-history", "fixture-recovery-copy-history");
        Assert.Contains(history, row => row.PublicationReference == goal.PublicationReference); Assert.Contains(history, row => row.PublicationReference == replacement.PublicationReference);
        await AssertNoIdentityWrites(database);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualObservedFuturePublicationClipsRecoveryCopyWithExactBoundary(bool includesFrom)
    {
        await using var database = await fixture.CreateDatabaseAsync(); var f = new PublishedRecoveryPasswordPresentationTests.Fixture();
        var content = new PresentationContentTestEnvironment(database, f.Clock, f.Resolver.Result.Content.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal)); await content.InitializeAsync(); ContentHistoryEntry? goal = null;
        foreach (var text in f.Resolver.Result.Content.Values) { var p = await content.PublishTextAsync(text.Key, text.Text); if (text.Key == "fixture.goal") goal = p; }
        var transition = f.Clock.GetUtcNow().AddSeconds(1);
        await using (var writer = content.Open())
        {
            content.Authorization.Actor = "fixture-author"; var p = await writer.Application.CreateDraftAsync(content.Draft("Synthetic scheduled goal copy", "fixture.goal")); p = await writer.Application.ValidateAsync(content.Command(p));
            content.Authorization.Actor = "fixture-approver"; p = await writer.Application.ApproveAsync(content.Command(p)); content.Authorization.Actor = "fixture-publisher";
            _ = await writer.Application.PublishAsync(content.Publish(p, goal!.PublicationReference) with { EffectiveInterval = new(transition, includesFrom, null, false) });
        }
        f.Binding = f.Binding with { ContentScope = content.Scope, VariantReference = content.Variant }; await using var owner = content.Open();
        var value = await f.ReadAsync(owner.Application); Assert.Equal(transition, value.EffectiveInterval.Until); Assert.Equal(!includesFrom, value.EffectiveInterval.IncludesUntil); await AssertNoIdentityWrites(database);
    }
    [Fact]
    public async Task DependencyCancellationDisablesOnlyRecoveryBootstrapAndPreservesActualCallerCancellation()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var f = new PublishedRecoveryPasswordPresentationTests.Fixture(); var dependency = new DependencyCanceledPresentation();
        await using var host = await AuthenticationTestHost.Start(database, services =>
        {
            // Availability-only seam: no Recovery policy/proof ports or endpoints are registered, and no private factor is seeded.
            services.AddScoped(provider => new RecoveryBrowserService(new RecoveryCaseApplication(provider.GetRequiredService<IdentityPersistenceDbContext>(), null,
                provider.GetRequiredService<AuthenticationApplication>(), provider.GetRequiredService<TimeProvider>()), new RecoveryPasswordPolicyCatalog([f.Binding.Policy]), dependency,
                provider.GetRequiredService<IAuthenticationPolicySource>(), provider.GetRequiredService<IDataProtectionProvider>(), provider.GetRequiredService<AuthenticationPartitionKey>(),
                AuthenticationTestHost.Origin, provider.GetRequiredService<TimeProvider>()));
        });
        using (var bootstrap = await host.Get("/authentication/options"))
        {
            Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode); var response = await bootstrap.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(response.TryGetProperty("recoveryAvailable", out _)); Assert.True(response.GetProperty("content").EnumerateObject().Any());
            Assert.False(string.IsNullOrWhiteSpace(response.GetProperty("csrfToken").GetString())); Assert.Equal(1, dependency.Reads);
        }
        using (var absent = await host.Get("/recovery/options")) Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            using var caller = new CancellationTokenSource(); caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await scope.ServiceProvider.GetRequiredService<RecoveryBrowserService>().AvailableAsync(caller.Token));
        }
        await AssertNoIdentityWrites(database);
    }
    private sealed class DependencyCanceledPresentation : IRecoveryPasswordAsyncPresentationSource
    {
        internal int Reads;
        public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at) => throw new InvalidOperationException("No synchronous publication fallback.");
        public ValueTask<RecoveryPasswordPresentation> RequirePublishedAsync(RecoveryPasswordPolicy policy, DateTimeOffset at, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads++;
            return ValueTask.FromException<RecoveryPasswordPresentation>(new OperationCanceledException(new CancellationToken(canceled: true)));
        }
    }

    private static async Task AssertNoIdentityWrites(IdentityTestDatabase database)
    {
        await using var identity = database.Context(); Assert.Empty(await identity.Accounts.ToArrayAsync()); Assert.Empty(await identity.Set<AuthenticationSessionRow>().ToArrayAsync());
        Assert.Empty(await identity.Set<RecoveryEntryRow>().ToArrayAsync()); Assert.Empty(await identity.Set<RecoveryCaseRow>().ToArrayAsync()); Assert.Empty(await identity.Set<RecoveryResultRow>().ToArrayAsync());
    }
}
