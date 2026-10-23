using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Tunner.Accounts;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Trusted owner policy. Numeric values are deliberately never defaulted.</summary>
public sealed record AuthenticationPolicy(string Version, string ProcessingReference, string RetentionReference,
    string PasswordAssurance, int PasswordMaximumLength, int PasswordHashIterations,
    TimeSpan AbsoluteLifetime, TimeSpan IdleLifetime, TimeSpan RememberAbsoluteLifetime,
    TimeSpan RememberIdleLifetime, TimeSpan BrowserContextLifetime, TimeSpan AbuseWindow,
    int NetworkAttemptLimit, int IdentifierAttemptLimit)
{
    public void Validate()
    {
        if (new[] { Version, ProcessingReference, RetentionReference, PasswordAssurance }.Any(string.IsNullOrWhiteSpace) ||
            PasswordMaximumLength < 64 || PasswordMaximumLength > 1_048_576 || PasswordHashIterations <= 0 ||
            new[] { AbsoluteLifetime, IdleLifetime, RememberAbsoluteLifetime, RememberIdleLifetime, BrowserContextLifetime, AbuseWindow }.Any(x => x <= TimeSpan.Zero) ||
            IdleLifetime > AbsoluteLifetime || RememberIdleLifetime > RememberAbsoluteLifetime ||
            NetworkAttemptLimit <= 0 || IdentifierAttemptLimit <= 0)
            throw new InvalidOperationException("Authentication requires a complete supported approved policy.");
    }
}
public interface IAuthenticationPolicySource { AuthenticationPolicy RequireEffective(DateTimeOffset effectiveAt); }
public sealed record AuthenticationPolicyVersion(PolicyVersionCandidate<string> Metadata, AuthenticationPolicy Values);
public sealed class AuthenticationPolicyCatalog : IAuthenticationPolicySource
{
    private readonly AuthenticationPolicyVersion[] versions;
    public AuthenticationPolicyCatalog(IEnumerable<AuthenticationPolicyVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions); this.versions = versions.ToArray();
        foreach (var version in this.versions)
        {
            version.Values.Validate();
            if (version.Metadata.VersionId != version.Values.Version) throw new ArgumentException("Policy identity mismatch.", nameof(versions));
        }
    }
    public AuthenticationPolicy RequireEffective(DateTimeOffset effectiveAt)
    {
        var selected = EffectivePolicyVersionResolver.Resolve(effectiveAt, "AUTHENTICATION", versions.Select(x => x.Metadata),
            static (a, b) => StringComparer.Ordinal.Equals(a, b), static x => x.Status == "ACTIVE").RequireResolvedVersion();
        return versions.Single(x => x.Values.Version == selected.VersionId).Values;
    }
}

public sealed record AccountAuthenticationAccessRequest(string AccountId, AccountState State, long StateVersion, string Operation, DateTimeOffset EffectiveAt);
public sealed record AccountAuthenticationAccessDecision(string AccountId, long StateVersion, string Operation, bool Allowed,
    string PolicyVersion, string EvidenceReference, EffectivePolicyInterval EffectiveInterval);
/// <summary>Trusted Account restriction authority, not browser claims or a substitute age gate.</summary>
public interface IAccountAuthenticationAccessSource
{
    ValueTask<AccountAuthenticationAccessDecision> RequireAsync(AccountAuthenticationAccessRequest request, CancellationToken cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PasswordSignInCommand
{
    public required Guid IdempotencyKey { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required bool RememberMe { get; init; }
    public override string ToString() => nameof(PasswordSignInCommand);
}

/// <summary>Only the trusted BFF constructs this from protected state and the real peer.</summary>
public sealed record AuthenticationCaller(string BrowserDigest, string NetworkDigest, string Origin, Guid? PreviousSessionId)
{
    public void Validate()
    {
        if (new[] { BrowserDigest, NetworkDigest }.Any(x => x.Length != 64 || !x.All(Uri.IsHexDigit)) ||
            !Uri.TryCreate(Origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Invalid trusted authentication context.");
    }
}

public sealed class AuthenticationPartitionKey : IDisposable
{
    private readonly byte[] key;
    public AuthenticationPartitionKey(ReadOnlySpan<byte> key)
    {
        if (key.Length < 32) throw new ArgumentException("Explicit strong authentication partition key required.", nameof(key));
        this.key = key.ToArray();
    }
    public string Network(IPAddress peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return Convert.ToHexString(HMACSHA256.HashData(key, peer.MapToIPv6().GetAddressBytes()));
    }
    internal string Identifier(string normalized) => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(normalized)));
    public void Dispose() => CryptographicOperations.ZeroMemory(key);
    public override string ToString() => nameof(AuthenticationPartitionKey);
}

/// <summary>Internal transport only. Never serialize bearer material into an HTTP response.</summary>
internal sealed class AuthenticationGrant(string key, AuthenticationTicket ticket)
{
    internal string Key { get; } = key;
    internal AuthenticationTicket Ticket { get; } = ticket;
    public override string ToString() => nameof(AuthenticationGrant);
}
internal sealed record AuthenticationAttempt(string? ErrorCode, AuthenticationGrant? Grant);

internal static class AuthenticationIdentity
{
    internal const string Scheme = "Tunner.User.Bff.Session";
    internal const string CookieName = "__Host-Tunner.User.Session";
    internal const string SessionClaim = "tunner:session";
    internal const string OriginClaim = "tunner:origin";
    internal static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static Guid? SessionId(ClaimsPrincipal principal) => Guid.TryParse(principal.FindFirstValue(SessionClaim), out var id) ? id : null;
}

/// <summary>Trusted worker/operator entry, never an HTTP endpoint. Expiry clears session bearer material; metadata retention remains owner-policy governed.</summary>
public interface IAuthenticationSessionMaintenance
{
    Task<int> ExpireSessionsAsync(CancellationToken cancellationToken = default);
    Task<int> PurgeExpiredAbuseAsync(CancellationToken cancellationToken = default);
}
