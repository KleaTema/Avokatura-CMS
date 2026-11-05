using System.Globalization;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Chat.Application.Policy;
using Ash.Modules.Chat.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Chat.Application;

/// <summary>
/// The chat use cases (task 049). Reading and writing a conversation needs an ACTIVE participant row: a person removed
/// from the case (left_at) can no longer read it. Writes run in the chat's own transaction; events go to the hub only
/// after it commits.
/// </summary>
public sealed class ChatService(
    ChatDbContext db,
    ChatUnitOfWork chat,
    ChatParticipants participants,
    IChatPolicy policy,
    ICaseLookup cases,
    ICaseAccessService access,
    IUserDirectory users,
    ICurrentUser currentUser,
    MessageStore store,
    ChatPush push,
    ChatRateLimiter rateLimiter,
    IAuditLog audit)
{
    public const int DefaultPage = 50;
    public const int MaxPage = 100;
    public const int MaxBody = 4000;
    public const int PreviewLength = 80;
    public const int UnreadCap = 99;
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// One page of a conversation, newest first. The created_at bound lets MySQL prune the monthly partitions (only the
    /// months at or before the cursor are read); ix_msg_conv_time serves the filter and the order. Public so the
    /// integration test can EXPLAIN exactly this statement.
    /// </summary>
    public const string PageSql = """
        SELECT id, public_id, created_at, conversation_id, sender_user_id, message_type, body, reply_to_message_id, edited_at, deleted_at
          FROM messages
         WHERE conversation_id = @conversation
           AND (created_at, id) < (@before_at, @before_id)
           AND created_at <= @before_at
         ORDER BY created_at DESC, id DESC
         LIMIT @take
        """;

    private const string UnreadSql = """
        SELECT COUNT(*) FROM (
          SELECT 1 FROM messages
           WHERE conversation_id = @conversation AND id > @last AND deleted_at IS NULL
             AND (sender_user_id IS NULL OR sender_user_id <> @me)
           LIMIT 100) unread
        """;

    private static readonly Error NotFound = Error.NotFound("chat.not_found", "Biseda nuk u gjet.");
    private static readonly Error Left = Error.Forbidden("chat.not_participant", "Nuk jeni më pjesëmarrës në këtë bisedë.");
    private static readonly Error ReadOnly = Error.Conflict("chat.read_only", "Biseda është vetëm për lexim.");
    private static readonly Error MessageNotFound = Error.NotFound("chat.message_not_found", "Mesazhi nuk u gjet.");

    private uint Me => (uint)currentUser.UserId;

    // ------------------------------------------------------------------ conversations

    public async Task<ConversationPage> ListAsync(string? after, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 20, 1, 50);
        var me = Me;
        var mine = from p in db.Participants.AsNoTracking()
                   join c in db.Conversations.AsNoTracking() on p.ConversationId equals c.Id
                   where p.UserId == me && p.LeftAt == null
                   select new { c, p.LastReadMessageId, SortAt = c.LastMessageAt ?? c.CreatedAt };
        if (Cursor.TryDecode(after, out var sortKey, out var afterId)
            && long.TryParse(sortKey, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
        {
            var at = new DateTime(ticks, DateTimeKind.Utc);
            var id = (ulong)afterId;
            mine = mine.Where(x => x.SortAt < at || (x.SortAt == at && x.c.Id < id));
        }

        var rows = await mine.OrderByDescending(x => x.SortAt).ThenByDescending(x => x.c.Id).Take(take + 1).ToListAsync(ct);
        var hasMore = rows.Count > take;
        rows = rows.Take(take).ToList();

        var items = new List<ChatConversation>();
        foreach (var row in rows)
        {
            items.Add(await ToConversationAsync(row.c, row.LastReadMessageId, ct));
        }

        var next = hasMore
            ? Cursor.Encode(rows[^1].SortAt.Ticks.ToString(CultureInfo.InvariantCulture), (long)rows[^1].c.Id)
            : null;
        return new ConversationPage(items, next);
    }

    /// <summary>
    /// POST /cases/{caseId}/conversation: the case chat, created on first use (one per case: uq_conv_case), and its
    /// participants brought up to date. 404 without case.view, 403 for someone the case profile does not put in the
    /// chat (e.g. a Kryetar or Monitor not on the team).
    /// </summary>
    public async Task<Result<ChatConversation>> OpenCaseAsync(Guid casePublicId, CancellationToken ct)
    {
        var c = await cases.GetAsync(casePublicId, ct);
        if (c is null || !await access.CanViewAsync(c.Id, ct))
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet ose nuk keni akses.");
        }

        var members = await policy.CaseParticipantsAsync(c.Id, ct);
        if (!members.Contains(currentUser.UserId))
        {
            return Error.Forbidden("chat.not_allowed", "Nuk jeni pjesë e ekipit të kësaj çështjeje, ndaj nuk keni bisedë.");
        }

        var caseId = (ulong)c.Id;
        var conversation = await db.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ConversationType == ConversationRow.CaseType && x.CaseId == caseId, ct);
        if (conversation is null)
        {
            conversation = await CreateCaseConversationAsync(c, members, ct);
        }
        else
        {
            await participants.ReconcileAsync(c.Id, ct);
        }

        var lastRead = await db.Participants.AsNoTracking()
            .Where(p => p.ConversationId == conversation.Id && p.UserId == Me)
            .Select(p => p.LastReadMessageId)
            .SingleAsync(ct);
        conversation = await db.Conversations.AsNoTracking().SingleAsync(x => x.Id == conversation.Id, ct);
        return await ToConversationAsync(conversation, lastRead, ct);
    }

    private async Task<ConversationRow> CreateCaseConversationAsync(CaseSummary c, IReadOnlyList<long> members, CancellationToken ct)
    {
        var archived = await cases.IsDossierArchivedAsync(c.Id, ct);
        var now = store.Now();
        await chat.Work.BeginAsync(ct);
        try
        {
            var conversation = new ConversationRow
            {
                PublicId = Guid.NewGuid(),
                ConversationType = ConversationRow.CaseType,
                CaseId = (ulong)c.Id,
                IsReadOnly = archived,
                CreatedBy = Me,
                CreatedAt = now,
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct);
            db.Participants.AddRange(members.Select(u => new ParticipantRow
            {
                ConversationId = conversation.Id, UserId = (uint)u, JoinedAt = now,
            }));
            await db.SaveChangesAsync(ct);

            var publicIds = (await users.GetPublicIdsAsync(members, ct)).Values.ToList();
            chat.Work.OnCommittedAsync(() => push.JoinAsync(conversation.PublicId, publicIds));
            await chat.Work.CommitAsync(ct);
            return conversation;
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 })
        {
            // opened by someone else at the same moment: use theirs
            await chat.Work.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            await participants.ReconcileAsync(c.Id, ct);
            var caseId = (ulong)c.Id;
            return await db.Conversations.AsNoTracking()
                .SingleAsync(x => x.ConversationType == ConversationRow.CaseType && x.CaseId == caseId, ct);
        }
        catch
        {
            await chat.Work.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>POST /direct: only when the policy allows 1:1 chats — never under CaseOnly (the client's choice).</summary>
    public async Task<Result<ChatConversation>> OpenDirectAsync(OpenDirectRequest req, CancellationToken ct)
    {
        var other = await users.GetIdsAsync([req.UserId], ct);
        var otherId = other.TryGetValue(req.UserId, out var o) ? o : -1;
        if (!policy.CanStartDirect(currentUser.UserId, otherId))
        {
            return Error.Forbidden("chat.direct_not_allowed", "Biseda lejohet vetëm brenda çështjes.");
        }

        // TODO(client-q-chat): direct chats (OpenChatPolicy) are not built further while the client wants case-only chat
        return Error.Forbidden("chat.direct_not_allowed", "Biseda lejohet vetëm brenda çështjes.");
    }

    // ------------------------------------------------------------------ messages

    public async Task<Result<MessagePage>> MessagesAsync(Guid conversationId, string? before, int? limit, CancellationToken ct)
    {
        var found = await ActiveAsync(conversationId, ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var take = Math.Clamp(limit ?? DefaultPage, 1, MaxPage);
        var (beforeAt, beforeId) = DecodeMessageCursor(before) ?? (store.Now().AddSeconds(1), ulong.MaxValue);
        var rows = await db.Messages.FromSqlRaw(PageSql,
                new MySqlParameter("@conversation", found.Value.Conversation.Id),
                new MySqlParameter("@before_at", beforeAt),
                new MySqlParameter("@before_id", beforeId),
                new MySqlParameter("@take", take + 1))
            .AsNoTracking()
            .ToListAsync(ct);
        var hasMore = rows.Count > take;
        rows = rows.Take(take).ToList();

        var items = await ToMessagesAsync(found.Value.Conversation.Id, rows, ct);
        var next = hasMore ? EncodeMessageCursor(rows[^1]) : null;
        return new MessagePage(items, next);
    }

    public async Task<Result<ChatMessage>> SendAsync(Guid conversationId, SendMessageRequest req, CancellationToken ct)
    {
        var body = (req.Body ?? string.Empty).Trim();
        if (body.Length is 0 or > MaxBody)
        {
            return Error.Validation("chat.body_invalid", $"Mesazhi duhet të ketë 1 deri në {MaxBody} shenja.");
        }

        var found = await ActiveAsync(conversationId, ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var conversation = found.Value.Conversation;
        if (conversation.IsReadOnly)
        {
            return ReadOnly;
        }

        ulong? replyTo = null;
        if (req.ReplyToMessageId is { } replyPublicId)
        {
            replyTo = await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id && m.PublicId == replyPublicId)
                .Select(m => (ulong?)m.Id)
                .FirstOrDefaultAsync(ct);
            if (replyTo is null)
            {
                return Error.Validation("chat.reply_not_found", "Mesazhi që po i përgjigjeni nuk u gjet në këtë bisedë.");
            }
        }

        if (!rateLimiter.TryTake(currentUser.UserId))
        {
            return Error.TooManyRequests("chat.rate_limited",
                $"Keni dërguar shumë mesazhe ({ChatRateLimiter.PerMinute} në minutë). Provoni pas pak.");
        }

        MessageRow row;
        await chat.Work.BeginAsync(ct);
        try
        {
            row = await store.InsertAsync(conversation.Id, Me, MessageRow.Text, body, replyTo, store.Now(), ct);

            // the sender has read their own message
            await using (var read = chat.Work.CreateCommand("""
                UPDATE conversation_participants SET last_read_message_id = GREATEST(last_read_message_id, @id), last_read_at = @at
                 WHERE conversation_id = @conversation AND user_id = @me
                """))
            {
                read.Parameters.AddWithValue("@id", row.Id);
                read.Parameters.AddWithValue("@at", row.CreatedAt);
                read.Parameters.AddWithValue("@conversation", conversation.Id);
                read.Parameters.AddWithValue("@me", Me);
                await read.ExecuteNonQueryAsync(ct);
            }

            var message = (await ToMessagesAsync(conversation.Id, [row], ct))[0];
            chat.Work.OnCommitted(() => push.Send(conversation.PublicId, ChatHub.MessageNew, new MessagePushed(conversation.PublicId, message)));
            await chat.Work.CommitAsync(ct);
            return message;
        }
        catch
        {
            await chat.Work.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>PUT /messages/{id}: the sender only, within 15 minutes. TODO(client-q-chat-edit-window): 15 minutes is the default.</summary>
    public async Task<Result<ChatMessage>> EditAsync(Guid messageId, EditMessageRequest req, CancellationToken ct)
    {
        var body = (req.Body ?? string.Empty).Trim();
        if (body.Length is 0 or > MaxBody)
        {
            return Error.Validation("chat.body_invalid", $"Mesazhi duhet të ketë 1 deri në {MaxBody} shenja.");
        }

        var found = await OwnMessageAsync(messageId, ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (conversation, row) = found.Value;
        if (store.Now() - row.CreatedAt > EditWindow)
        {
            return Error.Conflict("chat.edit_window", "Mesazhi mund të ndryshohet vetëm brenda 15 minutave nga dërgimi.");
        }

        return await ChangeAsync(conversation, row, ChatHub.MessageEdited, async now =>
        {
            row.Body = body;
            row.EditedAt = now;
            await using var update = chat.Work.CreateCommand(
                "UPDATE messages SET body = @body, edited_at = @now WHERE id = @id AND created_at = @at");
            update.Parameters.AddWithValue("@body", body);
            update.Parameters.AddWithValue("@now", now);
            update.Parameters.AddWithValue("@id", row.Id);
            update.Parameters.AddWithValue("@at", row.CreatedAt);
            await update.ExecuteNonQueryAsync(ct);
        }, ct);
    }

    /// <summary>DELETE /messages/{id}: the sender only; soft (deleted_at) — others see "Mesazh i fshirë", the body stays.</summary>
    public async Task<Result<ChatMessage>> DeleteAsync(Guid messageId, CancellationToken ct)
    {
        var found = await OwnMessageAsync(messageId, ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (conversation, row) = found.Value;
        var result = await ChangeAsync(conversation, row, ChatHub.MessageDeleted, async now =>
        {
            row.DeletedAt = now;
            await using var update = chat.Work.CreateCommand(
                "UPDATE messages SET deleted_at = @now WHERE id = @id AND created_at = @at");
            update.Parameters.AddWithValue("@now", now);
            update.Parameters.AddWithValue("@id", row.Id);
            update.Parameters.AddWithValue("@at", row.CreatedAt);
            await update.ExecuteNonQueryAsync(ct);
        }, ct);
        if (result.IsSuccess)
        {
            audit.Record(AuditActions.ChatMessageDeleted, "CHAT_MESSAGE", (long)row.Id,
                conversation.CaseId is { } caseId ? (long)caseId : null,
                new { conversation = conversation.PublicId, message = row.PublicId });
        }

        return result;
    }

    /// <summary>POST /conversations/{id}/read: the pointer only moves forward (an older id changes nothing).</summary>
    public async Task<Result> MarkReadAsync(Guid conversationId, MarkReadRequest req, CancellationToken ct)
    {
        var found = await ActiveAsync(conversationId, ct);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error!);
        }

        var conversation = found.Value.Conversation;
        var messageId = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && m.PublicId == req.LastMessageId)
            .Select(m => (ulong?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (messageId is null)
        {
            return Result.Failure(MessageNotFound);
        }

        await chat.Work.BeginAsync(ct);
        try
        {
            await using var read = chat.Work.CreateCommand("""
                UPDATE conversation_participants SET last_read_message_id = @id, last_read_at = @now
                 WHERE conversation_id = @conversation AND user_id = @me AND last_read_message_id < @id
                """);
            read.Parameters.AddWithValue("@id", messageId.Value);
            read.Parameters.AddWithValue("@now", store.Now());
            read.Parameters.AddWithValue("@conversation", conversation.Id);
            read.Parameters.AddWithValue("@me", Me);
            var moved = await read.ExecuteNonQueryAsync(ct) > 0;
            if (moved)
            {
                var pushed = new ConversationReadPushed(conversation.PublicId, currentUser.PublicId, req.LastMessageId);
                chat.Work.OnCommitted(() => push.Send(conversation.PublicId, ChatHub.ConversationRead, pushed));
            }

            await chat.Work.CommitAsync(ct);
        }
        catch
        {
            await chat.Work.RollbackAsync(CancellationToken.None);
            throw;
        }

        return Result.Success();
    }

    /// <summary>GET /unread-total: the Chat menu badge — unread messages over my active conversations, capped at 99.</summary>
    public async Task<UnreadTotal> UnreadTotalAsync(CancellationToken ct)
    {
        var me = Me;
        var mine = await db.Participants.AsNoTracking()
            .Where(p => p.UserId == me && p.LeftAt == null)
            .Select(p => new { p.ConversationId, p.LastReadMessageId })
            .ToListAsync(ct);
        var total = 0;
        foreach (var p in mine)
        {
            total += await UnreadAsync(p.ConversationId, p.LastReadMessageId, ct);
            if (total > UnreadCap)
            {
                return new UnreadTotal(UnreadCap, true);
            }
        }

        return new UnreadTotal(total, false);
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Active(ConversationRow Conversation, ParticipantRow Participant);

    /// <summary>The conversation for the current user: 404 if it does not exist or they were never in it, 403 after leaving.</summary>
    private async Task<Result<Active>> ActiveAsync(Guid conversationId, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.PublicId == conversationId, ct);
        if (conversation is null)
        {
            return NotFound;
        }

        var me = Me;
        var participant = await db.Participants.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ConversationId == conversation.Id && p.UserId == me, ct);
        if (participant is null)
        {
            return NotFound;
        }

        return participant.LeftAt is null ? new Active(conversation, participant) : Left;
    }

    private async Task<Result<(ConversationRow Conversation, MessageRow Row)>> OwnMessageAsync(Guid messageId, CancellationToken ct)
    {
        var row = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.PublicId == messageId, ct);
        if (row is null)
        {
            return MessageNotFound;
        }

        var conversation = await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == row.ConversationId, ct);
        var found = await ActiveAsync(conversation.PublicId, ct);
        if (found.IsFailure)
        {
            // not a participant: the message is not theirs to know about
            return found.Error!.Type == Ash.SharedKernel.Results.ErrorType.NotFound ? MessageNotFound : found.Error!;
        }

        if (row.SenderUserId != Me || row.MessageType != MessageRow.Text)
        {
            return Error.Forbidden("chat.not_own_message", "Mund të ndryshoni ose fshini vetëm mesazhet tuaja.");
        }

        if (row.DeletedAt is not null)
        {
            return Error.Conflict("chat.message_deleted", "Mesazhi është fshirë.");
        }

        if (conversation.IsReadOnly)
        {
            return ReadOnly;
        }

        return (conversation, row);
    }

    private async Task<Result<ChatMessage>> ChangeAsync(
        ConversationRow conversation, MessageRow row, string method, Func<DateTime, Task> change, CancellationToken ct)
    {
        await chat.Work.BeginAsync(ct);
        try
        {
            await change(store.Now());
            var message = (await ToMessagesAsync(conversation.Id, [row], ct))[0];
            chat.Work.OnCommitted(() => push.Send(conversation.PublicId, method, new MessagePushed(conversation.PublicId, message)));
            await chat.Work.CommitAsync(ct);
            return message;
        }
        catch
        {
            await chat.Work.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<int> UnreadAsync(ulong conversationId, ulong lastRead, CancellationToken ct)
    {
        await using var command = new MySqlCommand(UnreadSql, chat.Work.Connection);
        if (chat.Work.Connection.State != System.Data.ConnectionState.Open)
        {
            await chat.Work.Connection.OpenAsync(ct);
        }

        command.Parameters.AddWithValue("@conversation", conversationId);
        command.Parameters.AddWithValue("@last", lastRead);
        command.Parameters.AddWithValue("@me", Me);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private async Task<ChatConversation> ToConversationAsync(ConversationRow c, ulong lastRead, CancellationToken ct)
    {
        var title = string.Empty;
        Guid? casePublicId = null;
        if (c.CaseId is { } caseId && await cases.GetAsync((long)caseId, ct) is { } summary)
        {
            title = string.IsNullOrEmpty(summary.CourtName) ? summary.CaseCode : $"{summary.CaseCode} · {summary.CourtName}";
            casePublicId = summary.PublicId;
        }

        string? preview = null;
        if (c.LastMessageId is { } lastId && c.LastMessageAt is { } lastAt)
        {
            var last = await db.Messages.AsNoTracking()
                .Where(m => m.Id == lastId && m.CreatedAt == lastAt)
                .Select(m => new { m.Body, m.DeletedAt })
                .FirstOrDefaultAsync(ct);
            if (last is not null)
            {
                var text = last.DeletedAt is null ? last.Body ?? string.Empty : ChatTexts.Deleted;
                preview = text.Length <= PreviewLength ? text : text[..(PreviewLength - 1)] + "…";
            }
        }

        var unread = await UnreadAsync(c.Id, lastRead, ct);
        return new ChatConversation(c.PublicId, c.ConversationType, casePublicId, title, c.IsReadOnly, c.LastMessageAt,
            preview, Math.Min(unread, UnreadCap), unread > UnreadCap);
    }

    private async Task<IReadOnlyList<ChatMessage>> ToMessagesAsync(ulong conversationId, IReadOnlyList<MessageRow> rows, CancellationToken ct)
    {
        var senders = rows.Where(r => r.SenderUserId is not null).Select(r => (long)r.SenderUserId!.Value).Distinct().ToList();
        var refs = senders.Count == 0 ? new Dictionary<long, UserRef>() : await users.GetRefsAsync(senders, ct);
        var replyIds = rows.Where(r => r.ReplyToMessageId is not null).Select(r => r.ReplyToMessageId!.Value).Distinct().ToList();
        var replies = replyIds.Count == 0
            ? new Dictionary<ulong, Guid>()
            : await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && replyIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.PublicId, ct);

        return rows.Select(r => new ChatMessage(
                r.PublicId,
                r.MessageType,
                r.SenderUserId is { } s && refs.TryGetValue(s, out var u) ? new ChatUser(u.PublicId, u.Name) : null,
                r.DeletedAt is null ? r.Body ?? string.Empty : ChatTexts.Deleted,
                r.DeletedAt is not null,
                r.ReplyToMessageId is { } rid && replies.TryGetValue(rid, out var rp) ? rp : null,
                r.CreatedAt,
                r.EditedAt))
            .ToList();
    }

    private static string EncodeMessageCursor(MessageRow row) =>
        Cursor.Encode(row.CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture), (long)row.Id);

    private static (DateTime, ulong)? DecodeMessageCursor(string? cursor) =>
        Cursor.TryDecode(cursor, out var key, out var id)
        && long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            ? (new DateTime(ticks, DateTimeKind.Utc), (ulong)id)
            : null;
}
