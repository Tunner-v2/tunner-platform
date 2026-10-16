using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit opt-in User BFF routes. The shared BFF HTTPS/origin/CSRF middleware remains mandatory.</summary>
public static class RegistrationEndpoints
{
    public const string BrowserCookieName = "__Host-Tunner.Registration";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddTunnerRegistrationApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(IRegistrationPolicySource), typeof(IRegistrationPasswordBlocklist),
            typeof(IRegistrationNotificationDispatcher), typeof(IRegistrationPresentationSource),
            typeof(RegistrationNetworkPartitionKey), typeof(IdentityPersistenceDbContext)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)))
            throw new InvalidOperationException("Registration requires explicit policy, blocklist, notification, content, key and persistence providers.");
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRegistrationApplication, RegistrationApplication>();
        return services;
    }

    public static IEndpointRouteBuilder MapTunnerRegistration(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/registration/options", (HttpContext context, IRegistrationPolicySource source,
            IRegistrationPresentationSource content, IAntiforgery antiforgery, IDataProtectionProvider protection, TimeProvider clock) =>
        {
            RegistrationPolicy policy;
            try { policy = source.RequireEffective(clock.GetUtcNow()); policy.Validate(); }
            catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
            RegistrationPresentation presentation;
            try { presentation = content.RequirePublished(policy); }
            catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
            catch (ArgumentException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
            var protector = protection.CreateProtector("Tunner.User.Registration.Browser.v1");
            if (!TryBrowser(context, protector, clock.GetUtcNow(), out _))
            {
                var browser = new BrowserContext(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), clock.GetUtcNow() + policy.BrowserContextLifetime);
                context.Response.Cookies.Append(BrowserCookieName, protector.Protect(JsonSerializer.Serialize(browser)), new CookieOptions
                { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = policy.BrowserContextLifetime, IsEssential = true });
            }
            var csrf = antiforgery.GetAndStoreTokens(context);
            return Results.Json(new { csrfToken = csrf.RequestToken, serverTime = clock.GetUtcNow(), policyVersion = policy.Version, policy.TermsVersion, policy.PrivacyVersion,
                policy.PasswordMinimumLength, policy.PasswordMaximumLength, policy.ProofDigits,
                presentation.Locale, presentation.Content, presentation.TermsUri, presentation.PrivacyUri, presentation.SupportUri });
        }).AllowAnonymous();
        endpoints.MapGet("/registration/status/{key:guid}", async (HttpContext context, Guid key, IRegistrationApplication application,
            IDataProtectionProvider protection, RegistrationNetworkPartitionKey network, TimeProvider clock) =>
        {
            var protector = protection.CreateProtector("Tunner.User.Registration.Browser.v1");
            if (!TryBrowser(context, protector, clock.GetUtcNow(), out var browser) || context.Connection.RemoteIpAddress is null)
                return Problem(403, TechnicalProblemCodes.ForbiddenScope);
            var caller = new RegistrationCaller(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(browser!.Id))), network.Partition(context.Connection.RemoteIpAddress));
            try { return ToHttp(await application.FindAsync(caller, key, context.RequestAborted).ConfigureAwait(false)); }
            catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        }).AllowAnonymous();
        endpoints.MapPost("/registration/register", (HttpContext context, IRegistrationApplication application) =>
            InvokeAsync<RegisterAccountCommand>(context, (caller, command, ct) => application.RegisterAsync(caller, command, ct))).AllowAnonymous();
        endpoints.MapPost("/registration/verify", (HttpContext context, IRegistrationApplication application) =>
            InvokeAsync<VerifyRegistrationCommand>(context, (caller, command, ct) => application.VerifyAsync(caller, command, ct))).AllowAnonymous();
        endpoints.MapPost("/registration/resend", (HttpContext context, IRegistrationApplication application) =>
            InvokeAsync<ResendRegistrationCommand>(context, (caller, command, ct) => application.ResendAsync(caller, command, ct))).AllowAnonymous();
        MapPackagedWebAssets(endpoints);
        return endpoints;
    }

    private static async Task<IResult> InvokeAsync<T>(HttpContext context, Func<RegistrationCaller,T,CancellationToken,Task<RegistrationResult>> action) where T : class
    {
        if (!context.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var services = context.RequestServices;
        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.Registration.Browser.v1");
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        if (!TryBrowser(context, protector, now, out var browser) || context.Connection.RemoteIpAddress is null)
            return Problem(403, TechnicalProblemCodes.ForbiddenScope);
        var caller = new RegistrationCaller(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(browser!.Id))),
            services.GetRequiredService<RegistrationNetworkPartitionKey>().Partition(context.Connection.RemoteIpAddress));
        RegistrationPolicy policy;
        try { policy = services.GetRequiredService<IRegistrationPolicySource>().RequireEffective(now); policy.Validate(); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        var maximumBytes = checked((long)policy.PasswordMaximumLength * 6 + (long)(policy.TermsVersion.Length + policy.PrivacyVersion.Length) * 6 + 4096);
        var size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (context.Request.ContentLength > maximumBytes) return Problem(413, "VALIDATION_ERROR");
        if (size is null || size.IsReadOnly) return Problem(503, "DEPENDENCY_UNAVAILABLE");
        size.MaxRequestBodySize = maximumBytes;
        T? command;
        try { command = await context.Request.ReadFromJsonAsync<T>(WebJson, context.RequestAborted).ConfigureAwait(false); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException exception) { return Problem(exception.StatusCode == StatusCodes.Status413PayloadTooLarge ? 413 : 400, "VALIDATION_ERROR"); }
        if (command is null) return Problem(400, "VALIDATION_ERROR");
        RegistrationResult result;
        try { result = await action(caller, command, context.RequestAborted).ConfigureAwait(false); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        return ToHttp(result);
    }
    private static IResult ToHttp(RegistrationResult result)
    {
        if (result.ErrorCode is not { } code) return Results.Json(result, statusCode: result.Status == "REQUIRES_ACTION" ? 202 : 200);
        var status = code switch { "VERSION_CONFLICT" => 409, "RATE_OR_ABUSE_LIMITED" => 429, _ => 400 };
        return Problem(status, code, result);
    }
    private static IResult Problem(int status, string code, RegistrationResult? result = null) =>
        TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
            new TechnicalProblemMetadata(code, result?.CorrelationId ?? Guid.NewGuid().ToString("N"), "UNSPECIFIED")
            { OperationId = result is { OperationId: var id } && id != Guid.Empty ? "OP-" + id.ToString("N") : null,
                CurrentVersion = result is { Version: > 0 } ? result.Version : null });
    private static bool TryBrowser(HttpContext context, IDataProtector protector, DateTimeOffset now, out BrowserContext? browser)
    {
        browser = null;
        if (!context.Request.Cookies.TryGetValue(BrowserCookieName, out var cookie)) return false;
        try { browser = JsonSerializer.Deserialize<BrowserContext>(protector.Unprotect(cookie)); }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
        return browser is not null && browser.ExpiresAt > now && browser.Id.Length == 64 && browser.Id.All(Uri.IsHexDigit);
    }
    private static void MapPackagedWebAssets(IEndpointRouteBuilder endpoints)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot", "registration");
        if (!Directory.Exists(root)) return;
        var types = new FileExtensionContentTypeProvider();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!types.TryGetContentType(path, out var contentType)) continue;
            var route = name == "index.html" ? "/registration" : "/registration/" + name;
            endpoints.MapGet(route, (HttpContext context) =>
            {
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                return Results.File(path, contentType);
            }).AllowAnonymous();
        }
    }
    private sealed record BrowserContext(string Id, DateTimeOffset ExpiresAt);
}
