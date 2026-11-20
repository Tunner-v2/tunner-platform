using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Tunner.Identity.Persistence;

/// <summary>Existing protected registration-code mechanics, with owner-specific binding/purpose and explicitly supplied format.</summary>
internal static class IdentityContactChallenge
{
    internal static (string Code, string Digest) Create(IDataProtector protector, string binding, int digits, string? previous = null)
    {
        if (digits is < 6 or > 9 || string.IsNullOrWhiteSpace(binding)) throw new ArgumentException("An explicit supported challenge format/binding is required.");
        var bound = (int)Math.Pow(10, digits); string code;
        do { code = RandomNumberGenerator.GetInt32(bound).ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); }
        while (StringComparer.Ordinal.Equals(previous, code));
        return (protector.Protect(code), Convert.ToBase64String(protector.Protect(Digest(binding, code))));
    }
    internal static byte[] Digest(string binding, string code) => SHA256.HashData(Encoding.UTF8.GetBytes(binding + ":" + code));
    internal static bool Verify(IDataProtector protector, string binding, string? protectedDigest, string? code) => protectedDigest is not null &&
        CryptographicOperations.FixedTimeEquals(Digest(binding, code ?? string.Empty), protector.Unprotect(Convert.FromBase64String(protectedDigest)));
}
