using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PublishedProductAuthorizationPresentationIntegrationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ActualPublishedContentReplacesUiCopyAndPreservesSemanticFactsAndHistoryWithoutIdentityWrites()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var f = new PublishedProductAuthorizationPresentationTests.Fixture();
        var env = new PresentationContentTestEnvironment(database, f.Clock, f.Resolver.Result.Content.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal));
        await env.InitializeAsync(); ContentHistoryEntry? heading = null;
        foreach (var text in f.Resolver.Result.Content.Values)
        { var published = await env.PublishTextAsync(text.Key, text.Text); if (text.Key == "authorization.heading") heading = published; }
        f.Binding = f.Binding with { ContentScope = env.Scope, VariantReference = env.Variant };
        await using var owner = env.Open(); var source = f.Source(owner.Application);
        var original = await source.RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None);
        original.Require(f.Facts, f.Clock.GetUtcNow()); Assert.Equal(f.Binding.Semantics.ProductName, original.ProductName); Assert.Equal(f.Binding.Semantics.AccountLabel, original.AccountLabel);
        Assert.Equal(heading!.PublicationReference.ToString("N"), original.Content.Single(text => text.Key == "authorization.heading").PublicationRef);
        f.Clock.Advance(TimeSpan.FromSeconds(1)); var replacement = await env.PublishTextAsync("authorization.heading", "Synthetic revised Product heading", supersedes: heading.PublicationReference);
        var current = await source.RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal("Synthetic revised Product heading", current.Content.Single(text => text.Key == "authorization.heading").Text);
        Assert.Equal(replacement.PublicationReference.ToString("N"), current.Content.Single(text => text.Key == "authorization.heading").PublicationRef);
        var history = await owner.Application.HistoryAsync(env.Scope, "authorization.heading", env.Variant, "OP-fixture-product-history", "fixture-product-history");
        Assert.Contains(history, entry => entry.PublicationReference == heading.PublicationReference); Assert.Contains(history, entry => entry.PublicationReference == replacement.PublicationReference);
        Assert.Equal(original.ProductName, current.ProductName); Assert.Equal(original.AccountLabel, current.AccountLabel); Assert.Equal(original.TermsUri, current.TermsUri);
        await AssertNoIdentityWrites(database);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservedScheduledReplacementClipsReviewMetadataAtExactFutureContentBoundary(bool includesFrom)
    {
        await using var database = await fixture.CreateDatabaseAsync(); var f = new PublishedProductAuthorizationPresentationTests.Fixture();
        var env = new PresentationContentTestEnvironment(database, f.Clock, f.Resolver.Result.Content.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal));
        await env.InitializeAsync(); ContentHistoryEntry? heading = null;
        foreach (var text in f.Resolver.Result.Content.Values)
        { var p = await env.PublishTextAsync(text.Key, text.Text); if (text.Key == "authorization.heading") heading = p; }
        var transition = f.Clock.GetUtcNow().AddSeconds(1);
        await using (var writer = env.Open())
        {
            env.Authorization.Actor = "fixture-author"; var proposal = await writer.Application.CreateDraftAsync(env.Draft("Synthetic scheduled Product heading", "authorization.heading"));
            proposal = await writer.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-approver"; proposal = await writer.Application.ApproveAsync(env.Command(proposal));
            env.Authorization.Actor = "fixture-publisher"; _ = await writer.Application.PublishAsync(env.Publish(proposal, heading!.PublicationReference) with { EffectiveInterval = new(transition, includesFrom, null, false) });
        }
        f.Binding = f.Binding with { ContentScope = env.Scope, VariantReference = env.Variant };
        await using var reader = env.Open(); var value = await f.Source(reader.Application).RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(transition, value.Metadata.EffectiveInterval.Until); Assert.Equal(!includesFrom, value.Metadata.EffectiveInterval.IncludesUntil);
        Assert.True(f.Interval.Contains(transition.AddSeconds(1))); await AssertNoIdentityWrites(database);
    }

    [Fact]
    public async Task FinalSemanticLookupCrossingActualPublicationExpiryCannotReturnReview()
    {
        await using var database = await fixture.CreateDatabaseAsync(); var f = new PublishedProductAuthorizationPresentationTests.Fixture();
        var env = new PresentationContentTestEnvironment(database, f.Clock, f.Resolver.Result.Content.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal));
        await env.InitializeAsync(); var expiry = f.Clock.GetUtcNow().AddSeconds(1);
        foreach (var text in f.Resolver.Result.Content.Values)
        {
            await using var writer = env.Open(); env.Authorization.Actor = "fixture-author"; var p = await writer.Application.CreateDraftAsync(env.Draft(text.Text, text.Key));
            p = await writer.Application.ValidateAsync(env.Command(p)); env.Authorization.Actor = "fixture-approver"; p = await writer.Application.ApproveAsync(env.Command(p));
            env.Authorization.Actor = "fixture-publisher"; _ = await writer.Application.PublishAsync(env.Publish(p) with { EffectiveInterval = new(f.Clock.GetUtcNow(), true, expiry, false) });
        }
        f.Binding = f.Binding with { ContentScope = env.Scope, VariantReference = env.Variant }; f.AfterFinalRead = () => f.Clock.Advance(TimeSpan.FromSeconds(1));
        await using var owner = env.Open(); await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Source(owner.Application).RequirePublishedAsync(f.Facts, f.Clock.GetUtcNow(), CancellationToken.None));
        Assert.True(f.Interval.Contains(f.Clock.GetUtcNow())); await AssertNoIdentityWrites(database);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSelectionBoundsPreserveBorrowedQualificationAtTiedInclusiveOrExclusiveEndpoints(bool includesBoundary)
    {
        await using var database = await fixture.CreateDatabaseAsync(); var clock = new AuthenticationTestHost.TestClock();
        var env = new PresentationContentTestEnvironment(database, clock); await env.InitializeAsync();
        var from = clock.GetUtcNow(); var until = from.AddSeconds(10);
        await using (var writer = env.Open())
        {
            env.Authorization.Actor = "fixture-author"; var proposal = await writer.Application.CreateDraftAsync(env.Draft("Synthetic shared bounds heading"));
            proposal = await writer.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-approver";
            proposal = await writer.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-publisher";
            _ = await writer.Application.PublishAsync(env.Publish(proposal) with { EffectiveInterval = new(from, true, until, true) });
        }
        clock.Advance(TimeSpan.FromTicks(1)); await using var identity = database.Context(); await using var transaction = await identity.Database.BeginTransactionAsync();
        var role = await identity.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync();
        var qualificationInterval = new EffectivePolicyInterval(from, includesBoundary, until, includesBoundary);
        var admission = new ContentTransactionReadQualification(new("fixture-shared-selection-read", "ACTIVE", env.Scope, qualificationInterval, null, ["SYNTHETIC-ONLY"]),
            "fixture-shared-selection-evidence", identity.Database.GetDbConnection().Database, role, 16, 16, 10, TimeSpan.FromSeconds(10));
        await using var owner = env.Open(); var request = new ContentResolutionRequest(env.Scope, env.Variant, clock.GetUtcNow());
        var normal = await owner.Application.ResolveAsync(request);
        var borrowed = await owner.Application.ResolveInTransactionAsync(request, identity.Database.GetDbConnection(), transaction.GetDbTransaction(), admission);
        Assert.NotNull(normal.EffectiveInterval); Assert.Equal(from, normal.EffectiveInterval.From); Assert.True(normal.EffectiveInterval.IncludesFrom);
        Assert.Equal(until, normal.EffectiveInterval.Until); Assert.True(normal.EffectiveInterval.IncludesUntil);
        Assert.Equal(from, borrowed.Availability.From); Assert.Equal(includesBoundary, borrowed.Availability.IncludesFrom);
        Assert.Equal(until, borrowed.Availability.Until); Assert.Equal(includesBoundary, borrowed.Availability.IncludesUntil);
        Assert.Equal(normal.Content["login.heading"], borrowed.Resolution.Content["login.heading"]);
        Assert.Same(transaction, identity.Database.CurrentTransaction); await transaction.RollbackAsync();
        await AssertNoIdentityWrites(database);
    }

    private static async Task AssertNoIdentityWrites(IdentityTestDatabase database)
    {
        await using var identity = database.Context(); Assert.Empty(await identity.Accounts.ToArrayAsync());
        Assert.Empty(await identity.Set<AuthenticationSessionRow>().ToArrayAsync()); Assert.Empty(await identity.Set<StepUpProofRow>().ToArrayAsync());
    }
}
