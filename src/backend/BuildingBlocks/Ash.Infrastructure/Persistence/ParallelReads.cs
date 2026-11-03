using Ash.SharedKernel.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Infrastructure.Persistence;

/// <summary>
/// The user of the request that started a <see cref="ParallelReads"/> scope. Scoped; empty in a request scope. The
/// module that registers <see cref="ICurrentUser"/> resolves it from here first, so a child scope sees the same user.
/// </summary>
public sealed class InheritedCurrentUser
{
    public ICurrentUser? User { get; set; }
}

/// <summary>
/// Runs a read beside the request's own queries (e.g. a list's COUNT next to its page query). One connection cannot
/// run two queries at once, so the read gets its own DI scope — with its own unit of work and connection, no
/// transaction — and the request's user. Use it for reads only: nothing written there is part of the request.
/// </summary>
public sealed class ParallelReads(IServiceScopeFactory scopes, ICurrentUser user)
{
    public async Task<TResult> RunAsync<TService, TResult>(
        Func<TService, CancellationToken, Task<TResult>> read, CancellationToken cancellationToken)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InheritedCurrentUser>().User = user;
        return await read(scope.ServiceProvider.GetRequiredService<TService>(), cancellationToken);
    }
}
