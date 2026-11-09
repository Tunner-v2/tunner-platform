using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Platform.Operations;
using Tunner.Accounts;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit opt-in User BFF routes. The shared BFF HTTPS/origin/CSRF middleware remains mandatory.</summary>
public static class RegistrationEndpoints
{
    public const string BrowserCookieName = "__Host-Tunner.Registration";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly string[] ScreenPaths = ["/registration", "/registration/verify-email", "/registration/personal-information", "/registration/consent", "/registration/complete"];

    public static IServiceCollection AddTunnerRegistrationApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(IRegistrationPolicySource), typeof(IRegistrationPasswordBlocklist),
            typeof(IRegistrationNotificationDispatcher), typeof(IRegistrationPresentationSource),
            typeof(RegistrationNetworkPartitionKey), typeof(IdentityPersistenceDbContext), typeof(IAccountAgePolicySource), typeof(IAccountConsentEvidenceSource)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)))
            throw new InvalidOperationException("Registration requires explicit policy, blocklist, notification, content, key and persistence providers.");
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IdentityWebAssets>();
        services.AddScoped<AccountPersonalInformationApplication>();
        services.AddScoped<IAccountCommercialEligibilityReader>(sp => sp.GetRequiredService<AccountPersonalInformationApplication>());
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
            var now = clock.GetUtcNow();
            if (!TryBrowser(context, protector, now, out var browser))
            {
                browser = new BrowserContext(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), now + policy.BrowserContextLifetime, Guid.NewGuid());
                WriteBrowser(context, protector, browser, now);
            }
            else if (browser!.RegistrationKey == Guid.Empty)
            {
                browser = browser with { RegistrationKey = Guid.NewGuid() }; WriteBrowser(context, protector, browser, now);
            }
            var csrf = antiforgery.GetAndStoreTokens(context);
            return Results.Json(new { csrfToken = csrf.RequestToken, registrationKey = browser.RegistrationKey, serverTime = clock.GetUtcNow(), policyVersion = policy.Version, policy.TermsVersion, policy.PrivacyVersion,
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
            catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        }).AllowAnonymous();
        endpoints.MapGet("/registration/progress", async (HttpContext context, IRegistrationApplication application,
            IDataProtectionProvider protection, RegistrationNetworkPartitionKey network, TimeProvider clock) =>
        {
            var protector = protection.CreateProtector("Tunner.User.Registration.Browser.v1");
            if (!TryBrowser(context, protector, clock.GetUtcNow(), out var browser) || context.Connection.RemoteIpAddress is null)
                return Problem(403, TechnicalProblemCodes.ForbiddenScope);
            if (browser!.RegistrationKey == Guid.Empty) return Problem(400, "NOT_FOUND_OR_NOT_DISCLOSABLE");
            var caller = new RegistrationCaller(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(browser.Id))), network.Partition(context.Connection.RemoteIpAddress));
            try { return ToHttp(await application.FindAsync(caller, browser.RegistrationKey, context.RequestAborted).ConfigureAwait(false)); }
            catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
            catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        }).AllowAnonymous();
        endpoints.MapPost("/registration/restart", RestartAsync).AllowAnonymous();
        endpoints.MapPost("/registration/register", (HttpContext context, IRegistrationApplication application) =>
            InvokeAsync<RegisterAccountCommand>(context, (caller, command, ct) => application.RegisterAsync(caller, command, ct))).AllowAnonymous();
        endpoints.MapPost("/registration/verify", (HttpContext context, IRegistrationApplication application) =>
            InvokeAsync<VerifyRegistrationCommand>(context, (caller, command, ct) => application.VerifyAsync(caller, command, ct))).AllowAnonymous();
        endpoints.MapPost("/registration/personal-information", (HttpContext context, IRegistrationApplication application) =>
            InvokeAsync<CompletePersonalInformationCommand>(context, (caller, command, ct) => application.CompletePersonalInformationAsync(caller, command, ct))).AllowAnonymous();
        endpoints.MapPost("/registration/continue", (HttpContext context, IRegistrationApplication application) =>
            InvokeAsync<ResendRegistrationCommand>(context, (caller, command, ct) => application.ContinueAsync(caller, command, ct))).AllowAnonymous();
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
        if (LimitBody(context, maximumBytes) is { } budgetFailure) return budgetFailure;
        T? command;
        try
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted).ConfigureAwait(false);
            if (body.Length > maximumBytes) return Problem(413, "VALIDATION_ERROR");
            command = JsonSerializer.Deserialize<T>(body.ToArray(), WebJson);
        }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException exception) { return Problem(exception.StatusCode == StatusCodes.Status413PayloadTooLarge ? 413 : 400, "VALIDATION_ERROR"); }
        if (command is null) return Problem(400, "VALIDATION_ERROR");
        RegistrationResult result;
        try { result = await action(caller, command, context.RequestAborted).ConfigureAwait(false); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        if (command is RegisterAccountCommand registration && result.ErrorCode is null && registration.IdempotencyKey != browser.RegistrationKey)
            WriteBrowser(context, protector, browser with { RegistrationKey = registration.IdempotencyKey }, now);
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
        return browser is not null && browser.ExpiresAt > now && browser.Id is { Length: 64 } && browser.Id.All(Uri.IsHexDigit);
    }
    private static IResult? LimitBody(HttpContext context, long maximumBytes)
    {
        if (context.Request.ContentLength > maximumBytes) return Problem(413, "VALIDATION_ERROR");
        var size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is null || size.IsReadOnly) return Problem(503, "DEPENDENCY_UNAVAILABLE");
        size.MaxRequestBodySize = maximumBytes; return null;
    }
    private static void WriteBrowser(HttpContext context, IDataProtector protector, BrowserContext browser, DateTimeOffset now) =>
        context.Response.Cookies.Append(BrowserCookieName, protector.Protect(JsonSerializer.Serialize(browser)), new CookieOptions
        { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = browser.ExpiresAt - now, IsEssential = true });
    private static async Task<IResult> RestartAsync(HttpContext context, IDataProtectionProvider protection, TimeProvider clock)
    {
        if (!context.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var now = clock.GetUtcNow(); var protector = protection.CreateProtector("Tunner.User.Registration.Browser.v1");
        if (!TryBrowser(context, protector, now, out var browser)) return Problem(403, TechnicalProblemCodes.ForbiddenScope);
        if (LimitBody(context, 256) is { } budgetFailure) return budgetFailure;
        try
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            if (body.RootElement.ValueKind != JsonValueKind.Object || body.RootElement.EnumerateObject().Any()) return Problem(400, "VALIDATION_ERROR");
        }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        var next = browser! with { RegistrationKey = Guid.NewGuid() }; WriteBrowser(context, protector, next, now);
        return Results.Json(new { registrationKey = next.RegistrationKey }); // Presentation pointer only; no Account, session or operation mutation.
    }
    private static void MapPackagedWebAssets(IEndpointRouteBuilder endpoints)
    {
        var assets = endpoints.ServiceProvider.GetRequiredService<IdentityWebAssets>();
        assets.MapAssets(endpoints);
        assets.MapScreens(endpoints, "index.html", ScreenPaths, static _ => true);
    }
    private sealed record BrowserContext(string Id, DateTimeOffset ExpiresAt, Guid RegistrationKey = default);
}
