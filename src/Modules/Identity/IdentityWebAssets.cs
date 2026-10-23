using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;

namespace Tunner.Identity.Persistence;

/// <summary>Shared Identity UI packaging. Each owner declares only its canonical screens and workflow guards.</summary>
internal sealed class IdentityWebAssets
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "wwwroot", "registration");
    private int mapped;
    internal void MapAssets(IEndpointRouteBuilder endpoints)
    {
        if (Interlocked.Exchange(ref mapped, 1) != 0 || !Directory.Exists(root)) return;
        var types = new FileExtensionContentTypeProvider();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || !types.TryGetContentType(file, out var contentType)) continue;
            var route = "/registration/" + Path.GetRelativePath(root, file).Replace('\\', '/');
            endpoints.MapGet(route, (HttpContext context) => Render(context, file, contentType)).AllowAnonymous();
        }
    }
    internal void MapScreens(IEndpointRouteBuilder endpoints, string entryName, IEnumerable<string> paths, Func<HttpContext, bool> allowed)
    {
        var entry = Path.Combine(root, entryName);
        if (!File.Exists(entry)) return;
        foreach (var path in paths) endpoints.MapGet(path, (HttpContext context) => allowed(context) ? Render(context, entry, "text/html") : Results.LocalRedirect("/login")).AllowAnonymous();
    }
    private static IResult Render(HttpContext context, string path, string contentType)
    {
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.File(path, contentType);
    }
}
