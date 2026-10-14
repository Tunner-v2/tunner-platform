using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Platform.Operations;

namespace Tunner.Platform.Bff;

/// <summary>Shared technical cookie/origin/CSRF mechanics; Identity and workforce authority remain with their owners.</summary>
public static class TunnerBffFoundation
{
    public const string AntiforgeryHeader = "X-Tunner-CSRF";

    public static IServiceCollection AddTunnerBffFoundation(this IServiceCollection services, BffSurface surface, string? browserOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(BffBoundaryProfile)))
            throw new InvalidOperationException("A BFF host must register exactly one browser surface.");
        var profile = BffBoundaryProfile.Create(surface, browserOrigin);
        services.AddSingleton(profile);
        services.AddDataProtection().SetApplicationName(profile.ApplicationName);
        services.TryAddSingleton<ITicketStore, UnavailableBffTicketStore>();
        services.AddHealthChecks();
        services.AddAuthentication(profile.Scheme).AddCookie(profile.Scheme, options =>
        {
            options.Cookie.Name = profile.CookieName;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.Zero; // No implicit owner session lifetime.
            options.Events.OnSigningIn = _ => throw new InvalidOperationException("Identity-owned browser sign-in is not activated.");
            options.Events.OnValidatePrincipal = context =>
            {
                var now = (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow();
                if (context.Properties.IssuedUtc is not { } issued || context.Properties.ExpiresUtc is not { } expires ||
                    issued > now || expires <= now || expires <= issued || context.Principal is null ||
                    !context.Principal.Identities.Any(identity => identity.IsAuthenticated) ||
                    context.Principal.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType != profile.Scheme))
                    context.RejectPrincipal();
                context.ShouldRenew = false;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToLogin = context => WriteProblem(context.HttpContext, StatusCodes.Status401Unauthorized, TechnicalProblemCodes.AuthenticationRequired);
            options.Events.OnRedirectToAccessDenied = context => WriteProblem(context.HttpContext, StatusCodes.Status403Forbidden, TechnicalProblemCodes.ForbiddenScope);
        });
        services.AddOptions<CookieAuthenticationOptions>(profile.Scheme)
            .Configure<ITicketStore>((options, store) => options.SessionStore = store)
            .Validate(options => options.SessionStore is not null && options.Cookie.Name == profile.CookieName &&
                options.Cookie.HttpOnly && options.Cookie.SecurePolicy == CookieSecurePolicy.Always && options.Cookie.SameSite == SameSiteMode.Lax &&
                options.Cookie.Path == "/" && options.Cookie.Domain is null && !options.SlidingExpiration && options.ExpireTimeSpan == TimeSpan.Zero,
                "The BFF cookie security boundary cannot be weakened by configuration.")
            .ValidateOnStart();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeader;
            options.Cookie.Name = profile.AntiforgeryCookieName;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddOptions<AntiforgeryOptions>()
            .Validate(options => options.HeaderName == AntiforgeryHeader && options.Cookie.Name == profile.AntiforgeryCookieName &&
                options.Cookie.Path == "/" && options.Cookie.Domain is null && options.Cookie.HttpOnly &&
                options.Cookie.SecurePolicy == CookieSecurePolicy.Always && options.Cookie.SameSite == SameSiteMode.Strict,
                "The BFF antiforgery boundary cannot be weakened by configuration.")
            .ValidateOnStart();
        services.AddAuthorization(options =>
        {
            var policy = new AuthorizationPolicyBuilder(profile.Scheme).RequireAuthenticatedUser().Build();
            options.DefaultPolicy = policy;
            options.FallbackPolicy = policy;
        });
        return services;
    }

    public static IApplicationBuilder UseTunnerBffFoundation(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var profile = application.ApplicationServices.GetRequiredService<BffBoundaryProfile>();
        application.UseExceptionHandler(error => error.Run(context => WriteProblem(context, StatusCodes.Status500InternalServerError, "DEPENDENCY_UNAVAILABLE")));
        application.UseRouting();
        application.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (!context.Request.IsHttps)
            {
                await WriteProblem(context, StatusCodes.Status400BadRequest, "VALIDATION_ERROR").ConfigureAwait(false);
                return;
            }
            var safe = IsSafe(context.Request.Method);
            var health = context.GetEndpoint()?.Metadata.GetMetadata<BffHealthMetadata>() is not null &&
                (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method));
            if (!health)
            {
                if (profile.BrowserOrigin is null)
                {
                    await WriteProblem(context, StatusCodes.Status503ServiceUnavailable, "DEPENDENCY_UNAVAILABLE").ConfigureAwait(false);
                    return;
                }
                if (!BffBoundaryProfile.TryOrigin($"{context.Request.Scheme}://{context.Request.Host.ToUriComponent()}", out var target) || !profile.Matches(target))
                {
                    await WriteProblem(context, StatusCodes.Status403Forbidden, TechnicalProblemCodes.ForbiddenScope).ConfigureAwait(false);
                    return;
                }
                var origins = context.Request.Headers.Origin;
                if ((!safe || origins.Count != 0) && (origins.Count != 1 || !BffBoundaryProfile.TryOrigin(origins[0]!, out var origin) || !profile.Matches(origin)))
                {
                    await WriteProblem(context, StatusCodes.Status403Forbidden, TechnicalProblemCodes.ForbiddenScope).ConfigureAwait(false);
                    return;
                }
            }
            await next(context).ConfigureAwait(false);
        });
        application.UseAuthentication();
        application.UseAuthorization();
        application.Use(async (context, next) =>
        {
            if (!IsSafe(context.Request.Method))
            {
                var values = context.Request.Headers[AntiforgeryHeader];
                if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
                {
                    await WriteProblem(context, StatusCodes.Status400BadRequest, "VALIDATION_ERROR").ConfigureAwait(false);
                    return;
                }
                try
                {
                    await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context).ConfigureAwait(false);
                }
                catch (AntiforgeryValidationException)
                {
                    await WriteProblem(context, StatusCodes.Status400BadRequest, "VALIDATION_ERROR").ConfigureAwait(false);
                    return;
                }
            }
            await next(context).ConfigureAwait(false);
        });
        return application;
    }

    public static IEndpointConventionBuilder MapTunnerBffHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapHealthChecks("/health").AllowAnonymous().WithMetadata(new BffHealthMetadata());
    }

    private static bool IsSafe(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static Task WriteProblem(HttpContext context, int status, string code)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        var metadata = new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED");
        return TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.", metadata).ExecuteAsync(context);
    }

    private sealed class BffHealthMetadata;
}
