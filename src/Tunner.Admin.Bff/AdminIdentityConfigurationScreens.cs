using System.Text.Json;
using Tunner.Identity.Persistence;
using Tunner.Platform.Bff;

namespace Tunner.Admin.Bff;

internal static class AdminIdentityConfigurationScreens
{
    private static readonly object FailureStatus = new();
    internal static void Map(IEndpointRouteBuilder endpoints, BffWebAssets assets) => assets.MapScreens(endpoints, "configuration.html",
        ["/identity/settings/authentication", "/identity/settings/authentication/review", "/identity/settings/authentication/history",
         "/identity/settings/assurance", "/identity/settings/assurance/review", "/identity/settings/assurance/history",
         "/identity/settings/authentication-content", "/identity/settings/authentication-content/review", "/identity/settings/authentication-content/history"],
        Allowed, allowAnonymous: false, static context => Results.StatusCode(
            context.Items.TryGetValue(FailureStatus, out var status) && status is int code ? code : StatusCodes.Status403Forbidden));

    private static async Task<bool> Allowed(HttpContext context)
    {
        try
        {
            if (context.Request.QueryString.HasValue) return false;
            var clock = context.RequestServices.GetRequiredService<TimeProvider>();
            var profile = context.RequestServices.GetRequiredService<IAdminIdentityConfigurationHttpProfileSource>().RequireEffective(clock.GetUtcNow());
            profile.Require(clock.GetUtcNow());
            context.Items[AdminIdentityConfigurationAuthorization.ProfileItem] = JsonSerializer.Serialize(profile);
            await AdminIdentityConfigurationAuthorization.CurrentSession(context,
                context.RequestServices.GetRequiredService<IAdminIdentityConfigurationSessionSource>(), clock, context.RequestAborted).ConfigureAwait(false);
            var family = context.Request.Path.Value!.Split('/')[3] switch
            {
                "authentication" => IdentityConfigurationFamily.Authentication,
                "assurance" => IdentityConfigurationFamily.Assurance,
                "authentication-content" => IdentityConfigurationFamily.AuthenticationContent,
                _ => throw new IdentityConfigurationForbiddenException()
            };
            var scopes = profile.Scopes.Where(x => x.Family == family).ToArray(); if (scopes.Length == 0) return false;
            var application = context.RequestServices.GetRequiredService<IdentityRuntimeConfigurationApplication>();
            var budgets = context.RequestServices.GetRequiredService<IIdentityConfigurationBrowseBudgetSource>();
            foreach (var scope in scopes)
            {
                var operation = Guid.NewGuid().ToString("N");
                await application.WorkspaceAsync(scope, budgets, "OP-IDENTITY-CONFIG-" + operation, operation, context.RequestAborted).ConfigureAwait(false);
            }
            return true;
        }
        catch (IdentityConfigurationForbiddenException) { return false; }
        catch (Exception error) when (error is InvalidOperationException or System.Data.Common.DbException or TimeoutException)
        { context.Items[FailureStatus] = StatusCodes.Status503ServiceUnavailable; return false; }
    }
}
