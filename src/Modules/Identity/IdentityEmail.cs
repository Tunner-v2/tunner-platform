using System.Net.Mail;

namespace Tunner.Identity.Persistence;

/// <summary>Shared syntax check only; authoritative verified-contact ownership remains domain state.</summary>
internal static class IdentityEmail
{
    internal static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 256 &&
        MailAddress.TryCreate(value.Trim(), out var parsed) && StringComparer.Ordinal.Equals(parsed.Address, value.Trim());
}
