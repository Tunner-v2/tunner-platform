using Tunner.Platform.Policies;

namespace Tunner.PresentationContent;

/// <summary>Explicit current Content authority, local only: no database/network I/O, blocking waits or stale cache.
/// Implementing the ordinary potentially-I/O profile port does not satisfy this native final-decision contract.</summary>
public interface IContentTransactionLocalProfileSource : IPresentationContentProfileSource
{
    ContentGovernanceProfile RequireCurrentLocal(string scope, DateTimeOffset effectiveAt);
}

/// <summary>Content-owned local witness for the exact profile selected by a transaction-bound read.</summary>
public sealed class ContentTransactionProfileWitness
{
    private readonly IContentTransactionLocalProfileSource source;
    private readonly string scope;
    private readonly string version;
    private readonly string digest;
    private readonly EffectivePolicyInterval availability;
    internal ContentTransactionProfileWitness(IContentTransactionLocalProfileSource source, ContentTransactionResolution resolution)
    { this.source = source; scope = resolution.Resolution.Scope; version = resolution.ProfileVersion; digest = resolution.ProfileDigest; availability = resolution.Availability; }
    public bool CurrentAt(DateTimeOffset effectiveAt)
    {
        try
        {
            var current = source.RequireCurrentLocal(scope, effectiveAt); current.Require(scope, effectiveAt);
            return availability.Contains(effectiveAt) && current.Metadata.VersionId == version && ContentDigest.Of(current) == digest;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return false; }
    }
    public override string ToString() => nameof(ContentTransactionProfileWitness);
}

public sealed partial class PresentationContentApplication
{
    /// <summary>Requires the same source that supplied the borrowed read to explicitly support authoritative local final checks.</summary>
    public ContentTransactionProfileWitness RequireLocalProfileWitness(ContentTransactionResolution resolution, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (profiles is not IContentTransactionLocalProfileSource local) throw new ContentUnavailableException();
        var witness = new ContentTransactionProfileWitness(local, resolution);
        if (!witness.CurrentAt(effectiveAt)) throw new ContentUnavailableException();
        return witness;
    }
}
