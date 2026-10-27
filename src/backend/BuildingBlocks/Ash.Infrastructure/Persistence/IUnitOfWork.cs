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
}
