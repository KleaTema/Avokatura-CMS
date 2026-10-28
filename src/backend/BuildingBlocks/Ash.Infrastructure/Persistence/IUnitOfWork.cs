using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Persistence;

public interface IUnitOfWork
{
    MySqlConnection Connection { get; }
    bool HasActiveTransaction { get; }

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
}
