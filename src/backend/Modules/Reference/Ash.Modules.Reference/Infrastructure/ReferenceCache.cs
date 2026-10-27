using Ash.Modules.Reference.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Ash.Modules.Reference.Infrastructure;

/// <summary>IMemoryCache wrapper with a 10 minute lifetime and one switch to drop every reference entry.</summary>
public sealed class ReferenceCache(IMemoryCache cache) : IReferenceCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly object _lock = new();
    private CancellationTokenSource _source = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> load, CancellationToken ct)
        where T : class
    {
        var cacheKey = "reference:" + key;
        if (cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        // Token taken before loading: an Invalidate() during the load expires this entry immediately.
        CancellationTokenSource source;
        lock (_lock)
        {
            source = _source;
        }

        var value = await load(ct);
        using var entry = cache.CreateEntry(cacheKey);
        entry.Value = value;
        entry.AbsoluteExpirationRelativeToNow = Lifetime;
        entry.AddExpirationToken(new CancellationChangeToken(source.Token));
        return value;
    }

    public void Invalidate()
    {
        CancellationTokenSource old;
        lock (_lock)
        {
            old = _source;
            _source = new CancellationTokenSource();
        }

        old.Cancel();
    }
}
