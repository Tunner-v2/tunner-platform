using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tunner.Identity.Persistence;

/// <summary>Optional private collection activation. Absence preserves the existing authentication composition.</summary>
internal sealed class DeviceBrowserService(DeviceApplication devices, IDeviceEvidencePolicySource policies,
    IDeviceSafeCharacteristicSource characteristics, AuthenticationPartitionKey partitions, IDataProtectionProvider protection,
    AuthenticationEndpoints.Surface surface, TimeProvider clock)
{
    internal const string CookieName = "__Host-Tunner.DeviceInstallation";
    private sealed record Installation(string Origin, string Token, string PolicyVersion, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
    private readonly IDataProtector protector = protection.CreateProtector("Tunner.User.Device.ObservedInstallation.v1");

    internal async Task ObserveAsync(HttpContext context, AuthenticationGrant grant, CancellationToken ct)
    {
        if (!context.Items.TryGetValue(AuthenticationEndpoints.GrantItem, out var item) || !ReferenceEquals(item, grant) ||
            context.Connection.RemoteIpAddress is null || !context.Request.IsHttps ||
            !StringComparer.OrdinalIgnoreCase.Equals(context.Request.Host.Value, new Uri(surface.Origin).Authority))
            throw new InvalidOperationException("Device collection requires the real same-origin Identity-issued grant.");
        var at = clock.GetUtcNow(); var policy = policies.RequireCurrent(surface.Origin, at); policy.Require(surface.Origin, at);
        Installation? installation = null;
        if (context.Request.Cookies.TryGetValue(CookieName, out var cookie))
        {
            try { installation = JsonSerializer.Deserialize<Installation>(protector.Unprotect(cookie)); }
            catch (CryptographicException) { /* Corrupt correlation is no authentication evidence. */ }
            catch (JsonException) { /* Unsupported correlation starts a fresh observed installation. */ }
        }
        var create = installation is null || installation.Origin != surface.Origin || installation.PolicyVersion != policy.Metadata.VersionId ||
            installation.Token is not { Length: 64 } || !installation.Token.All(Uri.IsHexDigit) ||
            installation.CreatedAt > at || installation.ExpiresAt <= at || installation.ExpiresAt > installation.CreatedAt + policy.CorrelationLifetime;
        if (create) installation = new(surface.Origin, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), policy.Metadata.VersionId,
            at, at + policy.CorrelationLifetime);
        var caller = StepUpCaller.FromPrincipal(grant.Ticket.Principal, surface.Origin, partitions.Network(context.Connection.RemoteIpAddress));
        var observed = characteristics.Observe(context, policy);
        if (!await devices.ObserveAuthenticatedAsync(caller, grant, installation!.Token, observed, ct).ConfigureAwait(false)) return;
        if (create)
            context.Response.Cookies.Append(CookieName, protector.Protect(JsonSerializer.Serialize(installation)), new CookieOptions
                { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, MaxAge = installation.ExpiresAt - at });
    }

    internal static IServiceCollection AddQualifiedEvidence(IServiceCollection services)
    {
        Type[] required = [typeof(IDeviceEvidencePolicySource), typeof(IDeviceSafeCharacteristicSource), typeof(IDeviceTrustEligibilitySource)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)))
            throw new InvalidOperationException("Device collection requires explicit policy, safe collection and trust eligibility authorities.");
        services.AddScoped<DeviceApplication>(); services.AddScoped<DeviceBrowserService>(); return services;
    }
}
