namespace Tunner.Identity.Persistence;

/// <summary>Trusted application read budget, never a browser policy or production default.</summary>
internal sealed class SessionListingReadWindow
{
    internal SessionListingReadWindow(int maximumRows, SessionListingContinuation? after = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        ArgumentOutOfRangeException.ThrowIfEqual(maximumRows, int.MaxValue);
        MaximumRows = maximumRows; After = after;
    }
    internal int MaximumRows { get; }
    internal SessionListingContinuation? After { get; }
    public override string ToString() => nameof(SessionListingReadWindow);
}

/// <summary>Internal keyset boundary bound to the original actor; not an HTTP cursor.</summary>
internal sealed record SessionListingContinuation(string AccountId, Guid ActorSessionId, string Origin,
    DateTimeOffset AuthenticatedAt, Guid SessionId)
{
    public override string ToString() => nameof(SessionListingContinuation);
}

internal enum SessionListingState { Active, Revoked, Expired }
internal sealed record SessionListingItem(Guid SessionReference, long Version, bool IsCurrent,
    DateTimeOffset AuthenticatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt,
    SessionListingState RecordedState, SessionListingState EffectiveState, DateTimeOffset EvaluatedAt)
{
    public override string ToString() => nameof(SessionListingItem);
}
internal sealed record SessionListingPage(IReadOnlyList<SessionListingItem> Items, bool HasMore,
    SessionListingContinuation? Next)
{
    public override string ToString() => nameof(SessionListingPage);
}
