using Tunner.Platform.Bff;
using Tunner.PresentationContent;

namespace Tunner.Admin.Bff;

internal static class AdminContentScreens
{
    private static readonly object FailureStatus = new();
    internal static void Map(IEndpointRouteBuilder endpoints, BffWebAssets assets)
    {
        assets.MapScreens(endpoints, "content.html", ["/admin/content", "/admin/content/review", "/admin/content/history"],
            Allowed, allowAnonymous: false, static context => Results.StatusCode(
                context.Items.TryGetValue(FailureStatus, out var status) && status is int code ? code : StatusCodes.Status403Forbidden));
    }

    private static async Task<bool> Allowed(HttpContext context)
    {
        try
        {
            if (context.Request.QueryString.HasValue) return false;
            var clock = context.RequestServices.GetRequiredService<TimeProvider>();
            var profiles = context.RequestServices.GetRequiredService<IAdminContentHttpProfileSource>();
            var profile = profiles.RequireEffective(clock.GetUtcNow()); profile.Require(clock.GetUtcNow());
            context.Items[AdminContentAuthorization.ProfileItem] = System.Text.Json.JsonSerializer.Serialize(profile);
            await AdminContentAuthorization.CurrentSession(context, context.RequestServices.GetRequiredService<IAdminContentSessionSource>(), clock,
                context.RequestAborted).ConfigureAwait(false);
            var application = context.RequestServices.GetRequiredService<PresentationContentApplication>();
            var budgets = context.RequestServices.GetRequiredService<IContentBrowseBudgetSource>();
            foreach (var scope in profile.Scopes)
            {
                var operation = Guid.NewGuid().ToString("N");
                await application.WorkspaceAsync(scope, budgets, "OP-CONTENT-" + operation, operation, context.RequestAborted).ConfigureAwait(false);
            }
            return true;
        }
        catch (ContentForbiddenException) { return false; }
        catch (Exception error) when (error is InvalidOperationException or System.Data.Common.DbException or TimeoutException)
        { context.Items[FailureStatus] = StatusCodes.Status503ServiceUnavailable; return false; }
    }
}
