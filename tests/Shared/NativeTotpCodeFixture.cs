using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Tunner.Identity.Persistence;

namespace Tunner.Testing.Totp;

/// <summary>Test-only native framework adapter, shared by tests and the private browser oracle.</summary>
internal static class NativeTotpCodeFixture
{
    internal static string NativeCode(string seed, int counterOffset = 0)
    {
        var assembly = typeof(AuthenticatorTokenProvider<IdentityUserAdapter>).Assembly;
        var decoder = assembly.GetType("Microsoft.AspNetCore.Identity.Base32", throwOnError: true)!.GetMethod("FromBase32", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var bytes = (byte[])decoder.Invoke(null, [seed])!;
        try
        {
            var compute = assembly.GetType("Microsoft.AspNetCore.Identity.Rfc6238AuthenticationService", throwOnError: true)!.GetMethod("ComputeTotp", BindingFlags.Static | BindingFlags.NonPublic)!;
            return ((int)compute.Invoke(null, [bytes, (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + counterOffset), null])!).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
