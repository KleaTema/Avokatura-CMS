using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Chat.Application.Policy;
using Ash.Modules.Chat.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Chat.Application;

/// <summary>
/// Keeps a case chat's participants equal to <see cref="IChatPolicy.CaseParticipantsAsync"/> (task 049). Called after
/// a CORE commit (the case events), when a case chat is opened, and nightly (ChatReconcileJob). Idempotent: it only
/// writes differences. A newcomer joins (or rejoins: left_at cleared) with a system message "… u shtua në bisedë"; a
/// person no longer on the case gets left_at (no reading, no writing — consistent with task 024) and "… doli nga
/// biseda". An archived dossier makes the chat read-only. Nothing happens for a case whose chat was never opened: its
/// first opening adds everyone at once, without system messages.
/// TODO(client-q-chat-history-after-removal): a removed person cannot read the chat any more (default).
/// </summary>
public sealed class ChatParticipants(
    ChatDbContext db,
    ChatUnitOfWork chat,
    IChatPolicy policy,
    ICaseLookup cases,
    IUserDirectory users,
    MessageStore messages,
    ChatPush push,
    IAuditLog audit,
    ILogger<ChatParticipants> logger)
{
    /// <summary>True when something changed.</summary>
    public async Task<bool> ReconcileAsync(long caseId, CancellationToken ct)
    {
        var id = (ulong)caseId;
        var conversationId = await db.Conversations.AsNoTracking()
            .Where(c => c.ConversationType == ConversationRow.CaseType && c.CaseId == id)
            .Select(c => (ulong?)c.Id)
            .SingleOrDefaultAsync(ct);
        if (conversationId is null)
        {
            return false;
        }

        // the facts come from the core database (committed data), read before the chat transaction starts
        var desired = (await policy.CaseParticipantsAsync(caseId, ct)).Select(u => (uint)u).ToHashSet();
        var archived = await cases.IsDossierArchivedAsync(caseId, ct);

        var joined = new List<uint>();
        var left = new List<uint>();
        var systemMessages = new List<MessageRow>();
        ConversationRow conversation;
        await chat.Work.BeginAsync(ct);
        try
        {
            // one reconcile at a time per conversation (two events of one request, or the job beside a request)
            await using (var lockRow = chat.Work.CreateCommand("SELECT id FROM conversations WHERE id = @id FOR UPDATE"))
            {
                lockRow.Parameters.AddWithValue("@id", conversationId.Value);
                await lockRow.ExecuteScalarAsync(ct);
            }

            conversation = await db.Conversations.SingleAsync(c => c.Id == conversationId.Value, ct);
            var rows = await db.Participants.Where(p => p.ConversationId == conversation.Id).ToListAsync(ct);
            var now = messages.Now();

            foreach (var userId in desired)
            {
                var row = rows.FirstOrDefault(r => r.UserId == userId);
                if (row is null)
                {
                    db.Participants.Add(new ParticipantRow { ConversationId = conversation.Id, UserId = userId, JoinedAt = now });
                    joined.Add(userId);
                }
                else if (row.LeftAt is not null)
                {
                    row.LeftAt = null;
                    row.JoinedAt = now;
                    joined.Add(userId);
                }
            }

            foreach (var row in rows.Where(r => r.LeftAt is null && !desired.Contains(r.UserId)))
            {
                row.LeftAt = now;
                left.Add(row.UserId);
            }

            var readOnlyChanged = archived && !conversation.IsReadOnly;
            if (readOnlyChanged)
            {
                conversation.IsReadOnly = true;
            }

            if (joined.Count == 0 && left.Count == 0 && !readOnlyChanged)
            {
                await chat.Work.RollbackAsync(CancellationToken.None);
                return false;
            }

            await db.SaveChangesAsync(ct);

            var names = await users.GetRefsAsync(joined.Concat(left).Select(u => (long)u).ToList(), ct);
            string Name(uint u) => names.TryGetValue(u, out var r) ? r.Name : "?";
            foreach (var userId in joined)
            {
                systemMessages.Add(await messages.InsertAsync(conversation.Id, null, MessageRow.System, ChatTexts.Joined(Name(userId)), null, now, ct));
            }

            foreach (var userId in left)
            {
                systemMessages.Add(await messages.InsertAsync(conversation.Id, null, MessageRow.System, ChatTexts.Left(Name(userId)), null, now, ct));
            }

            var publicIds = await users.GetPublicIdsAsync(joined.Concat(left).Select(u => (long)u).ToList(), ct);
            var conversationPublicId = conversation.PublicId;
            var joinedPublic = joined.Where(u => publicIds.ContainsKey(u)).Select(u => publicIds[u]).ToList();
            var leftPublic = left.Where(u => publicIds.ContainsKey(u)).Select(u => publicIds[u]).ToList();
            var pushed = systemMessages
                .Select(m => new MessagePushed(conversationPublicId, new ChatMessage(m.PublicId, m.MessageType, null, m.Body!, false, null, m.CreatedAt, null)))
                .ToList();

            // after the chat commit: leavers out of the group FIRST (they must not hear their own "doli"), newcomers in
            chat.Work.OnCommittedAsync(async () =>
            {
                await push.LeaveAsync(conversationPublicId, leftPublic);
                await push.JoinAsync(conversationPublicId, joinedPublic);
                foreach (var message in pushed)
                {
                    push.Send(conversationPublicId, ChatHub.MessageNew, message);
                }

                push.Send(conversationPublicId, ChatHub.ParticipantsChanged, new ParticipantsChangedPushed(conversationPublicId));
            });
            await chat.Work.CommitAsync(ct);
        }
        catch
        {
            await chat.Work.RollbackAsync(CancellationToken.None);
            throw;
        }

        foreach (var userId in joined)
        {
            audit.Record(AuditActions.ChatParticipantAdded, "CHAT_CONVERSATION", (long)conversation.Id, caseId,
                new { user = userId });
        }

        foreach (var userId in left)
        {
            audit.Record(AuditActions.ChatParticipantRemoved, "CHAT_CONVERSATION", (long)conversation.Id, caseId,
                new { user = userId });
        }

        logger.LogInformation("Case chat of case {CaseId}: {Joined} joined, {Left} left, read-only {ReadOnly}.",
            caseId, joined.Count, left.Count, conversation.IsReadOnly);
        return true;
    }

    /// <summary>
    /// For a case event (after the core commit): reconcile, never throwing — a failure is logged and the nightly job
    /// repairs it.
    /// </summary>
    public async Task ReconcileQuietlyAsync(long caseId)
    {
        try
        {
            await ReconcileAsync(caseId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Case chat participants of case {CaseId} could not be updated; the nightly reconcile retries.", caseId);
        }
    }
}
