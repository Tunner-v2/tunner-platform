namespace Tunner.Identity.Persistence;

internal sealed partial class AuthenticationApplication
{
    // All known session identities must be acquired before any Account lock. Use the same
    // representation as the existing advisory-lock namespace, independently of Guid byte order.
    internal async Task LockSessionsCanonicalAsync(IEnumerable<Guid> sessionIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        foreach (var id in sessionIds.Distinct().OrderBy(x => x.ToString("N"), StringComparer.Ordinal))
        {
            if (id == Guid.Empty) throw new ArgumentException("A session lock requires an exact identity.", nameof(sessionIds));
            await LockSessionAsync(id, ct).ConfigureAwait(false);
        }
    }
}
