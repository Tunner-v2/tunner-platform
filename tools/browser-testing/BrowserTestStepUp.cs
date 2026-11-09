using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Identity.Persistence;

internal sealed class BrowserTestStepUpOperations(IServiceScopeFactory scopes) : IUserStepUpOperationSource
{
    internal const string PolicyVersion = "browser-test-step-up-v1";
    internal static readonly AssurancePolicyScope Scope = new("LOCAL_TEST_VERIFY_IDENTITY", "ISOLATED_LOCAL_BROWSER_TESTING");
    internal const string Schema = """
        CREATE SCHEMA IF NOT EXISTS browser_testing;
        CREATE TABLE IF NOT EXISTS browser_testing.step_up_results (
          operation_id text PRIMARY KEY, account_id text NOT NULL, session_id uuid NOT NULL,
          recorded_at_utc timestamptz NOT NULL, version bigint NOT NULL CHECK (version=1)
        );
        """;
    public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
        var operation = Operation(actor);
        var count = await database.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM browser_testing.step_up_results WHERE operation_id={operation} AND account_id={actor.AccountId} AND session_id={actor.SessionId}").SingleAsync(cancellationToken);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { actor.AccountId, actor.SessionId.ToString("N"), actor.Origin, "SYNTHETIC-LOCAL-TEST-ONLY" }))));
        return new(actor, operation, operation, Scope, "LOCAL-VERIFICATION-" + actor.SessionId.ToString("N"), 0, digest, PolicyVersion, count == 1);
    }
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext database, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null || operation.ExpectedPolicyVersion != PolicyVersion ||
            operation.Actor.AccountId != actor.AccountId || operation.Actor.SessionId != actor.SessionId || operation.Actor.Origin != actor.Origin)
            throw new InvalidOperationException("A current isolated test action transaction is required.");
        var id = Operation(actor); var now = DateTimeOffset.UtcNow;
        return await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO browser_testing.step_up_results(operation_id,account_id,session_id,recorded_at_utc,version) VALUES({id},{actor.AccountId},{actor.SessionId},{now},1) ON CONFLICT DO NOTHING", cancellationToken) == 1;
    }
    private static string Operation(UserStepUpActor actor) => "OP-LOCAL-STEP-UP-" + actor.SessionId.ToString("N");
}
internal sealed class BrowserTestStepUpPresentation : IStepUpPresentationSource
{
    public StepUpPresentation RequirePublished(AssurancePolicyVersion policy)
    {
        if (policy.Metadata.VersionId is not (BrowserTestStepUpOperations.PolicyVersion or BrowserTestPasskeys.EnrollmentPolicyVersion or BrowserTestPasskeys.RevocationPolicyVersion))
            throw new InvalidOperationException("Isolated test presentation only.");
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-step-up-content.json")));
        return new("en", source.RootElement.GetProperty("content").EnumerateObject().Select(p =>
            new RegistrationPublishedText(p.Name,"synthetic-local-test","synthetic-local-test","SYNTHETIC-LOCAL-TEST-ONLY","en","PUBLISHED",p.Value.GetString()!)));
    }
}
internal static class BrowserTestStepUpAssets
{
    internal static void Map(WebApplication app)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot", "step-up");
        if (!Directory.Exists(root)) throw new InvalidOperationException("Build the isolated step-up fixture before starting.");
        var types = new FileExtensionContentTypeProvider();
        foreach (var file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
        {
            if (file.EndsWith(".html",StringComparison.OrdinalIgnoreCase) || !types.TryGetContentType(file,out var type)) continue;
            var route = "/browser-testing/step-up-assets/" + Path.GetRelativePath(root,file).Replace('\\','/');
            app.MapGet(route, () => Results.File(file,type)).AllowAnonymous();
        }
        app.MapGet("/browser-testing/step-up", (HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated != true) return Results.LocalRedirect("/login");
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.File(Path.Combine(root,"index.html"),"text/html");
        }).AllowAnonymous();
    }
}
