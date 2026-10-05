namespace Tunner.Database.Outbox;

/// <summary>
/// Commits an owner write and its outbox record as one PostgreSQL transaction.
/// </summary>
public interface ITransactionalOutbox
{
    /// <summary>
    /// Stages owner state through that owner's narrow persistence context, then stages the shared outbox record.
    /// </summary>
    /// <remarks>
    /// The generic type is intentional: a caller can see only the tables mapped by its own context plus the shared
    /// outbox mapping. This shared primitive does not expose a general <c>DbContext</c> or foreign-module tables.
    /// </remarks>
    Task PersistAsync<TContext>(
        TContext context,
        Func<TContext, CancellationToken, Task> writeOwnerStateAsync,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken = default)
        where TContext : class, ITransactionalOutboxContext;
}