using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;
using static Tunner.Identity.Persistence.Tests.TotpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class TotpProofContinuationTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OwnerFailureContinuesOnlyItsOriginalActiveProofWithoutCodeAndDisabledSourceFailsClosed(bool disableSource)
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using (var setup = db.Context()) await setup.Database.ExecuteSqlRawAsync("CREATE TABLE identity.step_up_browser_target (id integer PRIMARY KEY, version bigint NOT NULL, value integer NOT NULL, session_id uuid NULL, operation_id text NULL); INSERT INTO identity.step_up_browser_target VALUES(1,1,0,NULL,NULL);");
        var owner = new StepUpBrowserTests.Owner(db) { ExpectedPolicy = "fixture-proof", ThrowMutation = true }; var profile = new Profile();
        await using var host = await StartTotp(db, profile, s => s.AddSingleton<IUserStepUpOperationSource>(owner)); await host.SeedAsync(db);
        var factor = await Enroll(host, profile, await Login(host));
        using var options = await host.Get("/authentication/step-up/options", factor.Cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var view = await options.Content.ReadFromJsonAsync<JsonElement>(); var requestId = view.GetProperty("requestId").GetGuid(); var csrf = view.GetProperty("csrfToken").GetString(); var cookies = Merge(factor.Cookies, options); var attempt = Guid.NewGuid();
        var eligible = view.GetProperty("totpAuthenticators").EnumerateArray().Single(); Assert.Equal(factor.Reference, eligible.GetProperty("authenticatorReference").GetGuid()); Assert.Equal(1, eligible.GetProperty("displayPosition").GetInt32());
        using (var failedOwner = await host.Post("/authentication/step-up/totp", new { requestId, attemptId = attempt, authenticatorReference = factor.Reference, code = NativeCode(profile.Seed(factor.Reference), 1) }, cookies, csrf))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, failedOwner.StatusCode); cookies = Merge(cookies, failedOwner); }
        await using (var after = db.Context()) { Assert.Equal(2, await after.Set<TotpUsedCodeRow>().CountAsync()); Assert.Equal("ACTIVE", await after.Set<StepUpProofRow>().Where(x => x.Operation == StepUpBrowserTests.Scope.Operation).Select(x => x.State).SingleAsync()); }
        using var recovery = await host.Get("/authentication/step-up/options", cookies); Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var recoveryView = await recovery.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("PROOF_ACTIVE", recoveryView.GetProperty("totpRecovery").GetProperty("status").GetString()); cookies = Merge(cookies, recovery);
        using (var wrong = await host.Post("/authentication/step-up/totp/continue", new { requestId, attemptId = Guid.NewGuid(), authenticatorReference = factor.Reference }, cookies, csrf)) Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        if (disableSource) await Authorize(host, await Begin(host, factor.Cookies, "DISABLE", factor.Reference, 2));
        owner.ThrowMutation = false;
        using var continued = await host.Post("/authentication/step-up/totp/continue", new { requestId, attemptId = attempt, authenticatorReference = factor.Reference }, cookies, csrf);
        Assert.Equal(disableSource ? HttpStatusCode.Forbidden : HttpStatusCode.OK, continued.StatusCode);
        if (!disableSource)
        {
            cookies = Merge(cookies, continued);
            using var repeated = await host.Post("/authentication/step-up/totp/continue", new { requestId, attemptId = attempt, authenticatorReference = factor.Reference }, cookies, csrf); Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        }
        await using var check = db.Context(); Assert.Equal(2, await check.Set<TotpUsedCodeRow>().CountAsync());
        Assert.Equal(disableSource ? 0 : 1, await check.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync());
        Assert.Equal(disableSource ? "ACTIVE" : "CONSUMED", await check.Set<StepUpProofRow>().Where(x => x.Operation == StepUpBrowserTests.Scope.Operation).Select(x => x.State).SingleAsync());
    }
}
