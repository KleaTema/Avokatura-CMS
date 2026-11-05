using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Chat.Infrastructure;

/// <summary>
/// The chat's OWN unit of work (task 049): connection ConnectionStrings:Chat (account ash_chat_app, rights on ash_chat
/// only), its own transaction — never the core one. A chat failure cannot roll back a case change, and a case change
/// reaches the chat only after its commit (the event handlers use IUnitOfWork.OnCommittedAsync of the CORE unit).
/// Writes: BeginAsync … CommitAsync in the handler; pushes go to <see cref="MySqlUnitOfWork.OnCommitted"/> of this unit.
/// </summary>
public sealed class ChatUnitOfWork(ChatOptions options) : IAsyncDisposable
{
    public MySqlUnitOfWork Work { get; } = new(options.Persistence);

    public ValueTask DisposeAsync() => Work.DisposeAsync();
}

/// <summary>ConnectionStrings:Chat plus the shared read time limit.</summary>
public sealed record ChatOptions(PersistenceOptions Persistence);

/// <summary>ash_chat (scripts 005, 018): conversations, participants, messages (partitioned by month on created_at).</summary>
public sealed class ChatDbContext(ChatUnitOfWork chat) : ModuleDbContextBase(chat.Work)
{
    public DbSet<ConversationRow> Conversations => Set<ConversationRow>();
    public DbSet<ParticipantRow> Participants => Set<ParticipantRow>();
    public DbSet<MessageRow> Messages => Set<MessageRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationRow>(b =>
        {
            b.ToTable("conversations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.ConversationType).HasColumnName("conversation_type");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.DirectPairKey).HasColumnName("direct_pair_key");
            b.Property(x => x.Title).HasColumnName("title");
            b.Property(x => x.IsReadOnly).HasColumnName("is_read_only");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.LastMessageId).HasColumnName("last_message_id");
            b.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
        });

        modelBuilder.Entity<ParticipantRow>(b =>
        {
            b.ToTable("conversation_participants");
            b.HasKey(x => new { x.ConversationId, x.UserId });
            b.Property(x => x.ConversationId).HasColumnName("conversation_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.JoinedAt).HasColumnName("joined_at");
            b.Property(x => x.LeftAt).HasColumnName("left_at");
            b.Property(x => x.LastReadMessageId).HasColumnName("last_read_message_id");
            b.Property(x => x.LastReadAt).HasColumnName("last_read_at");
        });

        modelBuilder.Entity<MessageRow>(b =>
        {
            b.ToTable("messages");
            b.HasKey(x => new { x.Id, x.CreatedAt });
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.ConversationId).HasColumnName("conversation_id");
            b.Property(x => x.SenderUserId).HasColumnName("sender_user_id");
            b.Property(x => x.MessageType).HasColumnName("message_type");
            b.Property(x => x.Body).HasColumnName("body");
            b.Property(x => x.ReplyToMessageId).HasColumnName("reply_to_message_id");
            b.Property(x => x.EditedAt).HasColumnName("edited_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        });
    }
}

public sealed class ConversationRow
{
    public const string CaseType = "CASE";
    public const string DirectType = "DIRECT";

    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    public string ConversationType { get; set; } = CaseType;
    /// <summary>ash_core.cases.id — a logical reference (no foreign key across schemas).</summary>
    public ulong? CaseId { get; set; }
    public string? DirectPairKey { get; set; }
    public string? Title { get; set; }
    public bool IsReadOnly { get; set; }
    public uint CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public ulong? LastMessageId { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public sealed class ParticipantRow
{
    public ulong ConversationId { get; set; }
    public uint UserId { get; set; }
    public DateTime JoinedAt { get; set; }
    /// <summary>Set when they leave (removed from the case team): no reading, no writing from then on.</summary>
    public DateTime? LeftAt { get; set; }
    /// <summary>Internal message id; only ever moves forward.</summary>
    public ulong LastReadMessageId { get; set; }
    public DateTime? LastReadAt { get; set; }
}

public sealed class MessageRow
{
    public const string Text = "TEXT";
    public const string System = "SYSTEM";

    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    /// <summary>The partition column; set by the application (UTC, milliseconds) so the pushed and stored values match.</summary>
    public DateTime CreatedAt { get; set; }
    public ulong ConversationId { get; set; }
    /// <summary>Null for SYSTEM messages.</summary>
    public uint? SenderUserId { get; set; }
    public string MessageType { get; set; } = Text;
    public string? Body { get; set; }
    public ulong? ReplyToMessageId { get; set; }
    public DateTime? EditedAt { get; set; }
    /// <summary>Soft delete: shown as "Mesazh i fshirë"; the body stays in the database for the record.</summary>
    public DateTime? DeletedAt { get; set; }
}
