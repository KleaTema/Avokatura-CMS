using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Persistence;

public sealed class MySqlUnitOfWork : IUnitOfWork, IAsyncDisposable
{
    private readonly List<DbContext> _contexts = [];
    private MySqlTransaction? _transaction;

    public MySqlUnitOfWork(PersistenceOptions options)
    {
        Connection = new MySqlConnection(options.ConnectionString);
    }

    public MySqlConnection Connection { get; }

    public bool HasActiveTransaction => _transaction is not null;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        await Connection.OpenAsync(cancellationToken);
        _transaction = await Connection.BeginTransactionAsync(cancellationToken);

        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(_transaction, cancellationToken);
        }
    }

    public async Task EnlistAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        if (_contexts.Contains(context))
        {
            return;
        }

        _contexts.Add(context);
        if (_transaction is not null)
        {
            await context.Database.UseTransactionAsync(_transaction, cancellationToken);
        }
    }

    public void Enlist(DbContext context)
    {
        if (_contexts.Contains(context))
        {
            return;
        }

        _contexts.Add(context);
        if (_transaction is not null)
        {
            context.Database.UseTransaction(_transaction);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        await ReleaseTransactionAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await ReleaseTransactionAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        await Connection.DisposeAsync();
    }

    private async Task ReleaseTransactionAsync()
    {
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(null);
        }

        await _transaction!.DisposeAsync();
        _transaction = null;
    }
}
