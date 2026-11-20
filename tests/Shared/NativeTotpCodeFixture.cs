using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Tunner.Identity.Persistence;

namespace Tunner.Testing.Totp;

/// <summary>Test-only native framework adapter, shared by tests and the private browser oracle.</summary>
internal static class NativeTotpCodeFixture
{
    // Explicit synthetic test-runtime admission; never selects a production policy.
    internal static string? IdentityInformationalVersion =>
        typeof(AuthenticatorTokenProvider<IdentityUserAdapter>).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    internal static string IdentityVersion => RequireIdentityVersion(IdentityInformationalVersion);

    internal static string RequireIdentityVersion(string? informationalVersion)
    {
        var separator = informationalVersion?.IndexOf('+') ?? -1;
        if (informationalVersion is null || separator <= 0 || separator == informationalVersion.Length - 1)
            throw new InvalidOperationException("The explicitly supported Identity test runtime is unavailable.");
        var version = informationalVersion[..separator];
        return version is "10.0.13" or "10.0.15" ? version :
            throw new InvalidOperationException("The explicitly supported Identity test runtime is unavailable.");
    }

    internal static string NativeCode(string seed, int counterOffset = 0)
        => NativeCode(seed, DateTimeOffset.UtcNow, counterOffset);

    internal static string NativeCode(string seed, DateTimeOffset instant, int counterOffset = 0)
    {
        var assembly = typeof(AuthenticatorTokenProvider<IdentityUserAdapter>).Assembly;
        var decoder = assembly.GetType("Microsoft.AspNetCore.Identity.Base32", throwOnError: true)!.GetMethod("FromBase32", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var bytes = (byte[])decoder.Invoke(null, [seed])!;
        try
        {
            var compute = assembly.GetType("Microsoft.AspNetCore.Identity.Rfc6238AuthenticationService", throwOnError: true)!.GetMethod("ComputeTotp", BindingFlags.Static | BindingFlags.NonPublic)!;
            return ((int)compute.Invoke(null, [bytes, (ulong)(instant.ToUnixTimeSeconds() / 30 + counterOffset), null])!).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
