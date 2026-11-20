using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tunner.Platform.Bff;

namespace Tunner.Identity.Persistence;

/// <summary>Shared Identity UI packaging. Each owner declares only its canonical screens and workflow guards.</summary>
internal sealed class IdentityWebAssets
{
    private readonly BffWebAssets assets = new("registration", "/registration/");
    internal void MapAssets(IEndpointRouteBuilder endpoints)
    {
        assets.MapAssets(endpoints, allowAnonymous: true);
    }
    internal void MapScreens(IEndpointRouteBuilder endpoints, string entryName, IEnumerable<string> paths, Func<HttpContext, bool> allowed)
    {
        assets.MapScreens(endpoints, entryName, paths, context => Task.FromResult(allowed(context)),
            allowAnonymous: true, static _ => Results.LocalRedirect("/login"));
    }
    internal void MapScreensAsync(IEndpointRouteBuilder endpoints, string entryName, IEnumerable<string> paths, Func<HttpContext, Task<bool>> allowed, Func<HttpContext, string>? rejected = null)
    {
        assets.MapScreens(endpoints, entryName, paths, allowed, allowAnonymous: true,
            context => Results.LocalRedirect(rejected?.Invoke(context) ?? "/login"));
    }
}
