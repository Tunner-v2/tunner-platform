using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Tunner.Platform.Bff;

/// <summary>
/// Trusted host registration for one terminal form-post protocol endpoint. This supplies no identity,
/// authorization or protocol validation. Ordinary endpoints never inherit its transport exception.
/// </summary>
public sealed class BffProtocolCallback
{
    private readonly Func<HttpContext, Task> handler;
    private readonly int maximumBodyBytes;
    private readonly FormOptions formOptions;

    public BffProtocolCallback(string path, int maximumBodyBytes, int maximumFields, int maximumKeyLength,
        int maximumValueLength, Func<HttpContext, Task> handler)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 2 || path[0] != '/' || path[^1] == '/' ||
            path.Contains("//", StringComparison.Ordinal) || path.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '/' and not '-' and not '_'))
            throw new ArgumentException("An exact canonical callback path is required.", nameof(path));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBodyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFields);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumKeyLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumValueLength);
        ArgumentNullException.ThrowIfNull(handler);
        Path = path; this.maximumBodyBytes = maximumBodyBytes; this.handler = handler;
        formOptions = new() { ValueCountLimit = maximumFields, KeyLengthLimit = maximumKeyLength, ValueLengthLimit = maximumValueLength };
    }

    public string Path { get; }

    internal async Task HandleAsync(HttpContext context)
    {
        // Root-host callbacks only. Do not accept a decoded alias, a query-carried response or multipart.
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (context.Request.PathBase.HasValue || !StringComparer.Ordinal.Equals(rawTarget, Path) ||
            !HttpMethods.IsPost(context.Request.Method) || context.Request.QueryString.HasValue ||
            !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType) ||
            !contentType.MediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (context.Request.ContentLength > maximumBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        // Enforce the same bound for chunked/unknown-length bodies, independently of server features.
        var originalBody = context.Request.Body;
        using var bounded = new MemoryStream();
        var buffer = new byte[Math.Min(maximumBodyBytes, 8192)];
        try
        {
            while (true)
            {
                var remaining = (long)maximumBodyBytes - bounded.Length;
                var read = await originalBody.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), context.RequestAborted).ConfigureAwait(false);
                if (read == 0) break;
                if (read > remaining)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                await bounded.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted).ConfigureAwait(false);
            }
            bounded.Position = 0;
            context.Request.Body = bounded;
            context.Features.Set<IFormFeature>(new FormFeature(context.Request, formOptions));
            IFormCollection form;
            try { form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false); }
            catch (InvalidDataException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (form.Any(field => field.Value.Count != 1))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await handler(context).ConfigureAwait(false);
            // No next() path exists: even an unhandled native request cannot become an ordinary request.
        }
        finally
        {
            context.Request.Body = originalBody;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
            if (bounded.TryGetBuffer(out var segment)) System.Security.Cryptography.CryptographicOperations.ZeroMemory(segment.AsSpan());
        }
    }
}
