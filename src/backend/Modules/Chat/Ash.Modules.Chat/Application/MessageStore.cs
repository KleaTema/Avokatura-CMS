using Ash.Modules.Chat.Infrastructure;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Chat.Application;

/// <summary>
/// Inserts messages with plain SQL inside the chat transaction: messages has a composite key (id, created_at) on a
/// partitioned table, so the generated id is read back with LAST_INSERT_ID() rather than through EF. Also moves the
/// conversation's last_message_* (the list is sorted on it, never on messages).
/// </summary>
public sealed class MessageStore(ChatUnitOfWork chat, IClock clock)
{
    /// <summary>UTC now in milliseconds, as DATETIME(3) keeps it (the pushed and the stored value are equal).</summary>
    public DateTime Now()
    {
        var now = clock.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    public async Task<MessageRow> InsertAsync(
        ulong conversationId, uint? senderUserId, string type, string body, ulong? replyTo, DateTime at, CancellationToken ct)
    {
        var publicId = Guid.NewGuid();
        await using (var insert = chat.Work.CreateCommand("""
            INSERT INTO messages (public_id, created_at, conversation_id, sender_user_id, message_type, body, reply_to_message_id)
            VALUES (@public_id, @at, @conversation, @sender, @type, @body, @reply);
            SELECT LAST_INSERT_ID();
            """))
        {
            insert.Parameters.AddWithValue("@public_id", PublicId.ToBytes(publicId));
            insert.Parameters.AddWithValue("@at", at);
            insert.Parameters.AddWithValue("@conversation", conversationId);
            insert.Parameters.AddWithValue("@sender", (object?)senderUserId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@type", type);
            insert.Parameters.AddWithValue("@body", body);
            insert.Parameters.AddWithValue("@reply", (object?)replyTo ?? DBNull.Value);
            var id = Convert.ToUInt64(await insert.ExecuteScalarAsync(ct));

            await using var last = chat.Work.CreateCommand(
                "UPDATE conversations SET last_message_id = @id, last_message_at = @at WHERE id = @conversation");
            last.Parameters.AddWithValue("@id", id);
            last.Parameters.AddWithValue("@at", at);
            last.Parameters.AddWithValue("@conversation", conversationId);
            await last.ExecuteNonQueryAsync(ct);

            return new MessageRow
            {
                Id = id, PublicId = publicId, CreatedAt = at, ConversationId = conversationId, SenderUserId = senderUserId,
                MessageType = type, Body = body, ReplyToMessageId = replyTo,
            };
        }
    }
}
