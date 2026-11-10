using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;
using static Tunner.Identity.Persistence.Tests.TotpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class TotpFinalBoundaryTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    [Theory]
    [InlineData("idle")] [InlineData("method")]
    public async Task OperationProofFinalAccessTransitionCannotCommitReplayProofOrAttempt(string transition)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var profile = new Profile(); await using var host = await StartTotp(db, profile);
        host.Policies.Policy = host.Policies.Policy with { IdleLifetime = TimeSpan.FromSeconds(1) };
        var account = await host.SeedAsync(db); var enrolled = await Enroll(host, profile, await Login(host));
        await using var scope = host.Services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
        var session = await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, account), new(AuthenticationIdentity.SessionClaim, session.SessionId.ToString("N")), new(AuthenticationIdentity.OriginClaim, Origin)], AuthenticationIdentity.Scheme));
        var caller = StepUpCaller.FromPrincipal(principal, Origin, new string('A', 64));
        var owner = new UserStepUpOperationContext(new(caller), "OP-final-boundary-fixture", "fixture-final", new("fixture.final-proof", "USER_BFF"), "fixture-resource", 1, new string('B', 64), "fixture-proof");
        var calls = 0;
        host.Access.BeforeDecision = request =>
        {
            if (request.Operation == owner.Binding.Scope.Operation && ++calls == 3)
            {
                if (transition == "idle") host.Clock.Advance(TimeSpan.FromSeconds(2));
                else host.Policies.Policy = host.Policies.Policy with { Version = "fixture-auth-v2", PasswordAssurance = "fixture-password-v2" };
            }
        };
        var result = await scope.ServiceProvider.GetRequiredService<TotpApplication>().IssueOperationProofAsync(caller, owner,
            new() { RequestId = Guid.NewGuid(), AttemptId = Guid.NewGuid(), AuthenticatorReference = enrolled.Reference, Code = NativeCode(profile.Seed(enrolled.Reference), 1) }, CancellationToken.None);
        Assert.True(calls >= 3, "FINAL_ACCESS_TRANSITION_NOT_EXERCISED");
        await using var read = db.Context(); Assert.Equal(1, await read.Set<TotpUsedCodeRow>().CountAsync());
        Assert.Empty(await read.Set<TotpProofAttemptRow>().ToArrayAsync()); Assert.False(await read.Set<StepUpProofRow>().AnyAsync(x => x.OperationId == owner.Binding.OperationId));
        Assert.Equal("STEP_UP_REQUIRED", result.ErrorCode); Assert.Null(result.Grant);
    }
}
