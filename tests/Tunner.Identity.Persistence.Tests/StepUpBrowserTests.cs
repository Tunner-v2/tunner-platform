using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class StepUpBrowserTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    internal static readonly AssurancePolicyScope Scope = new("fixture.change-value", "fixture.user-bff");
    internal sealed class Owner(IdentityTestDatabase database) : IUserStepUpOperationSource
    {
        internal string Resource { get; set; } = "fixture-resource";
        internal string ExpectedPolicy { get; set; } = "fixture-step-v1";
        internal bool Missing { get; set; }
        internal bool ThrowMutation { get; set; }
        internal bool ThrowResolve { get; set; }
        public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken cancellationToken)
        {
            if (ThrowResolve) throw new InvalidOperationException("Synthetic owner unavailable.");
            if (Missing) return null;
            await using var read = database.Context();
            var done = await read.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM identity.step_up_browser_target WHERE session_id={actor.SessionId} AND operation_id={OperationId(actor)}").SingleAsync(cancellationToken);
            return new(actor, OperationId(actor), "fixture-correlation", Scope, Resource, 1, new string('A',64), ExpectedPolicy, done == 1);
        }
        private static string OperationId(UserStepUpActor actor) => "OP-fixture-" + actor.SessionId.ToString("N");
        public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken cancellationToken)
        {
            Assert.NotNull(context.Database.CurrentTransaction);
            var changed = await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.step_up_browser_target SET value=value+1, version=version+1, session_id={actor.SessionId}, operation_id={OperationId(actor)} WHERE id=1 AND version={operation.Binding.ExpectedVersion}", cancellationToken);
            if (ThrowMutation) { await context.SaveChangesAsync(cancellationToken); throw new InvalidOperationException("Synthetic local action failure."); }
            return changed == 1;
        }
    }
    internal sealed class Policies(TimeProvider clock) : IAssurancePolicySource
    {
        internal bool Available { get; set; } = true;
        internal bool PasswordAllowed { get; set; } = true;
        internal string Version { get; set; } = "fixture-step-v1";
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null)
        {
            if (!Available) throw new InvalidOperationException("Synthetic assurance unavailable.");
            var policy = new AssurancePolicyVersion(new(Version, "ACTIVE", Scope,
                new EffectivePolicyInterval(clock.GetUtcNow().AddHours(-1), true, clock.GetUtcNow().AddHours(1), false), null, ["SYNTHETIC-TEST-ONLY"]),
                PasswordAllowed ? "fixture-password" : "fixture-stronger", TimeSpan.FromSeconds(30), new Dictionary<string,string> { ["pwd"]="fixture-password" });
            return new AssurancePolicyCatalog([policy]).RequireEffective(scope, executedAt, expectedVersion);
        }
    }
    internal sealed class Presentation : IStepUpPresentationSource
    {
        public StepUpPresentation RequirePublished(AssurancePolicyVersion policy)
        {
            using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "step-up-content.proposal.json")));
            return new("en", source.RootElement.GetProperty("content").EnumerateObject().Select(p =>
                new RegistrationPublishedText(p.Name,"synthetic-variant","synthetic-version","SYNTHETIC-TEST-ONLY","en","PUBLISHED",p.Value.GetString()!)));
        }
    }
    private sealed record Boot(Guid RequestId, string Csrf, string Cookies);
    private static async Task Initialize(IdentityTestDatabase db)
    {
        await using var context = db.Context();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE identity.step_up_browser_target (id integer PRIMARY KEY, version bigint NOT NULL, value integer NOT NULL, session_id uuid NULL, operation_id text NULL); INSERT INTO identity.step_up_browser_target VALUES(1,1,0,NULL,NULL);");
    }
    private static Task<AuthenticationTestHost> StartBridge(IdentityTestDatabase db, Owner owner, Policies? policy = null) => Start(db, services =>
    {
        services.AddSingleton<IUserStepUpOperationSource>(owner);
        services.AddSingleton<IAssurancePolicySource>(sp => policy ?? new Policies(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IStepUpPresentationSource, Presentation>();
    });
    private static async Task<string> Login(AuthenticationTestHost host, string? existing = null)
    {
        var boot = await host.Options(existing);
        using var signed = await host.Post("/authentication/sign-in", Command(boot.Key), boot.Cookies, boot.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); return Merge(boot.Cookies,signed);
    }
    private static async Task<Boot> Options(AuthenticationTestHost host, string cookies)
    {
        using var response = await host.Get("/authentication/step-up/options", cookies); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDING",json.GetProperty("status").GetString());
        return new(json.GetProperty("requestId").GetGuid(),json.GetProperty("csrfToken").GetString()!,Merge(cookies,response));
    }
    private static async Task<int> Value(IdentityTestDatabase db)
    {
        await using var read = db.Context(); return await read.Database.SqlQueryRaw<int>("SELECT value AS \"Value\" FROM identity.step_up_browser_target WHERE id=1").SingleAsync();
    }
    private static async Task<string?> Code(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString();

    [Fact]
    public async Task CompletionRecoveryIdentifiesTheProtectedRequestAndNeverAttributesAnotherSessionsResult()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await Initialize(db); var owner = new Owner(db);
        await using var host = await StartBridge(db, owner); await host.SeedAsync(db);
        var first = await Options(host, await Login(host));
        var second = await Options(host, await Login(host)); Assert.NotEqual(first.RequestId, second.RequestId);
        using (var applied = await host.Post("/authentication/step-up/password", new { requestId = second.RequestId, password = Password }, second.Cookies, second.Csrf))
            Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        using (var completed = await host.Get("/authentication/step-up/options", second.Cookies))
        {
            var json = await completed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("COMPLETED", json.GetProperty("status").GetString());
            Assert.Equal(second.RequestId, json.GetProperty("requestId").GetGuid());
        }
        var firstView = first.Cookies.Split(';', StringSplitOptions.TrimEntries).Single(x => x.StartsWith(StepUpBrowserEndpoints.CookieName + "=", StringComparison.Ordinal));
        var staleView = string.Join("; ", second.Cookies.Split(';', StringSplitOptions.TrimEntries)
            .Where(x => !x.StartsWith(StepUpBrowserEndpoints.CookieName + "=", StringComparison.Ordinal)).Append(firstView));
        using (var completed = await host.Get("/authentication/step-up/options", staleView))
        {
            var json = await completed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("COMPLETED", json.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("requestId").ValueKind);
        }
        Assert.Equal(1, await Value(db));
    }

    [Fact]
    public async Task BoundBrowserActionCompletesOnceReconcilesRetryAndNeverReturnsProofMaterial()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await Initialize(db); var owner = new Owner(db);
        await using var host = await StartBridge(db, owner); await host.SeedAsync(db); var signed = await Login(host);
        using var first = await host.Get("/authentication/step-up/options",signed); Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        var json = await first.Content.ReadFromJsonAsync<JsonElement>(); var cookies = Merge(signed,first);
        var cookie = Assert.Single(first.Headers.GetValues("Set-Cookie"),x=>x.StartsWith(StepUpBrowserEndpoints.CookieName+"=",StringComparison.Ordinal));
        Assert.Contains("secure",cookie,StringComparison.OrdinalIgnoreCase);Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);Assert.Contains("samesite=strict",cookie,StringComparison.OrdinalIgnoreCase);
        Assert.True(first.Headers.CacheControl?.NoStore); Assert.Equal("no-referrer",Assert.Single(first.Headers.GetValues("Referrer-Policy")));
        Assert.True(json.GetProperty("passwordAvailable").GetBoolean()); Assert.DoesNotContain("fixture-resource",await first.Content.ReadAsStringAsync());
        var boot = await Options(host,cookies); Assert.Equal(json.GetProperty("requestId").GetGuid(),boot.RequestId);
        var command = new { requestId=boot.RequestId,password=Password };
        using (var result = await host.Post("/authentication/step-up/password",command,boot.Cookies,boot.Csrf))
        { Assert.Equal(HttpStatusCode.OK,result.StatusCode);Assert.Equal("{\"status\":\"COMPLETED\"}",await result.Content.ReadAsStringAsync()); }
        using (var retry = await host.Post("/authentication/step-up/password",command,boot.Cookies,boot.Csrf)) Assert.Equal(HttpStatusCode.OK,retry.StatusCode);
        using (var status = await host.Get("/authentication/step-up/options",boot.Cookies))
            Assert.Equal("COMPLETED",(await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(1,await Value(db)); await using var check = db.Context(); var proof = await check.Set<StepUpProofRow>().SingleAsync();
        Assert.Equal("CONSUMED",proof.State);Assert.Empty(proof.ProtectedKey);Assert.Equal(1,await check.Set<OutboxMessage>().CountAsync(x=>x.EventType=="StepUpProofConsumed"));
        Assert.DoesNotContain(proof.KeyDigest,await first.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("empty")][InlineData("long")][InlineData("wrong")]
    public async Task BackendValidationAndCredentialFailureCannotApplyAction(string kind)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await Initialize(db);
        await using var host = await StartBridge(db,new(db));await host.SeedAsync(db);var boot=await Options(host,await Login(host));
        var password=kind=="empty"?"":kind=="long"?new string('x',129):"wrong";
        using var result=await host.Post("/authentication/step-up/password",new{requestId=boot.RequestId,password},boot.Cookies,boot.Csrf);
        Assert.Equal(kind=="wrong"?HttpStatusCode.Unauthorized:HttpStatusCode.BadRequest,result.StatusCode);Assert.Equal(0,await Value(db));
    }

    [Theory]
    [InlineData("unknown")][InlineData("duplicate")][InlineData("malformed")][InlineData("array")][InlineData("oversize")]
    public async Task StrictBoundedJsonRejectsAuthorityInjectionAndInvalidBodies(string kind)
    {
        await using var db=await fixture.CreateDatabaseAsync();await Initialize(db);
        await using var host=await StartBridge(db,new(db));await host.SeedAsync(db);var boot=await Options(host,await Login(host));
        var normal="{\"requestId\":\""+boot.RequestId+"\",\"password\":\"wrong\"";
        var raw=kind switch {"unknown"=>normal+",\"assurance\":\"trusted\"}","duplicate"=>normal+",\"Password\":\"other\"}","malformed"=>"{",
            "array"=>"[]",_=>normal+",\"padding\":\""+new string('x',6000)+"\"}"};
        using var response=await host.RawPost("/authentication/step-up/password",raw,boot.Cookies,boot.Csrf);
        Assert.Equal(kind=="oversize"?HttpStatusCode.RequestEntityTooLarge:HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,await Value(db));
    }

    [Theory]
    [InlineData("csrf")][InlineData("origin")][InlineData("cookie")][InlineData("request")][InlineData("operation")][InlineData("session")][InlineData("expired")]
    public async Task BrowserContextCannotBeReusedAfterChangingItsAuthority(string change)
    {
        await using var db=await fixture.CreateDatabaseAsync();await Initialize(db);var owner=new Owner(db);
        await using var host=await StartBridge(db,owner);await host.SeedAsync(db);var boot=await Options(host,await Login(host));
        var cookies=boot.Cookies;
        if(change=="cookie")cookies=cookies.Replace(StepUpBrowserEndpoints.CookieName+"=",StepUpBrowserEndpoints.CookieName+"=tampered",StringComparison.Ordinal);
        if(change=="session")cookies=await Login(host,cookies);
        if(change=="operation")owner.Resource="different-resource";
        if(change=="expired")host.Clock.Advance(TimeSpan.FromMinutes(2));
        using var response=await host.Post("/authentication/step-up/password",new{requestId=change=="request"?Guid.NewGuid():boot.RequestId,password=Password},
            cookies,change=="csrf"?"forged":boot.Csrf,change=="origin"?"https://other.example.test":Origin);
        Assert.False(response.IsSuccessStatusCode);Assert.Equal(0,await Value(db));
    }

    [Fact]
    public async Task UnauthenticatedOrUnactivatedSurfaceHasNoUsableBridge()
    {
        Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddTunnerOperationStepUp());
        await using var db=await fixture.CreateDatabaseAsync();await Initialize(db);
        await using(var host=await StartBridge(db,new(db)))
        {using var response=await host.Get("/authentication/step-up/options");Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);}
        await using(var host=await Start(db))
        {await host.SeedAsync(db);using var response=await host.Get("/authentication/step-up/options",await Login(host));Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);}
    }

    [Fact]
    public async Task ChangedPolicyAndUnavailableOwnerFailClosedWhileCompletedResultNeedsNoNewAssurance()
    {
        await using var db=await fixture.CreateDatabaseAsync();await Initialize(db);var owner=new Owner(db);var policy=new Policies(new TestClock());
        await using var host=await StartBridge(db,owner,policy);await host.SeedAsync(db);var boot=await Options(host,await Login(host));
        policy.Version="fixture-step-v2";
        using(var stale=await host.Post("/authentication/step-up/password",new{requestId=boot.RequestId,password=Password},boot.Cookies,boot.Csrf))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        policy.Version="fixture-step-v1";owner.ThrowResolve=true;
        using(var failed=await host.Get("/authentication/step-up/options",boot.Cookies))Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);
        owner.ThrowResolve=false;
        using(var applied=await host.Post("/authentication/step-up/password",new{requestId=boot.RequestId,password=Password},boot.Cookies,boot.Csrf))Assert.Equal(HttpStatusCode.OK,applied.StatusCode);
        policy.Available=false;
        using var completed=await host.Get("/authentication/step-up/options",boot.Cookies);Assert.Equal(HttpStatusCode.OK,completed.StatusCode);
        Assert.Equal("COMPLETED",(await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());Assert.Equal(1,await Value(db));
    }

    [Fact]
    public async Task InsufficientPasswordAssuranceIsAdvertisedAndCannotApply()
    {
        await using var db=await fixture.CreateDatabaseAsync();await Initialize(db);var policy=new Policies(new TestClock()){PasswordAllowed=false};
        await using var host=await StartBridge(db,new(db),policy);await host.SeedAsync(db);var signed=await Login(host);
        using var options=await host.Get("/authentication/step-up/options",signed);var json=await options.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("passwordAvailable").GetBoolean());
        using var result=await host.Post("/authentication/step-up/password",new{requestId=json.GetProperty("requestId").GetGuid(),password=Password},Merge(signed,options),json.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);Assert.Equal("STEP_UP_REQUIRED",await Code(result));Assert.Equal(0,await Value(db));
    }

    [Fact]
    public async Task ThrownOwnerMutationRollsBackAndConcurrentRetryNeverDuplicatesTheAction()
    {
        await using var db=await fixture.CreateDatabaseAsync();await Initialize(db);var owner=new Owner(db){ThrowMutation=true};
        await using var host=await StartBridge(db,owner);await host.SeedAsync(db);var boot=await Options(host,await Login(host));
        var command=new{requestId=boot.RequestId,password=Password};
        using(var failed=await host.Post("/authentication/step-up/password",command,boot.Cookies,boot.Csrf))Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);
        Assert.Equal(0,await Value(db));owner.ThrowMutation=false;
        var results=await Task.WhenAll(host.Post("/authentication/step-up/password",command,boot.Cookies,boot.Csrf),host.Post("/authentication/step-up/password",command,boot.Cookies,boot.Csrf));
        try{Assert.All(results,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode));}finally{foreach(var r in results)r.Dispose();}
        Assert.Equal(1,await Value(db));
    }
}
