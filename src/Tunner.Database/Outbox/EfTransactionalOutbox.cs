namespace Tunner.Database.Outbox;

/// <summary>
/// Coordinates one owner-scoped write and one outbox record in a single database transaction.
/// </summary>
public sealed class EfTransactionalOutbox : ITransactionalOutbox
{
    public async Task PersistAsync<TContext>(
        TContext context,
        Func<TContext, CancellationToken, Task> writeOwnerStateAsync,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken = default)
        where TContext : class, ITransactionalOutboxContext
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writeOwnerStateAsync);
        ArgumentNullException.ThrowIfNull(outboxMessage);

        await using var transaction = await context
            .BeginOutboxTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await writeOwnerStateAsync(context, cancellationToken).ConfigureAwait(false);
            context.AddOutboxMessage(outboxMessage);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}