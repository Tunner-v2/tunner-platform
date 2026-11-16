using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class ContentChangeReasonTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ReasonIsRetainedInImmutableHistoryAndNeverCopiedIntoEffects()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var environment = new PresentationContentTestEnvironment(database, new TestClock()); await environment.InitializeAsync();
        await using var scope = environment.Open();
        var draft = await scope.Application.CreateDraftAsync(environment.Draft("Fixture wording") with { ChangeReason = "fixture-private-review-reason" });
        var preview = await scope.Application.PreviewAsync(environment.Command(draft)); Assert.Equal("fixture-private-review-reason", preview.ChangeReason);
        draft = await scope.Application.ValidateAsync(environment.Command(draft)); environment.Authorization.Actor = "fixture-approver";
        draft = await scope.Application.ApproveAsync(environment.Command(draft)); environment.Authorization.Actor = "fixture-publisher";
        var publication = await scope.Application.PublishAsync(environment.Publish(draft));
        var history = await scope.Application.HistoryAsync(environment.Scope, "login.heading", environment.Variant, "OP-reason-history", "fixture-correlation");
        Assert.Equal("fixture-private-review-reason", publication.ChangeReason);
        Assert.Equal(publication.ChangeReason, Assert.Single(history).ChangeReason);
        await using var platform = environment.Platform();
        Assert.All(await platform.OutboxMessages.ToArrayAsync(), x => Assert.DoesNotContain("fixture-private-review-reason", x.Payload, StringComparison.Ordinal));
        Assert.All(await platform.AuditEvents.ToArrayAsync(), x => Assert.DoesNotContain("fixture-private-review-reason", x.Envelope, StringComparison.Ordinal));
        await using var context = environment.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync("20261116061227_P2PresentationContentFoundation"));
        Assert.Equal("23514", error.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE presentation_content.versions SET \"ChangeReason\"='forged' WHERE \"Id\"={publication.VersionReference}"));
    }

    [Fact]
    public async Task InvalidReasonCannotMutateOrPublishTheDraft()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var environment = new PresentationContentTestEnvironment(database, new TestClock()); await environment.InitializeAsync();
        await using var scope = environment.Open();
        await Assert.ThrowsAsync<ArgumentException>(() => scope.Application.CreateDraftAsync(environment.Draft("Fixture wording") with { ChangeReason = "unsafe\0reason" }));
        await using var platform = environment.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync());
    }
}
