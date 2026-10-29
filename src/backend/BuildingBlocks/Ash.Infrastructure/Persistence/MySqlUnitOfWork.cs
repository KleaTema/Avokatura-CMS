using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Persistence;

public sealed class MySqlUnitOfWork : IUnitOfWork, IAsyncDisposable
{
    private readonly List<DbContext> _contexts = [];
    private readonly List<Action> _afterCommit = [];
    private MySqlTransaction? _transaction;

    public MySqlUnitOfWork(PersistenceOptions options)
    {
        Connection = new MySqlConnection(options.ConnectionString);
    }

    public MySqlConnection Connection { get; }

    public bool HasActiveTransaction => _transaction is not null;

    public MySqlCommand CreateCommand(string sql)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException(
                "No active transaction: raw commands must run inside the unit of work (a POST/PUT/PATCH/DELETE request).");
        }

        return new MySqlCommand(sql, Connection, _transaction);
    }

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

    public void OnCommitted(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_transaction is null)
        {
            // Nothing to wait for: the change is already durable.
            action();
            return;
        }

        _afterCommit.Add(action);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        await ReleaseTransactionAsync();
        RunAfterCommitActions();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _afterCommit.Clear();

        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await ReleaseTransactionAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _afterCommit.Clear();

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        await Connection.DisposeAsync();
    }

    private void RunAfterCommitActions()
    {
        var actions = _afterCommit.ToArray();
        _afterCommit.Clear();

        foreach (var action in actions)
        {
            try
            {
                action();
            }
            catch
            {
                // The data is already committed; a failing side effect (e.g. cache eviction)
                // must not turn a successful request into a 500. Worst case the cache
                // expires on its own (10 min).
            }
        }
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
