using Tunner.Database.Outbox;

namespace Tunner.Worker.Messaging;

/// <summary>Publishes a committed outbox projection and completes only after broker confirmation.</summary>
public interface IConfirmedOutboxPublisher
{
    Task PublishConfirmedAsync(PendingOutboxMessage message, CancellationToken cancellationToken = default);
}