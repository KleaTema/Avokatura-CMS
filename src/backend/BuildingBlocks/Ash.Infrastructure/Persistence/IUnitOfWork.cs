using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Persistence;

public interface IUnitOfWork
{
    MySqlConnection Connection { get; }

    /// <summary>The connection string and the read-query time limit (module DbContexts configure themselves from it).</summary>
    PersistenceOptions Options { get; }

    bool HasActiveTransaction { get; }

    /// <summary>
    /// Raw SQL (e.g. a stored procedure) inside the request: the command is bound to the unit of work's connection
    /// AND transaction, so it commits or rolls back with everything else. Throws when no transaction is active.
    /// </summary>
    MySqlCommand CreateCommand(string sql);

    Task BeginAsync(CancellationToken cancellationToken = default);
    Task EnlistAsync(DbContext context, CancellationToken cancellationToken = default);
    void Enlist(DbContext context);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="action"/> only after the current transaction has committed
    /// (e.g. cache invalidation). Discarded on rollback. With no active transaction it runs immediately.
    /// </summary>
    void OnCommitted(Action action);

    /// <summary>
    /// Like <see cref="OnCommitted(Action)"/> for async work that must be done before the response (task 049: the chat
    /// participants follow a committed assignment). Awaited in order after the commit; a failure is swallowed (the data
    /// is committed) — the caller logs it and has its own repair (the chat's nightly reconcile).
    /// </summary>
    void OnCommittedAsync(Func<Task> action);
}
