using System.Collections.Concurrent;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Chat.Application;

/// <summary>
/// At most <see cref="PerMinute"/> messages per user in any 60 seconds (task 049) — a sliding window in memory, per
/// API instance (with several instances a user could send that many on each: acceptable, it guards against a runaway
/// client, not an attacker with many tabs).
/// </summary>
public sealed class ChatRateLimiter(IClock clock)
{
    public const int PerMinute = 30;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<long, Queue<DateTime>> _sent = new();

    /// <summary>True (and counted) when the user may send one more now.</summary>
    public bool TryTake(long userId)
    {
        var now = clock.UtcNow;
        var queue = _sent.GetOrAdd(userId, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= Window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= PerMinute)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
