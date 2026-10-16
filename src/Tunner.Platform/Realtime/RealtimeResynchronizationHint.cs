namespace Tunner.Platform.Realtime;

/// <summary>
/// A payload-minimal non-authoritative instruction for an already authorized
/// Tunner browser to re-read affected state from its BFF/API/read model.
/// </summary>
public sealed record RealtimeResynchronizationHint;

/// <summary>
/// The only client callback offered by the reusable P1 realtime foundation.
/// Domain-specific callback categories belong to their owning later work item.
/// </summary>
public interface ITunnerRealtimeClient
{
    Task ResynchronizeAsync(RealtimeResynchronizationHint hint);
}

/// <summary>
/// Publishes the generic non-authoritative resynchronization hint to a
/// server-authorized audience. Callers must establish authorization through the
/// authoritative application/BFF path before requesting delivery.
/// </summary>
public interface IRealtimeHintPublisher
{
    Task PublishResynchronizationHintAsync(RealtimeAudience audience, CancellationToken cancellationToken = default);
}