using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tunner.Database.Transactions;

/// <summary>A PostgreSQL session advisory lock on the exact held EF connection. Domain receipts and transactions remain caller-owned.</summary>
public sealed class PostgresCommandLease
{
    private readonly DatabaseFacade database;
    private readonly DbConnection connection;
    private readonly string reference;
    private readonly Action<DbConnection> invalidate;
    private bool acquired, released;
    private PostgresCommandLease(DatabaseFacade database, string reference, Action<DbConnection> invalidate)
    { this.database = database; connection = database.GetDbConnection(); this.reference = reference; this.invalidate = invalidate; }
    public static async Task<PostgresCommandLease> AcquireAsync(DatabaseFacade database, string reference, Action<DbConnection> invalidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database); ArgumentException.ThrowIfNullOrWhiteSpace(reference); ArgumentNullException.ThrowIfNull(invalidate);
        var lease = new PostgresCommandLease(database, reference, invalidate);
        if (database.CurrentTransaction is not null || lease.connection.State != ConnectionState.Closed)
            throw new InvalidOperationException("A command lease requires an unused database connection.");
        try
        {
            await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            _ = await lease.AdvisoryAsync(true, cancellationToken).ConfigureAwait(false); lease.acquired = true; return lease;
        }
        catch (Exception error) { await lease.ReleaseAsync(error).ConfigureAwait(false); throw; }
    }
    public async Task ReleaseAsync(Exception? originalFailure = null)
    {
        if (released) return; released = true;
        try
        {
            // Server acquisition may have succeeded immediately before client cancellation.
            if (connection.State == ConnectionState.Open)
            {
                var unlocked = await AdvisoryAsync(false, CancellationToken.None).ConfigureAwait(false);
                if (acquired && !Equals(unlocked, true)) throw new InvalidOperationException("The command lease could not be released.");
            }
        }
        catch when (originalFailure is not null) { invalidate(connection); }
        catch { invalidate(connection); throw; }
        finally
        {
            try { await database.CloseConnectionAsync().ConfigureAwait(false); }
            catch when (originalFailure is not null) { invalidate(connection); }
        }
    }
    private async Task<object?> AdvisoryAsync(bool acquire, CancellationToken ct)
    {
        await using var sql = connection.CreateCommand();
        sql.CommandText = acquire ? "SELECT pg_advisory_lock(hashtextextended(@reference,0))" : "SELECT pg_advisory_unlock(hashtextextended(@reference,0))";
        var parameter = sql.CreateParameter(); parameter.ParameterName = "reference"; parameter.Value = reference; sql.Parameters.Add(parameter);
        return await sql.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
