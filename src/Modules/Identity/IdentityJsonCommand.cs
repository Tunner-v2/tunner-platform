using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Tunner.Identity.Persistence;

/// <summary>Shared credential command parsing. Callers retain their own content-type/domain rules.</summary>
internal static class IdentityJsonCommand
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    internal static async Task<T> ReadAsync<T>(HttpContext context, long maximumBytes) where T : class
    {
        if (context.Request.ContentLength > maximumBytes) throw new BadHttpRequestException("Request body exceeds the command limit.", 413);
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is null || feature.IsReadOnly) throw new InvalidOperationException("Bounded credential input is unavailable.");
        feature.MaxRequestBodySize = maximumBytes;
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
        if (buffer.Length > maximumBytes) throw new BadHttpRequestException("Request body exceeds the command limit.", 413);
        using var json = JsonDocument.Parse(buffer.ToArray());
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("A command object is required.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (json.RootElement.EnumerateObject().Any(p => !names.Add(p.Name))) throw new JsonException("Duplicate command properties are not supported.");
        return json.Deserialize<T>(WebJson) ?? throw new JsonException("A command is required.");
    }
}
