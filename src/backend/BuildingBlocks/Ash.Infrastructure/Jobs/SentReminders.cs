using Ash.Infrastructure.Persistence;

namespace Ash.Infrastructure.Jobs;

/// <summary>
/// ash_core.sent_reminders (script 016): one row per reminder ever sent, by a key the job builds
/// ("hearing:&lt;id&gt;:&lt;date&gt;:tomorrow", "pending:&lt;historyId&gt;:3d", "partition:&lt;date&gt;"). Marked in the
/// SAME transaction as the notifications it sends: a rollback forgets both, a second run finds the key and sends
/// nothing.
/// </summary>
public sealed class SentReminders(IUnitOfWork unitOfWork)
{
    public const int MaxKeyLength = 120;

    /// <summary>True when the key is new (send now); false when it was already sent.</summary>
    public async Task<bool> TryMarkAsync(string key, DateTime nowUtc, CancellationToken ct)
    {
        if (key.Length > MaxKeyLength)
        {
            throw new ArgumentException($"A reminder key has at most {MaxKeyLength} characters.", nameof(key));
        }

        // IGNORE turns only the duplicate (uq_sr_key) into "0 rows": the key length is checked above
        await using var command = unitOfWork.CreateCommand(
            "INSERT IGNORE INTO sent_reminders (dedupe_key, sent_at) VALUES (@key, @at)");
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@at", nowUtc);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }
}
