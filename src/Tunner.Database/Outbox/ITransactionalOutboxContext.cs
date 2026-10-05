using Microsoft.EntityFrameworkCore.Storage;

namespace Tunner.Database.Outbox;

/// <summary>
/// The deliberately narrow persistence capability used by the transactional-outbox coordinator.
/// </summary>
/// <remarks>
/// Module-specific contexts implement this contract with only their owned state and the shared outbox mapping.
/// It prevents the coordinator from exposing a general EF Core context as a cross-module mutation surface.
/// </remarks>
public interface ITransactionalOutboxContext
{
    Task<IDbContextTransaction> BeginOutboxTransactionAsync(CancellationToken cancellationToken = default);

    void AddOutboxMessage(OutboxMessage message);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}