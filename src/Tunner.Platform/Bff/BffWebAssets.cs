using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;

namespace Tunner.Platform.Bff;

/// <summary>Shared immutable web packaging. Owners retain canonical paths, authentication and workflow guards.</summary>
public sealed class BffWebAssets(string directoryName, string routePrefix)
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "wwwroot", directoryName);
    private int mapped;

    public void MapAssets(IEndpointRouteBuilder endpoints, bool allowAnonymous)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (Interlocked.Exchange(ref mapped, 1) != 0 || !Directory.Exists(root)) return;
        var types = new FileExtensionContentTypeProvider();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || !types.TryGetContentType(file, out var contentType)) continue;
            var route = routePrefix + Path.GetRelativePath(root, file).Replace('\\', '/');
            var endpoint = endpoints.MapGet(route, (HttpContext context) => Render(context, file, contentType));
            if (allowAnonymous) endpoint.AllowAnonymous();
        }
    }

    public void MapScreens(IEndpointRouteBuilder endpoints, string entryName, IEnumerable<string> paths,
        Func<HttpContext, Task<bool>> allowed, bool allowAnonymous, Func<HttpContext, IResult> rejected)
    {
        ArgumentNullException.ThrowIfNull(endpoints); ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(allowed); ArgumentNullException.ThrowIfNull(rejected);
        var entry = Path.Combine(root, entryName); if (!File.Exists(entry)) return;
        foreach (var path in paths)
        {
            var endpoint = endpoints.MapGet(path, async (HttpContext context) => await allowed(context).ConfigureAwait(false)
                ? Render(context, entry, "text/html") : rejected(context));
            if (allowAnonymous) endpoint.AllowAnonymous();
        }
    }

    private static IResult Render(HttpContext context, string path, string contentType)
    {
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.File(path, contentType);
    }
}
