using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Pure publication-bound intersection only; no policy selection, I/O, caching or defaults.</summary>
internal static class PublishedPresentationAvailability
{
    internal static EffectivePolicyInterval Intersect(params EffectivePolicyInterval[] intervals)
    {
        var from = intervals.Max(interval => interval.From); var includesFrom = intervals.Where(interval => interval.From == from).All(interval => interval.IncludesFrom);
        var ends = intervals.Where(interval => interval.Until is not null).ToArray();
        var until = ends.Length == 0 ? (DateTimeOffset?)null : ends.Min(interval => interval.Until!.Value);
        var includesUntil = until is not null && ends.Where(interval => interval.Until == until).All(interval => interval.IncludesUntil);
        return new(from, includesFrom, until, includesUntil);
    }
}
