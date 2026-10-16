using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Tunner.Platform.Realtime;

/// <summary>
/// A server-derived, opaque SignalR audience key. It is not a Product, Account,
/// resource, or client-supplied subscription identifier.
/// </summary>
public sealed record RealtimeAudience
{
    private const int MaximumGroupNameLength = 128;

    public RealtimeAudience(string groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        if (groupName.Length > MaximumGroupNameLength || groupName.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or ':')))
        {
            throw new ArgumentException("A realtime audience key must be a short opaque technical identifier.", nameof(groupName));
        }

        GroupName = groupName;
    }

    public string GroupName { get; }
}

/// <summary>
/// Supplies only audiences that the authenticated server-side Tunner scope has
/// authorized for a new browser connection. Implementations must not trust
/// arbitrary client input and must use the authoritative identity/policy source
/// for any resource authorization decision.
/// </summary>
public interface IRealtimeAudienceResolver
{
    Task<RealtimeAudienceResolution> ResolveAsync(RealtimeConnectionAuthorizationContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Server-only information made available to a scope resolver during connection
/// authorization. The browser cannot submit or modify these values through the hub.
/// </summary>
public sealed record RealtimeConnectionAuthorizationContext(
    ClaimsPrincipal Principal,
    IAuthorizationService AuthorizationService,
    string ConnectionId);

/// <summary>
/// The server-derived authorization outcome for one connection.
/// </summary>
public sealed class RealtimeAudienceResolution
{
    private RealtimeAudienceResolution(bool isAuthorized, IReadOnlyCollection<RealtimeAudience> audiences)
    {
        IsAuthorized = isAuthorized;
        Audiences = audiences;
    }

    public bool IsAuthorized { get; }

    public IReadOnlyCollection<RealtimeAudience> Audiences { get; }

    public static RealtimeAudienceResolution Deny() => new(false, []);

    public static RealtimeAudienceResolution Allow(IReadOnlyCollection<RealtimeAudience> audiences)
    {
        ArgumentNullException.ThrowIfNull(audiences);
        return new(true, audiences.Distinct().ToArray());
    }
}

/// <summary>
/// Secure baseline used until an identity/BFF owner provides authoritative scope resolution.
/// </summary>
public sealed class DenyAllRealtimeAudienceResolver : IRealtimeAudienceResolver
{
    public Task<RealtimeAudienceResolution> ResolveAsync(RealtimeConnectionAuthorizationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(RealtimeAudienceResolution.Deny());
    }
}