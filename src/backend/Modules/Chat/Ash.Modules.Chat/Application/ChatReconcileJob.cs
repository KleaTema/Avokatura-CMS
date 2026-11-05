using Ash.Infrastructure.Jobs;
using Ash.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Chat.Application;

/// <summary>
/// Nightly (01:30 Tirane, task 049): every case chat's participants recomputed from the cases — repairs whatever an
/// event missed (a reconcile that failed after the core commit, a user deactivated, an institution's users changed).
/// Each case in its own chat transaction; idempotent.
/// </summary>
internal sealed class ChatReconcileJob(ChatDbContext db, ChatParticipants participants, ILogger<ChatReconcileJob> logger) : IScheduledJob
{
    private const int Batch = 500;

    public string Name => "chat-reconcile";

    public JobSchedule Schedule => JobSchedule.DailyAtTirane(1, 30);

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        ulong after = 0;
        var changed = 0;
        while (true)
        {
            var page = await db.Conversations.AsNoTracking()
                .Where(c => c.ConversationType == ConversationRow.CaseType && c.Id > after)
                .OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.CaseId })
                .Take(Batch)
                .ToListAsync(ct);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var c in page)
            {
                if (await participants.ReconcileAsync((long)c.CaseId!.Value, ct))
                {
                    changed++;
                }

                db.ChangeTracker.Clear();
            }

            after = page[^1].Id;
        }

        logger.LogInformation("Chat reconcile: {Changed} case chats corrected.", changed);
    }
}
