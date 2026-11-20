namespace Tunner.Identity.Persistence;

/// <summary>PostgreSQL precision shared by protected selectors and persisted Identity metadata.</summary>
internal static class IdentityPersistenceTime
{
    internal static DateTimeOffset Normalize(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
}
